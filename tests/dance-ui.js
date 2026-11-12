/* Synchronous native WebView2 UI acceptance test for the dance popup.
   The player iframe is third-party, so commands are checked through window.danceCommandLog and playback
   time is injected with synthetic MessageEvents that look like the embed's infoDelivery messages. */
(() => {
  const $ = selector => document.querySelector(selector);
  const check = (condition, message) => { if (!condition) throw new Error(message); };
  const ORIGIN = 'https://www.youtube-nocookie.com';
  const tick = (time, origin = ORIGIN, source = null) => window.dispatchEvent(new MessageEvent('message', {
    data: JSON.stringify({ event: 'infoDelivery', info: { currentTime: time } }), origin, source: source || $('#dance-frame').contentWindow
  }));
  const last = () => window.danceCommandLog[window.danceCommandLog.length - 1];
  try {
    const dances = window.GAME_CONTENT.dances;
    check(dances.length >= 6, 'Fewer than 6 dance videos');
    check(new Set(dances.map(d => d.youtubeId)).size === dances.length, 'Duplicate dance video IDs');
    check(dances.every(d => /^[A-Za-z0-9_-]{11}$/.test(d.youtubeId) && d.url === 'https://www.youtube.com/watch?v=' + d.youtubeId), 'A dance entry has a malformed ID or URL');
    check(document.querySelectorAll('#variant-pick option').length === dances.length, 'Not every dance is in the picker');
    check($('#variant-mode'), 'Order selector is missing');
    check($('#dance-frame'), 'No video started by itself when the card opened');
    check($('#variant-edit'), 'List edit button is missing');

    const pickIndex = i => { const s = $('#variant-pick'); s.value = String(i); s.dispatchEvent(new Event('change')); };
    pickIndex(0);
    const frame = $('#dance-frame');
    check(frame && frame.src.startsWith(ORIGIN + '/embed/' + dances[0].youtubeId) && frame.src.includes('enablejsapi=1') && frame.src.includes('autoplay=1'), 'Player URL is not the no-cookie embed with the JS API and autoplay on');
    check($('#practice-rate') && $('#loop-back') && $('#loop-a') && $('#loop-b') && $('#loop-off'), 'Practice controls are missing');

    document.querySelector('[data-rate="0.5"]').click();
    check(last().func === 'setPlaybackRate' && last().args[0] === 0.5 && $('#practice-rate').textContent === '0.5×', 'Speed button did not set 0.5x');
    document.querySelector('[data-rate="0.25"]').click();
    check(last().args[0] === 0.25, 'Speed button did not set 0.25x');

    tick(20);
    check($('#practice-time').textContent === '0:20', 'Playback time was not shown');
    $('#loop-back').click();
    check($('#practice-loop').textContent.includes('0:15 ~ 0:20'), 'Last-5-seconds loop did not label 0:15~0:20');
    check(window.danceCommandLog.some(c => c.func === 'seekTo' && c.args[0] === 15), 'Last-5-seconds loop did not jump to 0:15');
    window.danceCommandLog.length = 0;
    tick(25);
    check(last() && last().func === 'seekTo' && last().args[0] === 15, 'Playing past B did not jump back to A');
    window.danceCommandLog.length = 0;
    tick(17);
    check(window.danceCommandLog.length === 0, 'Time inside the loop caused a seek');

    window.danceCommandLog.length = 0;
    tick(99, 'https://evil.example');
    tick(99, ORIGIN, window);
    check(window.danceCommandLog.length === 0 && $('#practice-time').textContent !== '1:39', 'Messages from another origin or window were trusted');

    $('#loop-off').click();
    window.danceCommandLog.length = 0;
    tick(30);
    check($('#practice-loop').textContent === '반복 꺼짐' && window.danceCommandLog.length === 0, 'Loop kept running after it was turned off');

    // "반복 안 함" 보장: 반복이 꺼진 상태에서 시간이 크게 뒤로 점프하면(Shorts가 저절로 재시작) 정지해야 한다.
    window.danceCommandLog.length = 0;
    tick(2);
    check(window.danceCommandLog.some(c => c.func === 'pauseVideo'), 'Player restart with loop off was not stopped');
    window.danceCommandLog.length = 0;

    tick(40); $('#loop-a').click(); tick(42); $('#loop-b').click();
    check($('#practice-loop').textContent.includes('0:40 ~ 0:42'), 'Manual A-B loop was not set');
    tick(50); $('#loop-a').click(); tick(50.2); $('#loop-b').click();
    check($('#practice-loop').textContent.includes('A 0:50') && !$('#practice-loop').textContent.includes('~'), 'B right after A was accepted');

    tick(60); $('#loop-back').click();
    pickIndex(1);
    check($('#dance-frame') && $('#dance-frame').src.startsWith(ORIGIN + '/embed/' + dances[1].youtubeId) && $('#dance-frame').src.includes('autoplay=1'), 'Picking another item did not start that video');
    check($('#practice-loop').textContent === '반복 꺼짐', 'Loop state leaked into the next video');
    window.danceCommandLog.length = 0;
    tick(90, ORIGIN, window);
    check(window.danceCommandLog.length === 0, 'Message listener leaked after switching videos');
    return { passed: true, videos: dances.length, speedButtons: true, lastFiveSecondLoop: true, manualAbLoop: true, rejectsForeignMessages: true, cleansUpOnBack: true, autoStarts: true };
  } catch (error) { return { passed: false, error: error.message }; }
})();
