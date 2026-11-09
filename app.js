/* Local UI. No answers, microphone input, or health details leave the app. */
(() => {
  'use strict';
  const host = window.chrome && window.chrome.webview;
  const root = document.querySelector('#app');
  const core = window.TeumCore;
  const content = window.GAME_CONTENT || { poems: [], dances: [] };
  const poetry = window.POEM_LIBRARY || { poems: content.poems || [] };
  const reading = window.POEM_READING || { items: [] };
  const poemFilters = { category: 'all', language: 'all', query: '' };
  const poemCategories = { lyric: '서정시', song: '노랫말', nobel: '노벨 문학상' };
  let state, breathTimer = null, breathStart = 0, quiz = null, selectedPoemId = '';
  const defaults = { IntervalMinutes: 60, WeekdaysOnly: true, LimitHours: true, StartHour: 9, StartMinute: 0, EndHour: 18, EndMinute: 0, ThemeIds: ['stretch'], Enabled: false, StartAtLogin: false, InhaleSeconds: 4, HoldSeconds: 0, ExhaleSeconds: 6, ThemeMode: 'ordered-one' };
  const symbols = { stretch: '↟', meditation: '◌', yoga: '♧', pilates: '↥', core: '◎', breathing: '≈', voice: '♪', 'back-seated': '↕', 'back-standing': '↗', dance: '♫', poem: '文' };
  const themeModes = [
    { id: 'sequential-all', label: '모두 순서대로', help: '알림이 오면 고른 쉼을 목록 순서대로 전부, 하나가 끝나면 이어서' },
    { id: 'random-all', label: '모두 무작위 순서로', help: '알림이 오면 고른 쉼을 전부, 순서를 섞어서 이어서' },
    { id: 'ordered-one', label: '하나씩 순서대로', help: '알림마다 하나만, 목록 순서대로 돌아가며' },
    { id: 'random-one', label: '하나만 무작위로', help: '알림마다 하나만, 고른 것 중에서 무작위로' }
  ];
  const modeOf = id => themeModes.find(m => m.id === id) || themeModes[2];
  const symbolFor = t => symbols[t.id] || (t.community ? '✿' : '◌');
  const tones = ['#e3e9ce', '#e4deec', '#eddcca', '#d9e6e7'];
  const $ = selector => document.querySelector(selector);
  const escape = value => String(value == null ? '' : value).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  function toast(message) {
    const box = $('#toast'); box.textContent = message; box.classList.add('show');
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
    if ($('#next-time')) $('#next-time').textContent = enabled && !Number.isNaN(next.getTime()) ? next.toLocaleTimeString('ko-KR', { hour: '2-digit', minute: '2-digit' }) : '직접 시작하세요';
    if ($('#next-date')) $('#next-date').textContent = enabled && !Number.isNaN(next.getTime()) ? next.toLocaleDateString('ko-KR', { month: 'long', day: 'numeric', weekday: 'short' }) : '간격과 주제를 선택한 뒤';
  }
  function extraThemes() {
    return [
      { id: 'dance', name: '걸그룹 안무', summary: '공식 안무 영상으로 원본 동작 따라 보기', instructions: ['공식 연습영상을 고르세요.', '서서 하는 움직임이므로 충분한 공간을 확보하세요.'], safety: '앉아서 하는 운동이 아닙니다. 통증이 있으면 어려운 동작을 건너뛰세요.', file: '' },
      { id: 'poem', name: '시 한 줄 채우기', summary: '다양한 시·노랫말·노벨 문학상 작품에서 무작위 한 줄', instructions: [], safety: '공개 원문 판본 기준으로 비교합니다. 최신 저작물은 공식 감상 링크로 구분합니다.', file: '' }
    ];
  }
  function themeCards(checked) {
    return state.themes.map((t, i) => `<article class="theme-card" style="--tone:${tones[i % tones.length]}"><label><div class="theme-top"><span class="theme-symbol">${symbolFor(t)}</span><input name="theme" type="checkbox" value="${escape(t.id)}" ${checked.has(t.id) ? 'checked' : ''} aria-label="${escape(t.name)} 주제 선택"></div><h3>${escape(t.name)}</h3><p>${escape(t.summary)}</p></label><button class="preview" data-preview="${escape(t.id)}" type="button">미리 보기 ↗</button></article>`).join('');
  }
  function syncText() {
    const sync = state.sync || {};
    if (sync.offline) return '이 환경에서는 공유 동기화를 쓰지 않아요.';
    if (sync.syncing) return '공유된 쉼을 불러오는 중…';
    const when = sync.last ? new Date(sync.last).toLocaleTimeString('ko-KR', { hour: '2-digit', minute: '2-digit' }) : '';
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
    const count = () => {
      const n = document.querySelectorAll('input[name=theme]:checked').length;
      const mode = modeOf((document.querySelector('input[name=theme-mode]:checked') || {}).value);
      $('#selected-count').textContent = `${n}개 선택 · ${mode.label}`;
      $('#theme-mode-help').textContent = n > 1 ? mode.help : '두 개 이상 고르면 아래 방식으로 실행돼요.';
    };
    document.querySelectorAll('input[name=theme], input[name=theme-mode]').forEach(input => input.addEventListener('change', count)); count();
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
    return `<details class="advanced share-panel" id="share-panel"><summary>내 쉼 공유하기 · GitHub에 올려 다른 사람 앱에도 나타나게</summary>
      <p class="small-help">여기서 쓴 내용이 위의 타일과 같은 모양으로 나와요. 아래 버튼을 누르면 GitHub의 새 파일 화면이 채워진 채 열리고, GitHub에서 로그인해 제안(Pull Request)을 보내면 저장소 관리자가 확인한 뒤 모두에게 나타나요.</p>
      <div class="share-grid"><form id="share-form" class="share-form" autocomplete="off">
        <label>이름 <small>${L.name}자</small><input id="share-name" maxlength="${L.name}" placeholder="예: 목 풀기 한 번"></label>
        <label>한 줄 설명 <small>${L.summary}자</small><input id="share-summary" maxlength="${L.summary}" placeholder="타일에 보이는 짧은 설명"></label>
        <label>동작 안내 <small>한 줄에 하나 · ${L.steps}줄까지 · 줄당 ${L.step}자</small><textarea id="share-steps" rows="5" placeholder="고개를 천천히 숙여요&#10;반대쪽으로 천천히 기울여요"></textarea></label>
        <label>주의사항 <small>${L.safety}자 · 필수</small><textarea id="share-safety" rows="2" maxlength="${L.safety}" placeholder="통증·어지럼이 생기면 바로 중단하세요."></textarea></label>
        <label>표시할 이름 <small>선택 · ${L.author}자 · 공개됩니다</small><input id="share-author" maxlength="${L.author}" placeholder="비워 두면 익명"></label>
        <div class="share-buttons"><button id="share-github" class="primary" type="submit">GitHub에 올리기 ↗</button><button id="share-copy" class="secondary" type="button">JSON 복사</button></div>
      </form>
      <aside><div class="eyebrow">미리 보기 · 다른 사람 화면</div><div id="share-preview" class="themes one"></div><p class="small-help">링크·HTML 기호(&lt; &gt;)는 쓸 수 없어요. 올린 내용은 공개되며 치료·진단 주장은 받지 않아요. 개인정보는 넣지 마세요.</p></aside></div></details>`;
  }
  function readShareForm() {
    return { name: $('#share-name').value, summary: $('#share-summary').value, steps: $('#share-steps').value, safety: $('#share-safety').value, author: $('#share-author').value };
  }
  function renderSharePreview() {
    const result = core.validateShare(readShareForm());
    const item = result.item;
    const theme = { id: 'preview', community: true, name: item.name || '(이름)', summary: item.summary || '(한 줄 설명)', author: item.author };
    const old = state.themes; state.themes = [theme];
    $('#share-preview').innerHTML = themeCards(new Set()).replace(/<button class="preview".*?<\/button>/, '');
    state.themes = old;
  }
  function bindShare() {
    ['name', 'summary', 'steps', 'safety', 'author'].forEach(key => $(`#share-${key}`).addEventListener('input', renderSharePreview));
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
    stopBreath();
    const s = state.settings;
    root.innerHTML = `<div class="shell">
      <aside class="sidebar"><div class="brand">틈<small>TEUM · A LITTLE RESET</small></div>
      <p class="side-note">몰입하는 시간 사이,<br>나를 돌보는 작은 틈.</p>
      <div class="side-badge"><i id="status-dot" class="dot"></i><span id="status-text"></span><br><span style="font-size:11px;color:#7d8c76">설정 창을 닫아도 트레이에 남아요.</span></div>
      <div class="next-label">다음 작은 쉼</div><div id="next-time" class="next-time"></div><div id="next-date" class="small-help"></div>
      <div class="side-bottom">내 PC 안에서만 동작합니다.<br>마이크·카메라·계정이 필요 없어요.<button id="hide-window" class="quiet">설정 창 닫기 / 트레이에 두기 ↗</button><button id="exit-app" class="quiet">알림기 완전히 종료</button></div></aside>
      <main class="main"><div class="heading"><div><div class="eyebrow">YOUR PERSONAL BREAK RITUAL</div><h1>잠깐, 나를 돌보는 시간.</h1><p>알림 간격과 하고 싶은 주제를 골라 주세요. 여러 개를 고르면 순서대로 만나요.</p></div><div class="stamp">◌</div></div>
      ${!host ? '<div class="native-warning">HTML 미리보기입니다. 창을 닫은 뒤에도 정해진 간격으로 전체 화면을 띄우려면 같은 폴더의 DeskHooker.exe를 실행하세요.</div>' : ''}
      <div class="settings-row"><section class="setting-card"><h2><span class="section-no">01</span>어느 간격으로 만날까요?</h2><div class="interval-display"><input id="start-time" type="time" value="${hm(s.StartHour, s.StartMinute)}" aria-label="시작 시각 (시:분)"><span>부터</span><input id="interval" type="number" min="1" max="240" value="${s.IntervalMinutes}" aria-label="팝업 간격, 분"><span>분마다</span></div><p class="small-help">시작 시각(시:분)과 1~240 사이의 간격(분)을 직접 입력하세요.<br>아래 ‘시간대에만 알림 받기’를 켜면 이 시각부터 받아요.<br>팝업을 닫은 시점부터 다음 간격을 다시 셉니다.</p><button id="run-now" class="secondary" type="button" style="margin-top:14px">▶ 즉시 실행 (시험용)</button><p class="small-help">시간대·요일·일시정지와 관계없이, 저장된 주제 순서대로 팝업을 바로 띄웁니다.</p></section>
      <section class="setting-card"><h2><span class="section-no">02</span>언제 알림을 받을까요?</h2><label class="schedule-line"><input id="weekdays" type="checkbox" ${s.WeekdaysOnly ? 'checked' : ''}>월요일부터 금요일까지만</label><label class="schedule-line"><input id="limit-hours" type="checkbox" ${s.LimitHours ? 'checked' : ''}>시작·종료 시각 사이에만 알림 받기</label><div class="schedule-line">종료 <input id="end-time" type="time" value="${hm(s.EndHour, s.EndMinute)}" aria-label="종료 시각 (시:분)"> 전까지 (00:00은 자정)</div><label class="schedule-line"><input id="login-start" type="checkbox" ${s.StartAtLogin ? 'checked' : ''}>Windows 로그인 때 자동 실행</label><p class="small-help">PC의 현지 시각 기준 · 잠금/절전 중에는 쉬어요.<br>알림을 놓쳐도 한꺼번에 몰아서 띄우지 않아요.</p></section></div>
      <div class="section-head"><h2><span class="section-no">03</span>어떤 작은 쉼이 필요하세요?</h2><span id="selected-count"></span></div>
      <div class="sync-bar"><span id="sync-text"></span><button id="sync-now" class="quiet" type="button">지금 동기화 ↻</button></div>
      <div id="theme-grid" class="themes">${themeCards(new Set(s.ThemeIds))}</div>
      <fieldset class="mode-box"><legend>여러 개를 골랐을 때 실행 방식</legend>
        <div class="mode-options">${themeModes.map(m => `<label><input type="radio" name="theme-mode" value="${m.id}" ${modeOf(s.ThemeMode).id === m.id ? 'checked' : ''}>${m.label}</label>`).join('')}</div>
        <p id="theme-mode-help" class="small-help"></p></fieldset>
      ${sharePanel()}
      <details class="advanced"><summary>호흡·발성 가이드 시간 직접 고르기</summary><div class="breath-settings"><label>들숨 <input id="inhale" type="number" min="1" max="10" value="${s.InhaleSeconds}">초</label><label>멈춤 <input id="hold" type="number" min="0" max="30" value="${s.HoldSeconds}">초</label><label>날숨 <input id="exhale" type="number" min="1" max="30" value="${s.ExhaleSeconds}">초</label></div><p class="caution">기본은 숨참기 없이 들숨 4초·날숨 6초입니다. 숫자를 끝까지 채우려 하지 말고 편안한 만큼만 하세요. 멈춤은 선택 사항이며 최대 30초입니다. 몸 상태에 따른 제한이 있으면 의료진에게 먼저 확인하세요. 과호흡 후 숨참기, 힘주어 버티기는 하지 마세요.</p><label><input id="hold-consent" type="checkbox" ${s.HoldSeconds > 0 ? 'checked' : ''}>멈춤은 필수가 아니며 불편하면 즉시 편안한 호흡으로 돌아가야 함을 확인했습니다.</label></details>
      <div class="actions"><button id="start-reminders" class="primary">${s.Enabled ? '변경 적용 · 알림 계속' : '선택한 주제로 알림 시작'} →</button><button id="save-settings" class="secondary">설정만 저장</button><button id="pause-reminders" class="quiet">알림 일시정지</button><small>팝업은 저절로 닫히지 않아요.<br>닫기 또는 Esc를 누르면 다음 쉼까지 대기해요.</small></div>
      <p class="small-help">일반적인 휴식·움직임 안내이며 치료·재활 처방이 아닙니다. 통증이 늘거나 저림·어지럼이 생기면 중단하세요. 허리 통증과 함께 새로 생긴 다리 힘 빠짐, 회음부 감각 저하, 대소변 조절 변화가 있으면 운동보다 즉시 진료가 우선입니다.</p>
      </main></div>`;
    bindThemeGrid(); bindShare(); updateSync();
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
  function parseTime(v) { const m = /^(\d{1,2}):(\d{2})$/.exec(v || ''); return m ? [Number(m[1]), Number(m[2])] : null; }
  function save(enabled) {
    const st = parseTime($('#start-time').value), et = parseTime($('#end-time').value);
    if (!st || !et) { toast('시작·종료 시각을 시:분으로 입력하세요.'); return; }
    const settings = {
      IntervalMinutes: Number($('#interval').value), WeekdaysOnly: $('#weekdays').checked, LimitHours: $('#limit-hours').checked,
      StartHour: st[0], StartMinute: st[1], EndHour: et[0] === 0 && et[1] === 0 ? 24 : et[0], EndMinute: et[0] === 0 && et[1] === 0 ? 0 : et[1], StartAtLogin: $('#login-start').checked,
      ThemeIds: Array.from(document.querySelectorAll('input[name=theme]:checked'), x => x.value), Enabled: enabled,
      InhaleSeconds: Number($('#inhale').value), HoldSeconds: Number($('#hold').value), ExhaleSeconds: Number($('#exhale').value),
      ThemeMode: (document.querySelector('input[name=theme-mode]:checked') || {}).value || 'ordered-one'
    };
    if (!Number.isInteger(settings.IntervalMinutes) || settings.IntervalMinutes < 1 || settings.IntervalMinutes > 240) { toast('간격은 1~240분 사이의 정수로 고르세요.'); return; }
    if (!settings.ThemeIds.length) { toast('주제를 하나 이상 골라 주세요.'); return; }
    if (!Number.isInteger(settings.StartHour) || !Number.isInteger(settings.EndHour) || settings.StartHour < 0 || settings.StartHour > 23 || settings.EndHour < 1 || settings.EndHour > 24 || settings.StartHour * 60 + settings.StartMinute >= settings.EndHour * 60 + settings.EndMinute) { toast('종료 시각은 시작 시각보다 늦어야 해요. (자정까지는 00:00)'); return; }
    if (![settings.InhaleSeconds, settings.HoldSeconds, settings.ExhaleSeconds].every(Number.isInteger)) { toast('호흡 시간은 정수로 입력하세요.'); return; }
    try { core.breathPhase(0, settings); } catch (error) { toast(error.message); return; }
    if (settings.HoldSeconds && !$('#hold-consent').checked) { toast('숨참기 주의사항을 확인하거나 멈춤을 0초로 바꾸세요.'); return; }
    send('save', { settings, holdConsent: $('#hold-consent').checked });
  }
  function popupFrame(theme) {
    return `<main class="popup"><header class="popup-bar"><div class="popup-brand">틈<span>A LITTLE RESET · ${escape(theme.name)}</span></div><button class="popup-close" type="button">닫기 × <span style="font-size:10px;opacity:.6">Esc</span></button></header><div class="popup-content"><div class="popup-heading"><div class="eyebrow">잠깐 쉬어가도 괜찮아요</div><h1>${escape(theme.name)}</h1><p>${escape(theme.summary)}</p></div><div id="popup-stage"></div></div><footer class="popup-footer"><span>창을 닫으면 다음 알림까지 대기해요. 자동으로 닫거나 입력을 강요하지 않아요.</span><span>움직임은 편한 범위에서 · 통증·저림·어지럼이 생기면 중단</span></footer></main>`;
  }
  function renderPopup() {
    stopBreath();
    const theme = state.themes.find(t => t.id === state.themeId) || state.themes[0];
    root.innerHTML = popupFrame(theme);
    $('.popup-close').addEventListener('click', () => { stopBreath(); send('close'); });
    if (theme.id === 'poem') renderPoem();
    else if (theme.id === 'dance') renderDances();
    else renderWellness(theme);
  }
  function renderWellness(theme) {
    const visual = theme.file ? `<img class="motion-image" src="${escape(theme.file)}" alt="${escape(theme.name)} 따라 하기 움직이는 그림">` : `<div class="text-stage"><div class="text-symbol" aria-hidden="true">${symbolFor(theme)}</div></div>`;
    $('#popup-stage').innerHTML = `<div class="wellness-layout"><div class="motion-stage">${visual}</div><aside class="instruction-panel"><div class="eyebrow">천천히 따라 해 보세요</div><ol>${(theme.instructions || []).map(line => `<li>${escape(line)}</li>`).join('')}</ol>${['breathing', 'voice'].includes(theme.id) ? breathingControls() : ''}<div class="safety">${escape(theme.safety)}</div></aside></div>`;
    if ($('#breath-start')) {
      $('#breath-start').addEventListener('click', beginBreath);
      $('#breath-stop').addEventListener('click', stopBreath);
    }
  }
  function breathingControls() {
    const s = state.settings;
    return `<section class="timer-box"><div class="eyebrow">선택형 호흡 가이드 · 소리 없음</div><div class="timer-display"><div id="breath-orb" class="breath-orb">—</div><div><div id="breath-phase" class="phase-label">편안한 호흡으로</div><div class="timer-caption">들숨 ${s.InhaleSeconds}초 / 멈춤 ${s.HoldSeconds}초 / 날숨 ${s.ExhaleSeconds}초</div></div></div><div class="timer-buttons"><button id="breath-start" class="primary">가이드 시작</button><button id="breath-stop" class="secondary">즉시 중단</button></div><p class="timer-caption">시간은 목표가 아닙니다. 숨이 차거나 어지러우면 즉시 중단하고 자연스럽게 호흡하세요. 발성은 편한 높이·작은 소리로, 숨을 억지로 오래 밀어내지 마세요.</p></section>`;
  }
  function beginBreath() {
    stopBreath(); breathStart = performance.now();
    const tick = () => {
      const phase = core.breathPhase(performance.now() - breathStart, state.settings);
      $('#breath-phase').textContent = { inhale: '편안하게 들이쉬기', hold: '선택한 멈춤 · 힘주지 않기', exhale: state.themeId === 'voice' ? '가볍게 음~ · 편한 만큼만' : '부드럽고 길게 내쉬기' }[phase.phase];
      $('#breath-orb').textContent = phase.remaining;
      const amount = phase.phase === 'inhale' ? phase.progress : phase.phase === 'hold' ? 1 : 1 - phase.progress;
      $('#breath-orb').style.transform = `scale(${0.86 + amount * 0.2})`;
    };
    tick(); breathTimer = setInterval(tick, 100);
  }
  function stopBreath() {
    if (breathTimer) clearInterval(breathTimer); breathTimer = null;
    if ($('#breath-phase')) $('#breath-phase').textContent = '편안한 호흡으로';
    if ($('#breath-orb')) { $('#breath-orb').textContent = '—'; $('#breath-orb').style.transform = 'scale(1)'; }
  }
  function renderPoem() {
    $('#popup-stage').innerHTML = `<section class="poetry-library"><div class="poetry-top"><p class="small-help">오프라인 출제 ${poetry.poems.length}편 · 공식 감상 링크 ${reading.items.length}개 · 답안은 저장·전송하지 않아요.</p><div class="poetry-tabs" role="group" aria-label="시 감상 방식"><button id="poem-quiz-tab" class="primary" aria-pressed="true">한 줄 채우기</button><button id="poem-reading-tab" class="secondary" aria-pressed="false">최근 시·가사·노벨 작품 ↗</button></div></div>
      <div id="poem-library"><div class="poetry-filters"><label>작품 분류<select id="poem-category"><option value="all">전체 작품</option>${Object.entries(poemCategories).map(([id, label]) => `<option value="${id}" ${poemFilters.category === id ? 'selected' : ''}>${label}</option>`).join('')}</select></label><label>원문 언어<select id="poem-language"><option value="all">모든 언어</option><option value="ko" ${poemFilters.language === 'ko' ? 'selected' : ''}>한국어</option><option value="en" ${poemFilters.language === 'en' ? 'selected' : ''}>영어 원문</option></select></label><label class="poetry-search">제목·작가 검색<input id="poem-search" type="search" placeholder="제목 또는 작가" value="${escape(poemFilters.query)}" autocomplete="off"></label><label class="poetry-picker">직접 작품 고르기<select id="poem-picker"></select></label></div><div class="poetry-status"><span id="poem-count" role="status" aria-live="polite"></span><button id="poem-next" class="secondary" type="button">다른 작품 무작위 ↗</button></div><div id="poem-body"></div></div>
      <section id="poem-reading-section" hidden><p class="reading-notice">최근 출간 시·노랫가사·노벨 문학상 작품의 공식 감상 안내입니다. 저작권이 남아 있는 전문은 이 앱에 복제하거나 빈칸 문제로 만들지 않아요. 버튼을 눌렀을 때만 외부 브라우저를 열며, 목록은 자동 수집·실시간 갱신되지 않습니다.${reading.checkedAt ? ` 출처 확인: ${escape(reading.checkedAt)}.` : ''}</p><label class="reading-filter">감상 링크 분류<select id="reading-category"><option value="all">전체 감상 링크</option><option value="recent">최근 출간 시</option><option value="song">공식 노랫말</option><option value="nobel">노벨 문학상 작품</option></select></label><div id="poem-reading-list" class="reading-grid"></div></section></section>`;
    const tab = mode => {
      const playing = mode === 'quiz';
      $('#poem-library').hidden = !playing; $('#poem-reading-section').hidden = playing;
      for (const [selector, active] of [['#poem-quiz-tab', playing], ['#poem-reading-tab', !playing]]) {
        $(selector).setAttribute('aria-pressed', String(active));
        $(selector).className = active ? 'primary' : 'secondary';
      }
    };
    $('#poem-quiz-tab').addEventListener('click', () => { tab('quiz'); refreshPoems(); });
    $('#poem-reading-tab').addEventListener('click', () => { tab('reading'); $('#poem-body').innerHTML = ''; quiz = null; window.teumGameQuestion = null; });
    for (const [selector, key, event] of [['#poem-category', 'category', 'change'], ['#poem-language', 'language', 'change'], ['#poem-search', 'query', 'input']]) {
      $(selector).addEventListener(event, () => { poemFilters[key] = $(selector).value; refreshPoems(); });
    }
    $('#poem-picker').addEventListener('change', () => { selectedPoemId = $('#poem-picker').value; refreshPoems(); });
    $('#poem-next').addEventListener('click', () => refreshPoems(true));
    $('#reading-category').addEventListener('change', renderPoemReading);
    refreshPoems(); renderPoemReading();
  }
  function refreshPoems(randomWork = false) {
    const pool = core.filterPoems(poetry.poems, poemFilters);
    const poem = (!randomWork && pool.find(item => item.id === selectedPoemId)) || core.choosePoem(pool, selectedPoemId);
    selectedPoemId = poem ? poem.id : '';
    $('#poem-picker').innerHTML = pool.map(item => `<option value="${escape(item.id)}" ${item.id === selectedPoemId ? 'selected' : ''}>${escape(item.title)} · ${escape(item.author)}</option>`).join('');
    $('#poem-count').textContent = `${pool.length}편 선택 가능 / 전체 ${poetry.poems.length}편 · 작품과 빈칸 줄은 무작위로 만나요`;
    $('#poem-picker').disabled = !poem; $('#poem-next').disabled = !poem;
    if (!poem) {
      quiz = null; window.teumGameQuestion = null;
      $('#poem-body').innerHTML = '<div id="poem-empty" class="poetry-empty" role="status">조건에 맞는 작품이 없어요. 검색어를 지우거나 분류·언어를 바꿔 주세요.</div>'; return;
    }
    renderPoemQuestion(poem);
  }
  function renderPoemQuestion(poem, blankIndex = null) {
    const previousIndex = quiz && quiz.poemId === poem.id ? quiz.blankIndex : null;
    quiz = core.chooseQuestion(poem, Math.random, { includeShort: true, blankIndex, previousIndex });
    window.teumGameQuestion = quiz;
    const nonblank = poem.lines.map((line, index) => ({ line, index })).filter(item => core.normalizeAnswer(item.line).length);
    const lineNumber = nonblank.findIndex(item => item.index === quiz.blankIndex) + 1;
    const prose = /original prose-poem paragraphs/.test(poem.edition || '');
    const unit = prose ? '문단' : '줄';
    $('#poem-body').innerHTML = `<div class="quiz-layout">
      <article class="poem-paper" lang="${poem.language === 'en' ? 'en' : 'ko'}">
        <div class="poem-meta">${escape(poem.title)} · ${escape(poem.author)}${poem.year ? ` · ${escape(poem.year)}` : ''}
          <span class="poem-badge">${escape(poemCategories[poem.category] || '서정시')} · ${poem.language === 'en' ? '영어 원문' : '한국어'}${poem.nobelYear ? ` · ${escape(poem.nobelYear)} 수상 작가` : ''}</span></div>
        <div id="poem-lines" class="poem-lines">${quiz.lines.map((line, index) => index === quiz.blankIndex ? `<div class="poem-blank">${lineNumber}번째 ${unit}을 채워 주세요. ✎</div>` : `<div class="poem-line ${line.trim() ? '' : 'stanza'}">${escape(line)}</div>`).join('')}</div>
      </article>
      <section class="answer-panel"><div class="eyebrow">${prose ? 'ONE MISSING PARAGRAPH' : 'ONE MISSING LINE'}</div><h2>이다음의 마음은 무엇일까요?</h2>
        <p>작품에서 사라진 한 ${unit}을 떠올려 적어 보세요.<br>띄어쓰기는 무시하고, 단어와 문장부호는 원문과 비교해요.</p>
        <div class="poem-line-controls"><label>빈칸 ${unit} 고르기<select id="poem-line-choice"><option value="random" ${blankIndex == null ? 'selected' : ''}>매번 무작위</option>${nonblank.map((item, order) => `<option value="${item.index}" ${item.index === blankIndex ? 'selected' : ''}>${order + 1}번째 ${unit}</option>`).join('')}</select></label><button id="poem-reroll" class="secondary" type="button" ${nonblank.length < 2 ? 'disabled' : ''}>같은 작품 · 다른 ${unit} ↻</button></div>
        <p id="poem-unit-note" class="small-help">현재 ${nonblank.length}${prose ? '문단' : '줄'} 중 ${lineNumber}번째 ${unit} · ${prose ? '산문시이므로 원문의 문단 단위로 출제해요. 문장을 임의로 잘라 시의 행으로 만들지 않습니다.' : '빈 줄은 출제하지 않아요.'}</p>
        <form id="poem-form"><textarea id="poem-answer" rows="${prose ? 3 : 2}" autocomplete="off" aria-label="빈 ${unit}에 들어갈 시 구절" placeholder="한 ${unit}을 입력하세요"></textarea><div class="quiz-buttons"><button id="poem-submit" class="primary" type="submit">입력한 답과 원문 비교</button><button id="poem-reveal" class="quiet" type="button">정답 보기</button></div></form>
        <div id="poem-result" hidden aria-live="polite"></div>
        <p class="source-note">${escape(poem.edition || '공개 원문 판본')}<br>저작권이 만료된 원문 기준이며 옛 표기·한자가 남아 있을 수 있어요. 영어 작품은 영어 원문입니다. 위키문헌 편집 기여의 출처·기여자·CC BY-SA 4.0 조건은 원문 링크와 poetry-sources.json에 남겼습니다.</p>
        <button id="poem-source" class="quiet" type="button">출처 원문 브라우저에서 열기 ↗</button>
      </section></div>`;
    $('#poem-form').addEventListener('submit', event => { event.preventDefault(); comparePoem(); });
    $('#poem-reveal').addEventListener('click', () => comparePoem(true));
    $('#poem-reroll').addEventListener('click', () => renderPoemQuestion(poem));
    $('#poem-line-choice').addEventListener('change', () => renderPoemQuestion(poem, $('#poem-line-choice').value === 'random' ? null : Number($('#poem-line-choice').value)));
    $('#poem-source').addEventListener('click', () => send('open-link', { url: poem.source }));
    const blank = $('.poem-blank'); if (blank) $('#poem-lines').scrollTop = Math.max(0, blank.offsetTop - $('#poem-lines').offsetTop - 40);
  }
  function renderPoemReading() {
    const category = $('#reading-category').value;
    const items = reading.items.filter(item => category === 'all' || item.category === category);
    $('#poem-reading-list').innerHTML = items.map(item => `<article class="reading-card"><div class="eyebrow">${escape({ recent: '최근 출간 시', song: '공식 노랫말', nobel: '노벨 문학상' }[item.category] || '공식 감상')}</div><h2>${escape(item.title)}</h2><p>${escape(item.author)}${item.year ? ` · ${escape(item.year)}` : ''}</p><p>${escape(item.note)}</p><span class="reading-only">외부 감상 전용 · 빈칸 출제 제외</span><button class="secondary" type="button" data-reading="${escape(item.id)}">공식 페이지 열기 ↗</button></article>`).join('') || '<p class="poetry-empty">이 분류의 공식 감상 링크를 준비하지 못했어요.</p>';
    document.querySelectorAll('[data-reading]').forEach(button => button.addEventListener('click', () => {
      const item = items.find(entry => entry.id === button.dataset.reading);
      if (item) send('open-link', { url: item.url });
    }));
  }
  function comparePoem(reveal = false) {
    if (!quiz) return;
    const entered = $('#poem-answer').value;
    if (!entered.trim() && !reveal) { toast('기억나는 한 줄을 먼저 적어 주세요.'); $('#poem-answer').focus(); return; }
    const correct = core.compareAnswer(entered, quiz.expected);
    const box = $('#poem-result'); box.hidden = false; box.dataset.correct = String(correct);
    box.innerHTML = `<strong>${correct ? '원문과 같은 정답이에요.' : '원문과 나란히 비교해 보세요.'}</strong><div class="answer-label">내가 적은 한 줄</div><div class="answer-value user-answer"></div><div class="answer-label">원문의 한 줄</div><div class="answer-value canonical-answer"></div>`;
    $('.user-answer').textContent = entered;
    $('.canonical-answer').textContent = quiz.expected;
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
  function renderDances() {
    const dances = content.dances || [];
    $('#popup-stage').innerHTML = `<div class="dance-note">서서 하는 원본 안무입니다. 책상·의자와 거리를 두고 공간을 확보하세요. 점프나 깊은 굽힘이 부담되면 보기만 하거나 다른 주제로 바꾸세요. 허리 통증이 있으면 저강도 자세 전환을 먼저 고르세요.<br>공식 영상을 직접 눌렀을 때만 인터넷에 연결합니다. 자동 재생·자동 소리 재생은 하지 않아요.</div><div class="dance-grid">${dances.map((dance, index) => `<article class="dance-card"><div class="dance-art">${['♫', '♪', '♬'][index % 3]}</div><div class="eyebrow" style="margin-top:18px">${escape(dance.artist)}</div><h2>${escape(dance.title)}</h2><p>${escape(dance.note || '공식 채널의 안무 영상을 보고 원본 동작을 확인하세요.')}</p><button class="primary" data-dance="${index}">이 창에서 공식 영상 보기 ▶</button><button class="quiet" data-video-link="${index}">브라우저에서 열기 ↗</button></article>`).join('')}</div>${!dances.length ? '<div class="error-panel">공식 안무 영상의 출처를 검증하지 못해 임의 링크를 넣지 않았습니다.</div>' : ''}`;
    document.querySelectorAll('[data-dance]').forEach(button => button.addEventListener('click', () => {
      const dance = dances[Number(button.dataset.dance)];
      if (!/^[a-zA-Z0-9_-]{11}$/.test(dance.youtubeId)) { toast('검증된 영상 ID가 없습니다. 공식 링크로 열어 주세요.'); return; }
      $('#popup-stage').innerHTML = `<button id="video-back" class="secondary video-back">← 다른 공식 영상 고르기</button><iframe id="dance-frame" class="video-player" title="${escape(dance.title)}" src="https://www.youtube-nocookie.com/embed/${dance.youtubeId}?autoplay=0&rel=0&enablejsapi=1" referrerpolicy="strict-origin-when-cross-origin" allow="encrypted-media; fullscreen; picture-in-picture" allowfullscreen></iframe>${practiceControls()}<p class="small-help">영상이 임베드를 허용하지 않으면 아래 공식 링크로 여세요. 원본을 내려받거나 재편집하지 않습니다. 속도·구간 반복은 영상 재생기를 원격 조작하는 것이며, 반응이 없으면 영상 안의 ⚙ 메뉴에서 직접 바꿀 수 있습니다.</p><button id="video-external" class="secondary">공식 영상 브라우저에서 열기 ↗</button>`;
      const stopPractice = bindPractice();
      $('#video-back').addEventListener('click', () => { stopPractice(); renderDances(); });
      $('#video-external').addEventListener('click', () => send('open-link', { url: dance.url }));
    }));
    document.querySelectorAll('[data-video-link]').forEach(button => button.addEventListener('click', () => send('open-link', { url: dances[Number(button.dataset.videoLink)].url })));
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
  document.addEventListener('keydown', event => { if (event.key === 'Escape' && state && state.mode === 'popup') { stopBreath(); send('close'); } });
  document.addEventListener('visibilitychange', () => { if (document.hidden) stopBreath(); });
  window.addEventListener('beforeunload', stopBreath);
  if (host) { host.addEventListener('message', event => receive(event.data)); send('ready'); }
  else {
    let stored = {};
    try { stored = JSON.parse(localStorage.getItem('teum-preview-settings') || '{}'); } catch (_) { /* Keep safe defaults. */ }
    const popup = location.hash.startsWith('#popup');
    receive({ type: 'state', mode: popup ? 'popup' : 'settings', themeId: decodeURIComponent(location.hash.split('=')[1] || 'stretch'), themes: [...(window.THEME_DATA || []), ...extraThemes()], settings: { ...defaults, ...stored }, sync: { offline: true }, next: '', notice: '' });
  }
})();
