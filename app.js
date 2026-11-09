/* Local UI. No answers, microphone input, or health details leave the app. */
(() => {
  'use strict';
  const host = window.chrome && window.chrome.webview;
  const root = document.querySelector('#app');
  const core = window.TeumCore;
  const content = window.GAME_CONTENT || { poems: [], dances: [] };
  let state, breathTimer = null, breathStart = 0, quiz = null, poemCursor = 0;
  const defaults = { IntervalMinutes: 60, WeekdaysOnly: true, LimitHours: true, StartHour: 9, EndHour: 18, ThemeIds: ['stretch'], Enabled: false, StartAtLogin: false, InhaleSeconds: 4, HoldSeconds: 0, ExhaleSeconds: 6 };
  const symbols = { stretch: '↟', meditation: '◌', yoga: '♧', pilates: '↥', core: '◎', breathing: '≈', voice: '♪', 'back-seated': '↕', 'back-standing': '↗', dance: '♫', poem: '文' };
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
      { id: 'poem', name: '시 한 줄 채우기', summary: '서정시에서 사라진 한 줄을 기억해 보세요', instructions: [], safety: '공개 원문 판본 기준으로 비교합니다.', file: '' }
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
    const count = () => { $('#selected-count').textContent = `${document.querySelectorAll('input[name=theme]:checked').length}개 선택 · 목록 순서대로`; };
    document.querySelectorAll('input[name=theme]').forEach(input => input.addEventListener('change', count)); count();
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
      <div class="settings-row"><section class="setting-card"><h2><span class="section-no">01</span>어느 간격으로 만날까요?</h2><div class="interval-display"><input id="interval" type="number" min="1" max="240" value="${s.IntervalMinutes}" aria-label="팝업 간격, 분"><span>분에 한 번</span></div><div class="pills">${[15, 30, 45, 60, 90, 120].map(n => `<button type="button" data-minutes="${n}" class="${s.IntervalMinutes === n ? 'active' : ''}">${n}분</button>`).join('')}</div><p class="small-help">1~240분을 직접 입력할 수도 있어요.<br>팝업을 닫은 시점부터 다음 간격을 다시 셉니다.</p></section>
      <section class="setting-card"><h2><span class="section-no">02</span>언제 알림을 받을까요?</h2><label class="schedule-line"><input id="weekdays" type="checkbox" ${s.WeekdaysOnly ? 'checked' : ''}>월요일부터 금요일까지만</label><label class="schedule-line"><input id="limit-hours" type="checkbox" ${s.LimitHours ? 'checked' : ''}>아래 시간대에만 알림 받기</label><div class="schedule-line"><input id="start-hour" type="number" min="0" max="23" value="${s.StartHour}" aria-label="시작 시각">시부터 <input id="end-hour" type="number" min="1" max="24" value="${s.EndHour}" aria-label="종료 시각">시 전까지</div><label class="schedule-line"><input id="login-start" type="checkbox" ${s.StartAtLogin ? 'checked' : ''}>Windows 로그인 때 자동 실행</label><p class="small-help">PC의 현지 시각 기준 · 잠금/절전 중에는 쉬어요.<br>알림을 놓쳐도 한꺼번에 몰아서 띄우지 않아요.</p></section></div>
      <div class="section-head"><h2><span class="section-no">03</span>어떤 작은 쉼이 필요하세요?</h2><span id="selected-count"></span></div>
      <div class="sync-bar"><span id="sync-text"></span><button id="sync-now" class="quiet" type="button">지금 동기화 ↻</button></div>
      <div id="theme-grid" class="themes">${themeCards(new Set(s.ThemeIds))}</div>
      ${sharePanel()}
      <details class="advanced"><summary>호흡·발성 가이드 시간 직접 고르기</summary><div class="breath-settings"><label>들숨 <input id="inhale" type="number" min="1" max="10" value="${s.InhaleSeconds}">초</label><label>멈춤 <input id="hold" type="number" min="0" max="30" value="${s.HoldSeconds}">초</label><label>날숨 <input id="exhale" type="number" min="1" max="30" value="${s.ExhaleSeconds}">초</label></div><p class="caution">기본은 숨참기 없이 들숨 4초·날숨 6초입니다. 숫자를 끝까지 채우려 하지 말고 편안한 만큼만 하세요. 멈춤은 선택 사항이며 최대 30초입니다. 몸 상태에 따른 제한이 있으면 의료진에게 먼저 확인하세요. 과호흡 후 숨참기, 힘주어 버티기는 하지 마세요.</p><label><input id="hold-consent" type="checkbox" ${s.HoldSeconds > 0 ? 'checked' : ''}>멈춤은 필수가 아니며 불편하면 즉시 편안한 호흡으로 돌아가야 함을 확인했습니다.</label></details>
      <div class="actions"><button id="start-reminders" class="primary">${s.Enabled ? '변경 적용 · 알림 계속' : '선택한 주제로 알림 시작'} →</button><button id="save-settings" class="secondary">설정만 저장</button><button id="pause-reminders" class="quiet">알림 일시정지</button><small>팝업은 저절로 닫히지 않아요.<br>닫기 또는 Esc를 누르면 다음 쉼까지 대기해요.</small></div>
      <p class="small-help">일반적인 휴식·움직임 안내이며 치료·재활 처방이 아닙니다. 통증이 늘거나 저림·어지럼이 생기면 중단하세요. 허리 통증과 함께 새로 생긴 다리 힘 빠짐, 회음부 감각 저하, 대소변 조절 변화가 있으면 운동보다 즉시 진료가 우선입니다.</p>
      </main></div>`;
    document.querySelectorAll('[data-minutes]').forEach(button => button.addEventListener('click', () => { $('#interval').value = button.dataset.minutes; document.querySelectorAll('[data-minutes]').forEach(b => b.classList.toggle('active', b === button)); }));
    bindThemeGrid(); bindShare(); updateSync();
    $('#sync-now').addEventListener('click', () => { if (!host) { toast('공유 동기화는 DeskHooker.exe에서 동작해요. 공유하기는 지금도 쓸 수 있어요.'); return; } send('sync'); });
    $('#start-reminders').addEventListener('click', () => save(true));
    $('#save-settings').addEventListener('click', () => save(s.Enabled));
    $('#pause-reminders').addEventListener('click', () => save(false));
    $('#hide-window').addEventListener('click', () => send('close'));
    $('#exit-app').addEventListener('click', () => send('exit'));
    updateStatus(state);
  }
  function save(enabled) {
    const settings = {
      IntervalMinutes: Number($('#interval').value), WeekdaysOnly: $('#weekdays').checked, LimitHours: $('#limit-hours').checked,
      StartHour: Number($('#start-hour').value), EndHour: Number($('#end-hour').value), StartAtLogin: $('#login-start').checked,
      ThemeIds: Array.from(document.querySelectorAll('input[name=theme]:checked'), x => x.value), Enabled: enabled,
      InhaleSeconds: Number($('#inhale').value), HoldSeconds: Number($('#hold').value), ExhaleSeconds: Number($('#exhale').value)
    };
    if (!Number.isInteger(settings.IntervalMinutes) || settings.IntervalMinutes < 1 || settings.IntervalMinutes > 240) { toast('간격은 1~240분 사이의 정수로 고르세요.'); return; }
    if (!settings.ThemeIds.length) { toast('주제를 하나 이상 골라 주세요.'); return; }
    if (!Number.isInteger(settings.StartHour) || !Number.isInteger(settings.EndHour) || settings.StartHour < 0 || settings.StartHour > 23 || settings.EndHour < 1 || settings.EndHour > 24 || settings.StartHour >= settings.EndHour) { toast('시작은 0~23시, 종료는 그 이후의 1~24시로 골라 주세요.'); return; }
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
    const poems = content.poems || [];
    if (!poems.length) { $('#popup-stage').innerHTML = '<div class="error-panel">검증된 시 원문을 준비하지 못했습니다. 다른 주제를 골라 주세요.</div>'; return; }
    const poem = poems[poemCursor % poems.length];
    quiz = core.chooseQuestion(poem);
    window.teumGameQuestion = quiz;
    $('#popup-stage').innerHTML = `<div class="quiz-layout"><article class="poem-paper"><div class="poem-meta">${escape(poem.title)} · ${escape(poem.author)}${poem.year ? ` · ${escape(poem.year)}` : ''}</div><div id="poem-lines" class="poem-lines">${quiz.lines.map((line, index) => index === quiz.blankIndex ? '<div class="poem-blank">이 한 줄을 채워 주세요. ✎</div>' : `<div class="poem-line ${line ? '' : 'stanza'}">${escape(line)}</div>`).join('')}</div></article><section class="answer-panel"><div class="eyebrow">ONE MISSING LINE</div><h2>이다음의 마음은 무엇일까요?</h2><p>시에서 사라진 한 줄을 떠올려 적어 보세요.<br>띄어쓰기는 무시하고, 단어와 문장부호는 원문과 비교해요.</p><form id="poem-form"><input id="poem-answer" autocomplete="off" aria-label="빈 줄에 들어갈 시 구절" placeholder="한 줄을 입력하세요"><div class="quiz-buttons"><button id="poem-submit" class="primary" type="submit">입력한 답과 원문 비교</button><button id="poem-next" class="secondary" type="button">다음 시 ↗</button></div></form><div id="poem-result" hidden aria-live="polite"></div><p class="source-note">원문 판본: ${escape(poem.source || '')}<br>저작권이 만료된 원문을 사용합니다. 정답은 이 판본의 표기 기준이에요. 답안은 저장하거나 전송하지 않습니다.</p></section></div>`;
    $('#poem-form').addEventListener('submit', event => { event.preventDefault(); comparePoem(); });
    $('#poem-next').addEventListener('click', () => { poemCursor++; renderPoem(); });
    const blank = $('.poem-blank'); if (blank) blank.scrollIntoView({ block: 'nearest' });
  }
  function comparePoem() {
    const entered = $('#poem-answer').value;
    if (!entered.trim()) { toast('기억나는 한 줄을 먼저 적어 주세요.'); $('#poem-answer').focus(); return; }
    const correct = core.compareAnswer(entered, quiz.expected);
    const box = $('#poem-result'); box.hidden = false; box.dataset.correct = String(correct);
    box.innerHTML = `<strong>${correct ? '원문과 같은 정답이에요.' : '원문과 나란히 비교해 보세요.'}</strong><div class="answer-label">내가 적은 한 줄</div><div class="answer-value user-answer"></div><div class="answer-label">원문의 한 줄</div><div class="answer-value canonical-answer"></div>`;
    $('.user-answer').textContent = entered;
    $('.canonical-answer').textContent = quiz.expected;
  }
  function renderDances() {
    const dances = content.dances || [];
    $('#popup-stage').innerHTML = `<div class="dance-note">서서 하는 원본 안무입니다. 책상·의자와 거리를 두고 공간을 확보하세요. 점프나 깊은 굽힘이 부담되면 보기만 하거나 다른 주제로 바꾸세요. 허리 통증이 있으면 저강도 자세 전환을 먼저 고르세요.<br>공식 영상을 직접 눌렀을 때만 인터넷에 연결합니다. 자동 재생·자동 소리 재생은 하지 않아요.</div><div class="dance-grid">${dances.map((dance, index) => `<article class="dance-card"><div class="dance-art">${['♫', '♪', '♬'][index % 3]}</div><div class="eyebrow" style="margin-top:18px">${escape(dance.artist)}</div><h2>${escape(dance.title)}</h2><p>${escape(dance.note || '공식 채널의 안무 영상을 보고 원본 동작을 확인하세요.')}</p><button class="primary" data-dance="${index}">이 창에서 공식 영상 보기 ▶</button><button class="quiet" data-video-link="${index}">브라우저에서 열기 ↗</button></article>`).join('')}</div>${!dances.length ? '<div class="error-panel">공식 안무 영상의 출처를 검증하지 못해 임의 링크를 넣지 않았습니다.</div>' : ''}`;
    document.querySelectorAll('[data-dance]').forEach(button => button.addEventListener('click', () => {
      const dance = dances[Number(button.dataset.dance)];
      if (!/^[a-zA-Z0-9_-]{11}$/.test(dance.youtubeId)) { toast('검증된 영상 ID가 없습니다. 공식 링크로 열어 주세요.'); return; }
      $('#popup-stage').innerHTML = `<button id="video-back" class="secondary video-back">← 다른 공식 영상 고르기</button><iframe class="video-player" title="${escape(dance.title)}" src="https://www.youtube-nocookie.com/embed/${dance.youtubeId}?autoplay=0&rel=0" referrerpolicy="strict-origin-when-cross-origin" allow="encrypted-media; fullscreen; picture-in-picture" allowfullscreen></iframe><p class="small-help">영상이 임베드를 허용하지 않으면 아래 공식 링크로 여세요. 원본을 내려받거나 재편집하지 않습니다.</p><button id="video-external" class="secondary">공식 영상 브라우저에서 열기 ↗</button>`;
      $('#video-back').addEventListener('click', renderDances);
      $('#video-external').addEventListener('click', () => send('open-link', { url: dance.url }));
    }));
    document.querySelectorAll('[data-video-link]').forEach(button => button.addEventListener('click', () => send('open-link', { url: dances[Number(button.dataset.videoLink)].url })));
  }
  function receive(message) {
    if (message.type === 'tick') { updateStatus(message); return; }
    if (message.type === 'error') { toast(message.message); return; }
    if (message.type === 'themes') {
      if (!state || state.mode === 'popup') return;
      state.themes = message.themes; state.settings = { ...state.settings, ThemeIds: message.settings.ThemeIds }; state.sync = message.sync;
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
