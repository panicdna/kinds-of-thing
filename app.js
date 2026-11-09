/* Local UI. No answers, microphone input, or health details leave the app. */
(() => {
  'use strict';
  const host = window.chrome && window.chrome.webview;
  const root = document.querySelector('#app');
  const core = window.TeumCore;
  const content = window.GAME_CONTENT || { dances: [] };
  let state;
  const defaults = { IntervalMinutes: 60, WeekdaysOnly: true, LimitHours: true, StartHour: 9, StartMinute: 0, EndHour: 18, EndMinute: 0, ThemeIds: ['stretch'], BreakEnabled: false, BreakStartHour: 12, BreakStartMinute: 0, BreakEndHour: 13, BreakEndMinute: 0, Enabled: false, StartAtLogin: false, InhaleSeconds: 4, HoldSeconds: 0, ExhaleSeconds: 6, ThemeMode: 'ordered-one' };
  const symbols = { stretch: '↟', meditation: '◌', voice: '♪', 'back-standing': '↗', dance: '♫', relax: '☾', recipe: '✦' };
  const themeModes = [
    { id: 'ordered-one', label: '하나씩 순서대로', help: '알림마다 하나만, 목록 순서대로 돌아가며' },
    { id: 'random-one', label: '하나만 무작위로', help: '알림마다 하나만, 고른 것 중에서 무작위로' }
  ];
  const modeOf = id => themeModes.find(m => m.id === id) || themeModes[0];
  const symbolFor = t => t.icon || symbols[t.id] || (t.community ? '✿' : '◌');
  const tones = ['#e3e9ce', '#e4deec', '#eddcca', '#d9e6e7'];
  const $ = selector => document.querySelector(selector);
  const escape = value => String(value == null ? '' : value).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  function toast(message) {
    const box = $('#toast'); box.textContent = message; box.classList.add('show');
    const note = $('#share-note'), dialog = $('#share-panel');
    if (note && dialog && dialog.open) note.textContent = message; // a modal dialog covers the page toast
    clearTimeout(toast.timer); toast.timer = setTimeout(() => box.classList.remove('show'), 5500);
  }
  function send(action, values = {}) {
    if (host) { host.postMessage(JSON.stringify({ action, ...values })); return; }
    if (action === 'preview') { window.open(`app.html#popup=${encodeURIComponent(values.themeId)}`, '_blank'); return; }
    if (action === 'run-now') {
      const ids = (state.settings && state.settings.ThemeIds) || [];
      if (!ids.length) return;
      const mode = (state.settings && state.settings.ThemeMode) || 'ordered-one';
      const pick = mode === 'random-one' ? [ids[Math.floor(Math.random() * ids.length)]] : mode === 'ordered-one' ? [ids[0]] : mode === 'random-all' ? ids.slice().sort(() => Math.random() - 0.5) : ids;
      pick.forEach(id => window.open(`app.html#popup=${encodeURIComponent(id)}`, '_blank')); return;
    }
    if (action === 'close') { window.close(); toast('이 창의 오른쪽 위 X를 눌러 닫으세요.'); return; }
    if (action === 'open-link') { window.open(values.url, '_blank', 'noopener,noreferrer'); return; }
    if (action === 'save') {
      state.settings = { ...values.settings, Enabled: false };
      localStorage.setItem('teum-preview-settings', JSON.stringify(state.settings));
      renderSettings(); toast('설정 미리보기를 저장했습니다. 실제 반복 팝업은 DeskHooker.exe에서 시작하세요.'); return;
    }
    if (action === 'exit') window.close();
  }
  function updateStatus(data) {
    if (!state) return;
    const enabled = data.enabled == null ? state.settings.Enabled : data.enabled;
    const next = new Date(data.next || state.next);
    if ($('#status-text')) $('#status-text').textContent = enabled ? '알림 실행 중' : '알림 대기 중';
    if ($('#status-dot')) $('#status-dot').classList.toggle('running', enabled);
    if ($('#next-time')) $('#next-time').textContent = enabled && !Number.isNaN(next.getTime()) ? next.toLocaleTimeString('ko-KR', { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }) : '직접 시작하세요';
    if ($('#next-date')) $('#next-date').textContent = enabled && !Number.isNaN(next.getTime()) ? next.toLocaleDateString('ko-KR', { month: 'long', day: 'numeric', weekday: 'short' }) : '간격과 주제를 선택한 뒤';
  }
  function extraThemes() {
    return [
      { id: 'dance', name: '댄스 안무', summary: '공식 안무 영상으로 원본 동작 따라 보기', instructions: ['공식 연습영상을 고르세요.', '서서 하는 움직임이므로 충분한 공간을 확보하세요.'], safety: '앉아서 하는 운동이 아닙니다. 통증이 있으면 어려운 동작을 건너뛰세요.', file: '' },
      { id: 'relax', name: '릴렉스 음향', summary: '편안한 소리를 들으며 잠깐 쉬기', instructions: [], safety: '소리는 작게 시작하고, 불편하면 바로 끄세요. 운전·보행 중에는 듣지 마세요. 치료가 아닌 짧은 휴식용 안내입니다.', file: '' },
      { id: 'recipe', name: '요리 레시피', summary: '짧은 요리 영상으로 잠깐 쉬어 가기', instructions: [], safety: '영상은 구경하며 쉬는 용도입니다. 실제로 따라 할 때는 불과 칼을 조심하고, 알레르기나 식이 제한이 있으면 재료를 직접 확인하세요.', file: '' }
    ];
  }
  // Second checkbox of every card: start this card at an exact time of day (HH:MM:SS), on top of the interval rotation.
  function cardTimeRow(t) {
    const cs = cardSchedule(t.id);
    return `<div class="card-time"><label class="card-time-on"><input type="checkbox" data-ct-on="${escape(t.id)}" ${cs.Enabled ? 'checked' : ''}>정해진 시각에 시작</label><input class="time24 time-sec" type="text" data-ct-time="${escape(t.id)}" inputmode="numeric" maxlength="8" placeholder="HH:MM:SS" value="${escape(cs.Time || '12:00:00')}" ${cs.Enabled ? '' : 'disabled'} aria-label="${escape(t.name)} 시작 시각 (24시간, 시:분:초)"></div>`;
  }
  function themeCards(checked) {
    return state.themes.map((t, i) => `<article class="theme-card" style="--tone:${tones[i % tones.length]}"><label><div class="theme-top"><span class="theme-symbol">${symbolFor(t)}</span><span class="rotation-label">주기 실행</span><input name="theme" type="checkbox" value="${escape(t.id)}" ${checked.has(t.id) ? 'checked' : ''} aria-label="${escape(t.name)} 주제 선택"></div><h3>${escape(t.name)}</h3><p>${escape(t.summary)}</p></label><div class="card-actions"><button class="preview" data-preview="${escape(t.id)}" type="button">미리 보기 ↗</button>${t.community && t.id !== 'preview' ? `<button class="card-delete" data-delete="${escape(t.id)}" type="button" title="공유 저장소(GitHub)에서 이 쉼을 지우는 화면을 엽니다">삭제하기</button>` : ''}</div>${t.id === 'preview' ? '' : cardTimeRow(t)}</article>`).join('');
  }
  function syncText() {
    const sync = state.sync || {};
    if (sync.offline) return '이 환경에서는 공유 동기화를 쓰지 않아요.';
    if (sync.syncing) return '공유된 쉼을 불러오는 중…';
    const when = sync.last ? new Date(sync.last).toLocaleTimeString('ko-KR', { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }) : '';
    const rejected = sync.rejected ? ` · 규칙에 맞지 않아 건너뜀 ${sync.rejected}개` : '';
    if (sync.error) return `${sync.error} (저장해 둔 ${sync.count || 0}개는 그대로 써요)`;
    return `${sync.count ? `공유된 쉼 ${sync.count}개` : '아직 공유된 쉼이 없어요'}${when ? ` · ${when}에 확인` : ''}${rejected} · 약 10분마다 자동 확인`;
  }
  function updateSync() {
    if ($('#sync-text')) $('#sync-text').textContent = syncText();
    if ($('#sync-now')) $('#sync-now').disabled = Boolean(state.sync && state.sync.syncing);
  }
  function bindThemeGrid() {
    document.querySelectorAll('[data-preview]').forEach(button => button.addEventListener('click', () => send('preview', { themeId: button.dataset.preview })));
    // Deleting a shared item = proposing to delete its file in the shared repository (GitHub asks for login and review).
    document.querySelectorAll('[data-delete]').forEach(button => button.addEventListener('click', () => {
      const url = core.buildDeleteUrl(button.dataset.delete);
      if (!url) { toast('이 쉼은 삭제 요청을 만들 수 없어요.'); return; }
      send('open-link', { url });
      toast('GitHub의 삭제 화면이 열립니다. 로그인 후 "Commit changes"로 삭제를 제안하세요. 병합되면 모든 앱에서 사라져요.');
    }));
    document.querySelectorAll('[data-ct-on]').forEach(box => box.addEventListener('change', () => {
      const input = document.querySelector(`[data-ct-time="${box.dataset.ctOn}"]`);
      input.disabled = !box.checked;
      setCardSchedule(box.dataset.ctOn, { Enabled: box.checked });
    }));
    document.querySelectorAll('[data-ct-time]').forEach(input => {
      const settle = () => { const t = parseClock(input.value); if (t) { input.value = t; setCardSchedule(input.dataset.ctTime, { Time: t }); } };
      input.addEventListener('blur', settle); input.addEventListener('change', settle);
    });
    const count = () => {
      const n = document.querySelectorAll('input[name=theme]:checked').length;
      const mode = modeOf((document.querySelector('input[name=theme-mode]:checked') || {}).value);
      $('#selected-count').textContent = `주기 실행 ${n}개 · ${mode.label}`;
      $('#theme-mode-help').textContent = n > 1 ? mode.help : '두 개 이상 고르면 아래 방식으로 실행돼요.';
    };
    document.querySelectorAll('input[name=theme], input[name=theme-mode]').forEach(input => input.addEventListener('change', count)); count();
    // A card is either in the interval rotation or starts at a fixed time, never both: turning one on turns the other off.
    const tileOf = el => el.closest('.theme-card');
    document.querySelectorAll('[data-ct-on]').forEach(box => box.addEventListener('change', () => {
      if (!box.checked) return;
      const rotation = tileOf(box).querySelector('input[name=theme]');
      if (rotation && rotation.checked) { rotation.checked = false; count(); }
    }));
    document.querySelectorAll('input[name=theme]').forEach(rotation => rotation.addEventListener('change', () => {
      if (!rotation.checked) return;
      const box = tileOf(rotation).querySelector('[data-ct-on]');
      if (box && box.checked) { box.checked = false; tileOf(rotation).querySelector('[data-ct-time]').disabled = true; setCardSchedule(box.dataset.ctOn, { Enabled: false }); }
    }));
  }
  // Rebuilds only the tiles, so choices the user ticked but has not saved yet survive a background sync.
  function refreshThemeGrid() {
    const grid = $('#theme-grid'); if (!grid) return;
    const checked = new Set(Array.from(document.querySelectorAll('input[name=theme]:checked'), x => x.value));
    const known = new Set(state.themes.map(t => t.id));
    const keep = new Set(Array.from(checked).filter(id => known.has(id)));
    if (!keep.size) state.settings.ThemeIds.filter(id => known.has(id)).forEach(id => keep.add(id));
    grid.innerHTML = themeCards(keep); bindThemeGrid(); updateSync();
  }
  function sharePanel() {
    const L = core.LIMITS;
    return `<div class="share-launch"><button id="share-open" class="secondary" type="button">내 쉼 공유하기 · GitHub에 올려 다른 사람 앱에도 나타나게 ↗</button></div>
      <dialog class="share-panel" id="share-panel"><div class="share-head"><h2>내 쉼 공유하기</h2><button id="share-close" class="quiet" type="button">닫기 ×</button></div>
      <details class="share-guide" open><summary>사용 방법과 예제</summary>
        <ol><li>아래 칸을 채우면 오른쪽 <b>미리 보기</b>에 다른 사람 화면이 그대로 나와요.</li>
        <li><b>GitHub에 올리기</b>를 누르면 새 파일 화면이 채워진 채 열려요. GitHub에 로그인해 <b>Propose new file</b>로 제안(Pull Request)을 보내세요. 앱은 계정·토큰을 다루지 않아요.</li>
        <li>저장소 관리자가 병합하면 모든 사용자의 앱이 약 10분 안에 받아 보여 줘요.</li></ol>
        <p class="small-help"><b>예제</b> — 이름 "목 풀기 한 번", 설명 "고개를 천천히 기울여 목 옆을 부드럽게 푸는 쉼", 동작 안내 4줄, 주의사항 1줄. <button id="share-example" class="quiet" type="button">예제 채우기</button></p>
        <p class="small-help"><b>유튜브 영상도 넣을 수 있어요.</b> 위 '유튜브 영상' 칸에 주소를 한 줄에 하나씩(최대 5개) 적으세요. <b>유튜브 주소만</b> 되고, 파일에는 주소 대신 영상 ID만 저장돼요. 앱은 받을 때마다 영상이 지금도 재생되는지 접속해 확인하고, 재생할 수 없는 영상이 있으면 그 쉼을 받지 않아요. 자세한 방법은 설명서에 있어요. <button id="share-guide-open" class="quiet" type="button">전체 설명서 열기 ↗</button></p>
      </details>
      <p id="share-note" class="share-note" role="status" aria-live="polite"></p>
      <div class="share-grid"><form id="share-form" class="share-form" autocomplete="off">
        <label>이름 <small>${L.name}자</small><input id="share-name" maxlength="${L.name}" placeholder="예: 목 풀기 한 번"></label>
        <label>한 줄 설명 <small>${L.summary}자</small><input id="share-summary" maxlength="${L.summary}" placeholder="타일에 보이는 짧은 설명"></label>
        <label>동작 안내 <small>한 줄에 하나 · ${L.steps}줄까지 · 줄당 ${L.step}자</small><textarea id="share-steps" rows="5" placeholder="고개를 천천히 숙여요&#10;반대쪽으로 천천히 기울여요"></textarea></label>
        <label>유튜브 영상 <small>선택 · 한 줄에 하나 · ${L.videos}개까지 · 주소 | 제목</small><textarea id="share-videos" rows="3" placeholder="https://www.youtube.com/shorts/영상ID | 영상 제목(선택)"></textarea></label>
        <fieldset class="share-icons"><legend>아이콘 <small>타일에 보이는 모양</small></legend><div class="icon-options">${core.ICONS.map((icon, i) => `<label class="icon-option"><input type="radio" name="share-icon" value="${icon}" ${i === 0 ? 'checked' : ''}><span>${icon}</span></label>`).join('')}</div></fieldset>
        <label>주의사항 <small>${L.safety}자 · 필수</small><textarea id="share-safety" rows="2" maxlength="${L.safety}" placeholder="통증·어지럼이 생기면 바로 중단하세요."></textarea></label>
        <label>표시할 이름 <small>선택 · ${L.author}자 · 공개됩니다</small><input id="share-author" maxlength="${L.author}" placeholder="비워 두면 익명"></label>
        <div class="share-buttons"><button id="share-github" class="primary" type="submit">GitHub에 올리기 ↗</button><button id="share-copy" class="secondary" type="button">JSON 복사</button></div>
      </form>
      <aside><div class="eyebrow">미리 보기 · 다른 사람 화면</div><div id="share-preview" class="themes one"></div><p class="small-help">링크·HTML 기호(&lt; &gt;)는 쓸 수 없어요. 올린 내용은 공개되며 치료·진단 주장은 받지 않아요. 개인정보는 넣지 마세요.</p></aside></div></dialog>`;
  }
  function readShareForm() {
    return { name: $('#share-name').value, summary: $('#share-summary').value, steps: $('#share-steps').value, videos: $('#share-videos').value, icon: (document.querySelector('input[name=share-icon]:checked') || {}).value, safety: $('#share-safety').value, author: $('#share-author').value };
  }
  function renderSharePreview() {
    const result = core.validateShare(readShareForm());
    const item = result.item;
    const theme = { id: 'preview', community: true, icon: item.icon, name: item.name || '(이름)', summary: item.summary || '(한 줄 설명)', author: item.author };
    const old = state.themes; state.themes = [theme];
    $('#share-preview').innerHTML = themeCards(new Set()).replace(/<button class="preview".*?<\/button>/, '');
    state.themes = old;
  }
  function bindShare() {
    const dlg = $('#share-panel');
    $('#share-open').addEventListener('click', () => { if (dlg.showModal) dlg.showModal(); else dlg.setAttribute('open', ''); });
    $('#share-close').addEventListener('click', () => dlg.close());
    $('#share-guide-open').addEventListener('click', () => send('open-link', { url: 'https://github.com/panicdna/kinds-of-thing/blob/main/%EC%89%BC-%EC%B6%94%EA%B0%80-%EA%B0%80%EC%9D%B4%EB%93%9C.md' }));
    $('#share-example').addEventListener('click', () => {
      const example = { name: '목 풀기 한 번', summary: '고개를 천천히 기울여 목 옆을 부드럽게 푸는 쉼', steps: '의자에 등을 기대고 어깨의 힘을 빼요.\n고개를 오른쪽으로 천천히 기울여 5~10초 쉬어요.\n가운데로 돌아와 왼쪽도 같은 방법으로 해요.\n손으로 당기지 않고 숨은 편하게 쉬어요.', safety: '통증·저림·어지럼이 생기면 바로 멈추세요. 치료가 아닌 짧은 휴식용 안내입니다.', author: '' };
      for (const [key, value] of Object.entries(example)) { const input = $(`#share-${key}`); input.value = value; input.dispatchEvent(new Event('input')); }
      toast('예제를 채웠어요. 미리 보기를 확인하고 내용에 맞게 고쳐 쓰세요.');
    });
    dlg.addEventListener('click', event => { if (event.target === dlg) dlg.close(); });
    ['name', 'summary', 'steps', 'videos', 'safety', 'author'].forEach(key => $(`#share-${key}`).addEventListener('input', renderSharePreview));
    document.querySelectorAll('input[name=share-icon]').forEach(radio => radio.addEventListener('change', renderSharePreview));
    renderSharePreview();
    const validated = () => {
      const result = core.validateShare(readShareForm());
      if (!result.ok) { toast(result.errors[0]); return null; }
      return result.item;
    };
    $('#share-form').addEventListener('submit', event => {
      event.preventDefault();
      const item = validated(); if (!item) return;
      try { send('open-link', { url: core.buildShareUrl(item) }); toast('GitHub 화면이 열립니다. 로그인 후 "Propose new file"로 제안을 보내 주세요.'); }
      catch (error) { toast(error.message); }
    });
    $('#share-copy').addEventListener('click', async () => {
      const item = validated(); if (!item) return;
      try { await navigator.clipboard.writeText(core.shareJson(item)); toast(`복사했어요. community/${item.id}.json 파일로 올리면 돼요.`); }
      catch (_) { toast('복사하지 못했어요. GitHub에 올리기를 이용해 주세요.'); }
    });
  }
  function renderSettings() {
    const s = state.settings;
    root.innerHTML = `<div class="shell">
      <aside class="sidebar"><div class="brand">틈<small>TEUM · A LITTLE RESET</small></div>
      <p class="side-note">몰입하는 시간 사이,<br>나를 돌보는 작은 틈.</p>
      <div class="side-badge"><i id="status-dot" class="dot"></i><span id="status-text"></span><br><span style="font-size:11px;color:#7d8c76">설정 창을 닫아도 트레이에 남아요.</span></div>
      <div class="next-label">다음 작은 쉼</div><div id="next-time" class="next-time"></div><div id="next-date" class="small-help"></div>
      <fieldset class="mode-box"><legend>쉼 여러 개를 골랐을 때 실행 방식</legend>
        <div class="mode-options">${themeModes.map(m => `<label><input type="radio" name="theme-mode" value="${m.id}" ${modeOf(s.ThemeMode).id === m.id ? 'checked' : ''}>${m.label}</label>`).join('')}</div>
        <p id="theme-mode-help" class="small-help"></p></fieldset>
      <div class="side-bottom">내 PC 안에서만 동작합니다.<br>마이크·카메라·계정이 필요 없어요.<button id="hide-window" class="quiet">설정 창 닫기 / 트레이에 두기 ↗</button><button id="exit-app" class="quiet">알림기 완전히 종료</button></div></aside>
      <main class="main"><div class="heading"><div><div class="eyebrow">YOUR PERSONAL BREAK RITUAL</div><h1>잠깐, 나를 돌보는 시간.</h1><p>알림 간격과 하고 싶은 주제를 골라 주세요. 여러 개를 고르면 순서대로 만나요.</p></div></div>
      ${!host ? '<div class="native-warning">HTML 미리보기입니다. 창을 닫은 뒤에도 정해진 간격으로 전체 화면을 띄우려면 같은 폴더의 DeskHooker.exe를 실행하세요.</div>' : ''}
      <div class="settings-row"><section class="setting-card"><h2><span class="section-no">01</span>어느 간격으로 만날까요?</h2><div class="interval-display"><input id="start-time" class="time24" type="text" inputmode="numeric" maxlength="5" placeholder="HH:MM" value="${hm(s.StartHour, s.StartMinute)}" aria-label="시작 시각 (24시간, 시:분)"><span>부터</span><input id="interval" type="number" min="1" max="240" value="${s.IntervalMinutes}" aria-label="팝업 간격, 분"><span>분마다</span></div><p class="small-help">시작 시각은 24시간제(시:분, 예: 13:30)로, 간격은 1~240 사이의 분으로 직접 입력하세요.<br>아래 ‘시간대에만 알림 받기’를 켜면 이 시각부터 받아요.<br>팝업을 닫은 시점부터 다음 간격을 다시 셉니다.</p><button id="run-now" class="secondary" type="button" style="margin-top:14px">▶ 즉시 실행 (시험용)</button><p class="small-help">시간대·요일·일시정지와 관계없이, 저장된 주제 순서대로 팝업을 바로 띄웁니다.</p></section>
      <section class="setting-card"><h2><span class="section-no">02</span>언제 알림을 받을까요?</h2><label class="schedule-line"><input id="weekdays" type="checkbox" ${s.WeekdaysOnly ? 'checked' : ''}>월요일부터 금요일까지만</label><label class="schedule-line"><input id="limit-hours" type="checkbox" ${s.LimitHours ? 'checked' : ''}>시작·종료 시각 사이에만 알림 받기</label><div class="schedule-line">종료 <input id="end-time" class="time24" type="text" inputmode="numeric" maxlength="5" placeholder="HH:MM" value="${hm(s.EndHour, s.EndMinute)}" aria-label="종료 시각 (24시간, 시:분)"> 전까지 (24시간제 · 24:00 또는 00:00은 자정)</div><label class="schedule-line"><input id="break-on" type="checkbox" ${s.BreakEnabled ? 'checked' : ''}>중간에 쉬는 시간(예: 점심)에는 알림 끄기</label><div class="schedule-line break-times">쉬는 시간 <input id="break-start" class="time24" type="text" inputmode="numeric" maxlength="5" placeholder="HH:MM" value="${hm(s.BreakStartHour, s.BreakStartMinute)}" aria-label="쉬는 시간 시작 (24시간, 시:분)" ${s.BreakEnabled ? '' : 'disabled'}> 부터 <input id="break-end" class="time24" type="text" inputmode="numeric" maxlength="5" placeholder="HH:MM" value="${hm(s.BreakEndHour, s.BreakEndMinute)}" aria-label="쉬는 시간 끝 (24시간, 시:분)" ${s.BreakEnabled ? '' : 'disabled'}> 까지</div><label class="schedule-line"><input id="login-start" type="checkbox" ${s.StartAtLogin ? 'checked' : ''}>Windows 로그인 때 자동 실행</label><p class="small-help">PC의 현지 시각 기준 · 잠금/절전 중에는 쉬어요.<br>알림을 놓쳐도 한꺼번에 몰아서 띄우지 않아요.</p></section></div>
      <div class="section-head"><h2><span class="section-no">03</span>어떤 작은 쉼이 필요하세요?</h2><span id="selected-count"></span></div>
      <div class="sync-bar"><span id="sync-text"></span><button id="sync-now" class="quiet" type="button">지금 동기화 ↻</button></div>
      <p class="small-help card-help"><b>주기 실행</b>: 체크한 쉼이 위에서 정한 간격마다 돌아가며 나와요. <b>정해진 시각에 시작</b>: 체크하고 시:분:초(24시간제)를 적으면 매일 그 시각에 그 쉼만 따로 떠요(간격·시간대·쉬는 시간과 무관, '월~금만'과 일시정지는 따릅니다). <b>한 카드에는 둘 중 하나만</b> 켤 수 있어요.</p>
      <div id="theme-grid" class="themes">${themeCards(new Set(s.ThemeIds.filter(id => !(((s.CardSchedules || {})[id]) || {}).Enabled)))}</div>
      ${sharePanel()}
      <div class="actions"><button id="start-reminders" class="primary">${s.Enabled ? '변경 적용 · 알림 계속' : '선택한 주제로 알림 시작'} →</button><button id="save-settings" class="secondary">설정만 저장</button><button id="pause-reminders" class="quiet">알림 일시정지</button><small>팝업은 저절로 닫히지 않아요.<br>닫기 또는 Esc를 누르면 다음 쉼까지 대기해요.</small></div>
      <p class="small-help">일반적인 휴식·움직임 안내이며 치료·재활 처방이 아닙니다. 통증이 늘거나 저림·어지럼이 생기면 중단하세요. 허리 통증과 함께 새로 생긴 다리 힘 빠짐, 회음부 감각 저하, 대소변 조절 변화가 있으면 운동보다 즉시 진료가 우선입니다.</p>
      </main></div>`;
    bindThemeGrid(); bindShare(); updateSync();
    ['#start-time', '#end-time', '#break-start', '#break-end'].forEach(sel => $(sel).addEventListener('blur', () => { const t = parseTime($(sel).value); if (t) $(sel).value = hm(t[0], t[1]); }));
    $('#break-on').addEventListener('change', event => { $('#break-start').disabled = $('#break-end').disabled = !event.target.checked; });
    $('#run-now').addEventListener('click', () => send('run-now'));
    $('#sync-now').addEventListener('click', () => { if (!host) { toast('공유 동기화는 DeskHooker.exe에서 동작해요. 공유하기는 지금도 쓸 수 있어요.'); return; } send('sync'); });
    $('#start-reminders').addEventListener('click', () => save(true));
    $('#save-settings').addEventListener('click', () => save(s.Enabled));
    $('#pause-reminders').addEventListener('click', () => save(false));
    $('#hide-window').addEventListener('click', () => send('close'));
    $('#exit-app').addEventListener('click', () => send('exit'));
    updateStatus(state);
  }
  function hm(h, m) { return String(h).padStart(2, '0') + ':' + String(m || 0).padStart(2, '0'); }
  // 24-hour "HH:MM" (also accepts "930" or "0930"); hour 0-24, minute 0-59, and 24 only as 24:00. Returns [h, m] or null.
  function parseTime(v) {
    let text = String(v || '').trim();
    if (/^\d{3,4}$/.test(text)) text = text.slice(0, -2) + ':' + text.slice(-2);
    const m = /^(\d{1,2}):(\d{2})$/.exec(text);
    if (!m) return null;
    const h = Number(m[1]), min = Number(m[2]);
    return h > 24 || min > 59 || (h === 24 && min !== 0) ? null : [h, min];
  }
  // "HH:MM:SS" (also "9:05", "930", "093000"); hour 0-23, minute and second 0-59. Returns the normalised text or null.
  function parseClock(v) {
    let text = String(v || '').trim();
    if (/^\d{4}$/.test(text)) text = text.slice(0, 2) + ':' + text.slice(2) + ':00';
    else if (/^\d{6}$/.test(text)) text = text.slice(0, 2) + ':' + text.slice(2, 4) + ':' + text.slice(4);
    const m = /^(\d{1,2}):(\d{2})(?::(\d{2}))?$/.exec(text);
    if (!m) return null;
    const h = Number(m[1]), min = Number(m[2]), sec = Number(m[3] || 0);
    if (h > 23 || min > 59 || sec > 59) return null;
    return [h, min, sec].map(x => String(x).padStart(2, '0')).join(':');
  }
  const cardSchedule = id => ((state.settings && state.settings.CardSchedules) || {})[id] || { Enabled: false, Time: '12:00:00' };
  function setCardSchedule(id, patch) {
    const all = { ...((state.settings && state.settings.CardSchedules) || {}) };
    all[id] = { ...cardSchedule(id), ...patch };
    state.settings = { ...state.settings, CardSchedules: all };
  }
  function save(enabled) {
    const st = parseTime($('#start-time').value), et = parseTime($('#end-time').value);
    if (!st || !et || st[0] > 23) { toast('시작·종료 시각을 24시간제 시:분으로 입력하세요. (예: 09:00, 18:30)'); return; }
    // Quiet period (e.g. lunch): only used when switched on, so a half-typed value never blocks saving while it is off.
    const breakOn = $('#break-on').checked;
    const breakStart = parseTime($('#break-start').value) || [defaults.BreakStartHour, defaults.BreakStartMinute];
    const breakEnd = parseTime($('#break-end').value) || [defaults.BreakEndHour, defaults.BreakEndMinute];
    if (breakOn) {
      if (!parseTime($('#break-start').value) || !parseTime($('#break-end').value) || breakStart[0] > 23) { toast('쉬는 시간을 24시간제 시:분으로 입력하세요. (예: 12:00 ~ 13:00)'); return; }
      if (breakEnd[0] * 60 + breakEnd[1] <= breakStart[0] * 60 + breakStart[1]) { toast('쉬는 시간은 끝이 시작보다 늦어야 해요. (자정까지는 24:00)'); return; }
    }
    const settings = {
      IntervalMinutes: Number($('#interval').value), WeekdaysOnly: $('#weekdays').checked, LimitHours: $('#limit-hours').checked,
      StartHour: st[0], StartMinute: st[1], EndHour: (et[0] === 0 && et[1] === 0) || et[0] === 24 ? 24 : et[0], EndMinute: (et[0] === 0 && et[1] === 0) || et[0] === 24 ? 0 : et[1], StartAtLogin: $('#login-start').checked,
      BreakEnabled: $('#break-on').checked, BreakStartHour: breakStart[0], BreakStartMinute: breakStart[1], BreakEndHour: breakEnd[0], BreakEndMinute: breakEnd[1],
      ThemeIds: Array.from(document.querySelectorAll('input[name=theme]:checked'), x => x.value), Enabled: enabled,
      ThemeMode: (document.querySelector('input[name=theme-mode]:checked') || {}).value || 'ordered-one'
    };
    if (!Number.isInteger(settings.IntervalMinutes) || settings.IntervalMinutes < 1 || settings.IntervalMinutes > 240) { toast('간격은 1~240분 사이의 정수로 고르세요.'); return; }
    const schedules = {};
    for (const box of document.querySelectorAll('[data-ct-on]')) {
      const id = box.dataset.ctOn, field = document.querySelector(`[data-ct-time="${id}"]`);
      const time = parseClock(field.value);
      if (box.checked && !time) { toast('정해진 시각은 24시간제 시:분:초로 입력하세요. (예: 12:00:30)'); return; }
      schedules[id] = { Enabled: box.checked, Time: time || '12:00:00' };
    }
    settings.CardSchedules = schedules;
    const hasFixed = Object.values(schedules).some(x => x.Enabled);
    if (!settings.ThemeIds.length && !hasFixed) { toast('주기 실행에 넣을 쉼을 하나 이상 고르거나, 정해진 시각에 시작할 쉼을 켜 주세요.'); return; }
    if (!Number.isInteger(settings.StartHour) || !Number.isInteger(settings.EndHour) || settings.StartHour < 0 || settings.StartHour > 23 || settings.EndHour < 1 || settings.EndHour > 24 || settings.StartHour * 60 + settings.StartMinute >= settings.EndHour * 60 + settings.EndMinute) { toast('종료 시각은 시작 시각보다 늦어야 해요. (자정까지는 00:00)'); return; }
    if (breakOn) {
      const minutes = (h, m) => h * 60 + m;
      const allowedStart = settings.LimitHours ? minutes(settings.StartHour, settings.StartMinute) : 0;
      const allowedEnd = settings.LimitHours ? minutes(settings.EndHour, settings.EndMinute) : 1440;
      if (minutes(breakStart[0], breakStart[1]) <= allowedStart && minutes(breakEnd[0], breakEnd[1]) >= allowedEnd) { toast('쉬는 시간이 알림을 받는 시간 전체를 덮고 있어요. 범위를 줄여 주세요.'); return; }
    }
    send('save', { settings });
  }
  function popupFrame(theme) {
    return `<main class="popup"><header class="popup-bar"><div class="popup-brand">틈<span>A LITTLE RESET · ${escape(theme.name)}</span></div>${state.remaining > 0 ? `<button class="popup-next" type="button">다음 쉼 ›<span style="font-size:10px;opacity:.6"> ${state.remaining}개 남음</span></button>` : ''}<button class="popup-close" type="button">닫기 × <span style="font-size:10px;opacity:.6">Esc</span></button></header><div class="popup-content"><div class="popup-heading"><div class="eyebrow">잠깐 쉬어가도 괜찮아요</div><h1>${escape(theme.name)}</h1><p>${escape(theme.summary)}</p></div><div id="popup-stage"></div></div><footer class="popup-footer"><span>창을 닫으면 다음 알림까지 대기해요. 자동으로 닫거나 입력을 강요하지 않아요.</span><span>움직임은 편한 범위에서 · 통증·저림·어지럼이 생기면 중단</span></footer></main>`;
  }
  function renderPopup() {
    const theme = state.themes.find(t => t.id === state.themeId) || state.themes[0];
    root.innerHTML = popupFrame(theme);
    $('.popup-close').addEventListener('click', () => { send('close'); });
    const nextBtn = $('.popup-next');
    if (nextBtn) nextBtn.addEventListener('click', () => { send('next'); });
    if (theme.id === 'dance') renderDances();
    else renderWellness(theme);
  }
  // Some themes have a list of sub-items (theme-variants.js): shown in order or at random, or picked directly.
  const validVariant = v => v.type === 'gif' || v.type === 'text' || /^[A-Za-z0-9_-]{11}$/.test(v.youtubeId || '');
  function variantList(theme) {
    const fixed = (window.THEME_VARIANTS || {})[theme.id] || [];
    // Videos shared with a community item (ids only, checked when the item was downloaded). Its own steps stay as item #1.
    const shared = (theme.videos || []).map(v => ({ id: 'yt-' + v.id, type: 'video', title: v.title, author: v.author || '', youtubeId: v.id, url: 'https://www.youtube.com/watch?v=' + v.id }));
    if (shared.length && (theme.instructions || []).length && !fixed.length) shared.unshift({ id: 'text', type: 'text', title: '기본 · 동작 안내', author: '' });
    return fixed.concat(shared).filter(validVariant);
  }
  const store = {
    get(key) { try { return localStorage.getItem(key); } catch (_) { return null; } },
    set(key, value) { try { localStorage.setItem(key, value); } catch (_) {} }
  };
  // Per-card order of sub-items: random unless the user chose "sequential"; the choice lives in settings.json (VariantModes).
  function variantMode(id) {
    const modes = (state && state.settings && state.settings.VariantModes) || {};
    return modes[id] === 'sequential' ? 'sequential' : 'random';
  }
  function setVariantMode(id, mode) {
    const value = mode === 'sequential' ? 'sequential' : 'random';
    state.settings = { ...state.settings, VariantModes: { ...((state.settings && state.settings.VariantModes) || {}), [id]: value } };
    send('save-variant-mode', { themeId: id, mode: value });
  }
  function pickVariant(id, count) {
    const last = Number(store.get('teum.variantIdx.' + id));
    let idx = 0;
    if (count > 1) {
      if (variantMode(id) === 'random') { do { idx = Math.floor(Math.random() * count); } while (idx === last); }
      else idx = Number.isInteger(last) && store.get('teum.variantIdx.' + id) !== null ? (last + 1) % count : 0;
    }
    store.set('teum.variantIdx.' + id, String(idx));
    return idx;
  }
  function variantPicker(id, list, current) {
    return `<div class="stretch-picker"><div class="stretch-pick-top"><b>${current + 1} / ${list.length}</b><label>보여 주는 순서 <select id="variant-mode"><option value="random" ${variantMode(id) === 'random' ? 'selected' : ''}>무작위</option><option value="sequential" ${variantMode(id) === 'sequential' ? 'selected' : ''}>순서대로</option></select></label><span class="small-help">바꾸면 이 카드에 기억돼요(다음 알림부터 적용). 아래에서 바로 골라도 돼요.</span></div><div class="stretch-pick-list">${list.map((v, i) => `<button type="button" class="${i === current ? 'primary' : 'secondary'}" data-variant="${i}" aria-pressed="${i === current}">#${i + 1} ${escape(v.title)}</button>`).join('')}</div></div>`;
  }
  function renderWellness(theme, forcedIdx) {
    const list = variantList(theme);
    let current = 0, video = null;
    if (list.length >= 1) {
      current = Number.isInteger(forcedIdx) ? forcedIdx : pickVariant(theme.id, list.length);
      if (Number.isInteger(forcedIdx)) store.set('teum.variantIdx.' + theme.id, String(current));
      if (list[current].type === 'video') video = list[current];
    }
    const picker = list.length > 1 ? variantPicker(theme.id, list, current) : '';
    const videoStage = video ? `<div class="video-stage"><iframe id="variant-frame" class="${video.wide ? 'wide-player' : 'short-player'}" title="${escape(video.title)}" src="https://www.youtube-nocookie.com/embed/${video.youtubeId}?autoplay=1&rel=0&playsinline=1${video.wide ? '' : `&loop=1&playlist=${video.youtubeId}`}" referrerpolicy="strict-origin-when-cross-origin" allow="autoplay; encrypted-media; fullscreen; picture-in-picture" allowfullscreen></iframe><p class="video-credit">${escape(video.title)} · ${escape(video.author)} · 재생기로만 보여 줘요 <button id="variant-external" class="quiet" type="button">YouTube에서 열기 ↗</button></p><p class="video-safety">${escape(theme.safety)}</p></div>` : '';
    const visual = theme.file ? `<img class="motion-image" src="${escape(theme.file)}" alt="${escape(theme.name)} 따라 하기 움직이는 그림">` : `<div class="text-stage"><div class="text-symbol" aria-hidden="true">${symbolFor(theme)}</div></div>`;
    $('#popup-stage').innerHTML = video ? `${picker}${videoStage}` : `${picker}<div class="wellness-layout"><div class="motion-stage">${visual}</div><aside class="instruction-panel"><div class="eyebrow">천천히 따라 해 보세요</div><ol>${(theme.instructions || []).map(line => `<li>${escape(line)}</li>`).join('')}</ol><div class="safety">${escape(theme.safety)}</div></aside></div>`;
    document.querySelectorAll('[data-variant]').forEach(button => button.addEventListener('click', () => renderWellness(theme, Number(button.dataset.variant))));
    if ($('#variant-mode')) $('#variant-mode').addEventListener('change', event => setVariantMode(theme.id, event.target.value));
    if ($('#variant-external')) $('#variant-external').addEventListener('click', () => send('open-link', { url: video.url }));
  }
  function practiceControls() {
    const speeds = [0.25, 0.5, 0.75, 1].map(rate => `<button class="quiet" data-rate="${rate}">${rate}×</button>`).join('');
    return `<div class="practice-panel"><div class="practice-row"><b>속도</b>${speeds}<span id="practice-rate" class="practice-state">1×</span></div><div class="practice-row"><b>구간 반복</b><button class="quiet" id="loop-back">최근 5초 반복</button><button class="quiet" id="loop-a">여기를 시작(A)</button><button class="quiet" id="loop-b">여기를 끝(B)</button><button class="quiet" id="loop-off">반복 끄기</button><span id="practice-loop" class="practice-state">반복 꺼짐</span></div><div class="practice-row"><b>이동</b><button class="quiet" id="seek-back">⏪ 3초</button><button class="quiet" id="seek-fwd">3초 ⏩</button><span id="practice-time" class="practice-state">0:00</span></div></div>`;
  }
  // Remote-controls the embedded player over postMessage (enablejsapi=1); no external script is loaded.
  function bindPractice() {
    const frame = $('#dance-frame');
    const origin = 'https://www.youtube-nocookie.com';
    const state = { time: 0, a: null, b: null, rate: 1 };
    const fmt = t => `${Math.floor(t / 60)}:${String(Math.floor(t % 60)).padStart(2, '0')}`;
    window.danceCommandLog = []; // inspected by tests/dance-ui.js
    const command = (func, args = []) => { window.danceCommandLog.push({ func, args }); if (frame.contentWindow) frame.contentWindow.postMessage(JSON.stringify({ event: 'command', func, args }), origin); };
    const label = () => { $('#practice-loop').textContent = state.a == null ? '반복 꺼짐' : state.b == null ? `A ${fmt(state.a)} 지정됨 · B를 정하세요` : `${fmt(state.a)} ~ ${fmt(state.b)} 반복 중`; };
    const startListening = () => { if (frame.contentWindow) frame.contentWindow.postMessage(JSON.stringify({ event: 'listening', id: 1, channel: 'widget' }), origin); };
    const onMessage = event => {
      if (event.origin !== origin || event.source !== frame.contentWindow) return;
      let data; try { data = typeof event.data === 'string' ? JSON.parse(event.data) : event.data; } catch (e) { return; }
      const info = data && (data.event === 'infoDelivery' ? data.info : data.event === 'initialDelivery' ? data.info : null);
      if (!info || typeof info.currentTime !== 'number') return;
      state.time = info.currentTime;
      $('#practice-time').textContent = fmt(state.time);
      if (state.a != null && state.b != null && (state.time >= state.b || state.time < state.a - 1)) command('seekTo', [state.a, true]);
    };
    window.addEventListener('message', onMessage);
    frame.addEventListener('load', startListening);
    const timer = setInterval(startListening, 4000); // re-subscribe in case the player reloaded
    document.querySelectorAll('[data-rate]').forEach(button => button.addEventListener('click', () => {
      state.rate = Number(button.dataset.rate); command('setPlaybackRate', [state.rate]); $('#practice-rate').textContent = `${state.rate}×`;
    }));
    $('#loop-back').addEventListener('click', () => { state.b = state.time; state.a = Math.max(0, state.time - 5); label(); command('seekTo', [state.a, true]); command('playVideo'); });
    $('#loop-a').addEventListener('click', () => { state.a = state.time; state.b = null; label(); });
    $('#loop-b').addEventListener('click', () => {
      if (state.a == null) { toast('먼저 시작(A) 지점을 정하세요.'); return; }
      if (state.time <= state.a + 0.5) { toast('끝(B)은 시작(A)보다 뒤여야 합니다.'); return; }
      state.b = state.time; label(); command('seekTo', [state.a, true]);
    });
    $('#loop-off').addEventListener('click', () => { state.a = state.b = null; label(); });
    $('#seek-back').addEventListener('click', () => command('seekTo', [Math.max(0, state.time - 3), true]));
    $('#seek-fwd').addEventListener('click', () => command('seekTo', [state.time + 3, true]));
    return () => { clearInterval(timer); window.removeEventListener('message', onMessage); };
  }
  // 댄스 안무: one video is picked (in order or at random, same setting as the other cards) and starts right away.
  let stopDance = null;
  function renderDances(forcedIdx) {
    if (stopDance) { stopDance(); stopDance = null; }
    const dances = (content.dances || []).filter(d => /^[a-zA-Z0-9_-]{11}$/.test(d.youtubeId || ''));
    if (!dances.length) { $('#popup-stage').innerHTML = '<p class="small-help">재생할 수 있는 검증된 안무 영상이 없습니다.</p>'; return; }
    const current = Number.isInteger(forcedIdx) ? forcedIdx : pickVariant('dance', dances.length);
    if (Number.isInteger(forcedIdx)) store.set('teum.variantIdx.dance', String(current));
    const dance = dances[current];
    const picker = variantPicker('dance', dances.map(d => ({ title: `${d.artist} · ${d.title}` })), current);
    $('#popup-stage').innerHTML = `${picker}<div class="dance-note">서서 하는 원본 안무입니다. 책상·의자와 거리를 두고 공간을 확보하세요. 점프나 깊은 굽힘이 부담되면 보기만 하거나 다른 주제로 바꾸세요. 허리 통증이 있으면 저강도 자세 전환을 먼저 고르세요.</div><iframe id="dance-frame" class="${dance.vertical ? 'short-player' : 'video-player'}" title="${escape(dance.title)}" src="https://www.youtube-nocookie.com/embed/${dance.youtubeId}?autoplay=1&rel=0&playsinline=1&enablejsapi=1${dance.vertical ? `&loop=1&playlist=${dance.youtubeId}` : ''}" referrerpolicy="strict-origin-when-cross-origin" allow="autoplay; encrypted-media; fullscreen; picture-in-picture" allowfullscreen></iframe>${practiceControls()}<p class="small-help">${escape(dance.artist)} · ${escape(dance.title)} (공식 채널). 영상이 임베드를 허용하지 않으면 아래 공식 링크로 여세요. 원본을 내려받거나 재편집하지 않습니다. 속도·구간 반복은 영상 재생기를 원격 조작하는 것이며, 반응이 없으면 영상 안의 ⚙ 메뉴에서 직접 바꿀 수 있습니다.</p><button id="video-external" class="secondary" type="button">공식 영상 브라우저에서 열기 ↗</button>`;
    stopDance = bindPractice();
    $('#video-external').addEventListener('click', () => send('open-link', { url: dance.url }));
    document.querySelectorAll('[data-variant]').forEach(button => button.addEventListener('click', () => renderDances(Number(button.dataset.variant))));
    if ($('#variant-mode')) $('#variant-mode').addEventListener('change', event => setVariantMode('dance', event.target.value));
  }
  function receive(message) {
    if (message.type === 'tick') { updateStatus(message); return; }
    if (message.type === 'error') { toast(message.message); return; }
    if (message.type === 'themes') {
      if (!state || state.mode === 'popup') return;
      state.themes = message.themes; state.settings = { ...state.settings, ThemeIds: message.settings.ThemeIds, ThemeMode: message.settings.ThemeMode }; state.sync = message.sync;
      refreshThemeGrid();
      if (message.notice && message.notice !== state.notice) { state.notice = message.notice; toast(message.notice); }
      return;
    }
    if (message.type !== 'state') return;
    state = message;
    if (state.mode === 'popup') renderPopup(); else renderSettings();
    if (state.notice && state.mode !== 'popup') toast(state.notice);
  }
  document.addEventListener('keydown', event => { if (event.key === 'Escape' && state && state.mode === 'popup') { send('close'); } });
  if (host) { host.addEventListener('message', event => receive(event.data)); send('ready'); }
  else {
    let stored = {};
    try { stored = JSON.parse(localStorage.getItem('teum-preview-settings') || '{}'); } catch (_) { /* Keep safe defaults. */ }
    const popup = location.hash.startsWith('#popup');
    receive({ type: 'state', mode: popup ? 'popup' : 'settings', themeId: decodeURIComponent(location.hash.split('=')[1] || 'stretch'), themes: [...(window.THEME_DATA || []), ...extraThemes()], settings: { ...defaults, ...stored }, sync: { offline: true }, next: '', notice: '' });
  }
})();
