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
    check(dances.length >= 8, 'Fewer than 8 dance videos');
    check(new Set(dances.map(d => d.youtubeId)).size === dances.length, 'Duplicate dance video IDs');
    check(dances.every(d => /^[A-Za-z0-9_-]{11}$/.test(d.youtubeId) && d.url === 'https://www.youtube.com/watch?v=' + d.youtubeId), 'A dance entry has a malformed ID or URL');
    check(document.querySelectorAll('[data-dance]').length === dances.length, 'Not every dance has a play button');
    check(!$('iframe'), 'A player was created before the user chose a video');

    $('[data-dance="0"]').click();
    const frame = $('#dance-frame');
    check(frame && frame.src.startsWith(ORIGIN + '/embed/' + dances[0].youtubeId) && frame.src.includes('enablejsapi=1') && frame.src.includes('autoplay=0'), 'Player URL is not the no-cookie embed with the JS API and autoplay off');
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

    tick(40); $('#loop-a').click(); tick(42); $('#loop-b').click();
    check($('#practice-loop').textContent.includes('0:40 ~ 0:42'), 'Manual A-B loop was not set');
    tick(50); $('#loop-a').click(); tick(50.2); $('#loop-b').click();
    check($('#practice-loop').textContent.includes('A 0:50') && !$('#practice-loop').textContent.includes('~'), 'B right after A was accepted');

    tick(60); $('#loop-back').click();
    $('#video-back').click();
    check(!$('iframe') && document.querySelectorAll('[data-dance]').length === dances.length, 'Back did not return to the video list');
    window.danceCommandLog.length = 0;
    tick(90, ORIGIN, window);
    check(window.danceCommandLog.length === 0, 'Message listener leaked after leaving the player');
    return { passed: true, videos: dances.length, speedButtons: true, lastFiveSecondLoop: true, manualAbLoop: true, rejectsForeignMessages: true, cleansUpOnBack: true };
  } catch (error) { return { passed: false, error: error.message }; }
})();
