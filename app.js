/* Local UI. No answers, microphone input, or health details leave the app. */
(() => {
  'use strict';
  const host = window.chrome && window.chrome.webview;
  const root = document.querySelector('#app');
  const core = window.TeumCore;
  const content = window.GAME_CONTENT || { dances: [] };
  let state;
  // 기본 프리셋(303~308): 09:00 시작 · 60분 간격 · 18:00 종료 · 평일만, 점심 12:00-13:00 쉬는 시간,
  // 로그인 때 자동 실행, 10분 자리비움 일시 정지, Knox 캘린더 일정 5분 전 알림(자동 닫힘 없음).
  // 카드 5개를 무작위로 하나씩(스트레칭·명상·발성·허리·릴렉스 — 댄스 제외), 퇴근버스는 17:00에 하루 한 번.
  const defaults = { IntervalMinutes: 60, WeekdaysOnly: true, LimitHours: true, StartHour: 9, StartMinute: 0, EndHour: 18, EndMinute: 0, ThemeIds: ['stretch', 'meditation', 'voice', 'back-standing', 'relax'], BreakEnabled: true, Breaks: [{ StartHour: 12, StartMinute: 0, EndHour: 13, EndMinute: 0 }], BreakStartHour: 12, BreakStartMinute: 0, BreakEndHour: 13, BreakEndMinute: 0, Enabled: false, StartAtLogin: true, InhaleSeconds: 4, HoldSeconds: 0, ExhaleSeconds: 6, ThemeMode: 'random-one', KnoxCalendarEnabled: true, KnoxLeadMinutes: 5, KnoxAutoClose: 'none', KnoxAutoCloseMinutes: 5, PauseWhenIdle: true, IdleMinutes: 10, Appearance: 'system', VariantModes: {}, LoopModes: {}, Schedules: { 'bus-home': '17:00:00' } };
  const symbols = { stretch: '↟', meditation: '◌', voice: '♪', 'back-standing': '↗', dance: '♫', relax: '☾', recipe: '✦', 'bus-home': '🚌' };
  const themeModes = [
    { id: 'ordered-one', label: '순차적으로', help: '알림마다 하나만, 목록 순서대로 돌아가며' },
    { id: 'random-one', label: '무작위로', help: '알림마다 하나만, 고른 것 중에서 무작위로' }
  ];
  // Whole-app color scheme: locked to "시스템 따르기" — light or dark, following Windows.
  function applyAppearance() {
    const dark = window.matchMedia && matchMedia('(prefers-color-scheme: dark)').matches;
    document.documentElement.dataset.appearance = dark ? 'dark' : 'light';
  }
  // Windows scheme switches re-theme open windows immediately, with no restart.
  if (window.matchMedia) {
    const scheme = matchMedia('(prefers-color-scheme: dark)');
    const onSchemeChange = () => applyAppearance();
    if (scheme.addEventListener) scheme.addEventListener('change', onSchemeChange);
    else if (scheme.addListener) scheme.addListener(onSchemeChange);
  }
  const symbolFor = t => t.icon || symbols[t.id] || (t.community ? '✿' : '◌');
  // 카드 아이콘 바탕 톤 4종은 app.css의 .tone-0 ~ .tone-3 클래스가 담당한다(다크 모드용 어두운 톤 포함).
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
    const away = !!data.away;
    const next = new Date(data.next || state.next);
    if ($('#status-text')) $('#status-text').textContent = away ? '입력 없어서 잠김' : enabled ? '알림 실행 중' : '알림 대기 중';
    if ($('#status-dot')) $('#status-dot').classList.toggle('running', enabled && !away);
    if ($('#next-time')) $('#next-time').textContent = enabled && !Number.isNaN(next.getTime()) ? next.toLocaleTimeString('ko-KR', { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }) : '직접 시작하세요';
    if ($('#next-date')) $('#next-date').textContent = enabled && !Number.isNaN(next.getTime()) ? next.toLocaleDateString('ko-KR', { month: 'long', day: 'numeric', weekday: 'short' }) : '간격과 주제를 선택한 뒤';
    renderNextList(enabled ? new Date(data.now || state.now) : null);
  }
  // "다음 작은 쉼" 목록: 주기 알림의 다음 시각 아래에, "몇 시에 실행" 카드의 시각도 보여 준다.
  // 오늘 기준으로 (1) 아직 지나지 않은 시각, (2) 재생 안 하는 구간(주말·근무 창 밖·쉬는 시간)에
  // 해당하지 않는 시각만 뜬다 — 목록에 보이는 시각만 실제로 뜬다(호스트의 발사 규칙과 같다).
  function renderNextList(now) {
    const box = $('#next-list'); if (!box) return;
    if (!now) { box.innerHTML = ''; return; }
    const s = state.settings;
    const items = [];
    if (s.WeekdaysOnly && (now.getDay() === 0 || now.getDay() === 6)) { box.innerHTML = ''; return; }
    const nowSec = now.getHours() * 3600 + now.getMinutes() * 60 + now.getSeconds();
    const startSec = (s.StartHour || 0) * 3600 + (s.StartMinute || 0) * 60;
    const endSec = (s.EndHour || 0) * 3600 + (s.EndMinute || 0) * 60;
    // 쉬는 시간은 이제 여러 개다 — 시각이 어느 구간에라도 들면 목록에서 제외한다.
    const breaks = breakList(s).map(b => [((b.StartHour || 0) * 3600) + (b.StartMinute || 0) * 60, ((b.EndHour || 0) * 3600) + (b.EndMinute || 0) * 60]);
    Object.keys(s.Schedules || {}).forEach(id => {
      const t = parseClock(s.Schedules[id]); if (!t) return;
      const at = t[0] * 3600 + t[1] * 60 + t[2];
      if (at <= nowSec) return; // 과거 지난 시간은 생략
      if (s.LimitHours && (at < startSec || at >= endSec)) return; // 시작 전·마감 후 제외
      if (s.BreakEnabled && breaks.some(([bStart, bEnd]) => at >= bStart && at < bEnd)) return; // 쉬는 시간(점심 등) 내 제외
      const theme = (state.themes || []).find(x => x.id === id); if (!theme) return;
      items.push({ at: t, name: theme.name });
    });
    items.sort((a, b) => a.at[0] * 3600 + a.at[1] * 60 + a.at[2] - (b.at[0] * 3600 + b.at[1] * 60 + b.at[2]));
    box.innerHTML = items.length
      ? '<div class="next-list-label">정해진 시각에 뜨는 쉼</div>' + items.slice(0, 5).map(e => `<div class="next-item"><b>${hms(e.at[0], e.at[1], e.at[2])}</b><span>${escape(e.name)}</span></div>`).join('')
      : '';
  }
  function extraThemes() {
    return [
      { id: 'dance', name: '댄스 안무', summary: '공식 안무 영상으로 원본 동작 따라 보기', instructions: ['공식 연습영상을 고르세요.', '서서 하는 움직임이므로 충분한 공간을 확보하세요.'], safety: '앉아서 하는 운동이 아닙니다. 통증이 있으면 어려운 동작을 건너뛰세요.', file: '' },
      { id: 'relax', name: '릴렉스 음향', summary: '편안한 소리를 들으며 잠깐 쉬기', instructions: [], safety: '소리는 작게 시작하고, 불편하면 바로 끄세요. 운전·보행 중에는 듣지 마세요. 치료가 아닌 짧은 휴식용 안내입니다.', file: '' },
      { id: 'recipe', name: '요리 레시피', summary: '짧은 요리 영상으로 잠깐 쉬어 가기', instructions: [], safety: '영상은 구경하며 쉬는 용도입니다. 실제로 따라 할 때는 불과 칼을 조심하고, 알레르기나 식이 제한이 있으면 재료를 직접 확인하세요.', file: '' }
    ];
  }
  function themeCards(checked, schedules) {
    // Every card can be pulled out of the list except the dance card (kept as the official-choreography
    // card) — the preview tile shows no actions at all. GitHub deletion is gone with the sync feature.
    // A card runs one of two ways, never both: "주기 실행" (the periodic checkbox, right/top of the
    // card) or "몇 시에 실행" (an absolute daily time). Choosing one greys the other out.
    return state.themes.map((t, i) => {
      const when = (schedules && schedules[t.id]) || '';
      const scheduled = Boolean(when);
      const periodic = checked.has(t.id) && !scheduled;
      return `<article class="theme-card${periodic || scheduled ? ' is-active' : ''} tone-${i % 4}">
        <div class="theme-top"><span class="theme-symbol">${symbolFor(t)}</span>
          <label class="theme-check" title="체크하면 정해진 간격으로 반복 실행돼요"><input name="theme" type="checkbox" value="${escape(t.id)}" ${periodic ? 'checked' : ''} ${scheduled ? 'disabled' : ''} aria-label="${escape(t.name)} 주기 실행"><span>주기 실행</span></label></div>
        <h3>${escape(t.name)}</h3><p>${escape(t.summary)}</p>
        <div class="card-schedule" title="정해진 시각에 하루 한 번만 띄워요. 켜면 주기 실행은 꺼져요.">
          <label class="theme-when"><input type="checkbox" name="theme-schedule" value="${escape(t.id)}" ${scheduled ? 'checked' : ''} ${periodic ? 'disabled' : ''} aria-label="${escape(t.name)} 몇 시에 실행"><input type="text" class="time24 card-time" value="${escape(when)}" placeholder="HH:MM:SS" maxlength="8" inputmode="numeric" ${scheduled && !periodic ? '' : 'disabled'} aria-label="${escape(t.name)} 실행 시각, 24시간제 시:분:초"><span>(24시각)에 실행</span></label>
        </div>
        <div class="card-actions"><button class="preview" data-preview="${escape(t.id)}" type="button">미리 보기 ↗</button>${t.id !== 'preview' && t.id !== 'dance' ? `<button class="card-delete" data-remove-theme="${escape(t.id)}" type="button" title="내 목록에서 이 카드를 삭제해요. '기본 프리셋으로 되돌리기'로 다시 가져올 수 있어요.">카드삭제</button>` : ''}</div></article>`;
    }).join('');
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
  function knoxSyncText() {
    const sync = state.knoxSync || {};
    if (sync.offline) return '이 환경에서는 일정 동기화를 쓰지 않아요.';
    if (sync.syncing) return 'Knox 일정을 불러오는 중…';
    if (sync.error) return `${sync.error}`;
    if (!sync.enabled) return '꺼져 있어요. 테스트로 직접 가져올 수 있어요.';
    const when = sync.last ? new Date(sync.last).toLocaleTimeString('ko-KR', { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }) : '';
    return `${when ? `${when}에 확인함` : '아직 확인 전이에요'} · 약 15분마다 자동 확인`;
  }
  function renderKnoxLog() {
    const box = $('#knox-log'); if (!box) return;
    const entries = (state.knoxSync && state.knoxSync.log) || [];
    box.innerHTML = entries.length ? entries.map(e => {
      const when = e.When ? new Date(e.When).toLocaleString('ko-KR', { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }) : '';
      return `<div class="knox-log-line"><span class="knox-log-time">${escape(when)}</span><span class="knox-log-result ${e.Ok ? 'ok' : 'fail'}">${e.Ok ? '성공' : '실패'}</span><span class="knox-log-detail">${escape(e.Detail || '')}</span></div>`;
    }).join('') : '기록이 아직 없어요.';
  }
  function knoxEntryLabel(e) {
    const start = e.start ? new Date(e.start) : null;
    const when = start && !Number.isNaN(start.getTime()) ? start.toLocaleString('ko-KR', { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }) : '';
    return (when ? when + ' ' : '') + (e.title || '제목 없는 일정');
  }
  function renderKnoxTest() {
    const pick = $('#knox-test-pick'), btn = $('#knox-test'); if (!pick || !btn) return;
    const entries = (state.knoxSync && state.knoxSync.calendar) || [];
    const selected = pick.value;
    pick.innerHTML = entries.length
      ? entries.map(e => `<option value="${escape(e.uid)}">${escape(knoxEntryLabel(e))}</option>`).join('')
      : '<option value="">가져온 다가오는 일정이 없어요 · 이미 시작한 일정은 여기에 나오지 않아요</option>';
    if (entries.some(e => e.uid === selected)) pick.value = selected;
    btn.disabled = entries.length === 0;
  }
  function updateKnoxSync() {
    if ($('#knox-sync-text')) $('#knox-sync-text').textContent = knoxSyncText();
    if ($('#knox-fetch')) $('#knox-fetch').disabled = Boolean(state.knoxSync && state.knoxSync.syncing);
    renderKnoxLog();
    renderKnoxTest();
  }
  // "주기 실행" ↔ "몇 시에 실행": a card runs one way or the other, never both — the choice
  // not taken is greyed out (disabled). This runs after every change and after each rebuild.
  function syncCardModes() {
    document.querySelectorAll('.theme-card').forEach(card => {
      const periodic = card.querySelector('input[name=theme]');
      const when = card.querySelector('input[name=theme-schedule]');
      const time = card.querySelector('.card-time');
      if (!periodic || !when || !time) return;
      if (when.checked) periodic.checked = false; // mutually exclusive, whatever the DOM came with
      periodic.disabled = when.checked;
      when.disabled = periodic.checked;
      time.disabled = !when.checked;
    });
  }
  function bindThemeGrid() {
    document.querySelectorAll('[data-preview]').forEach(button => button.addEventListener('click', () => send('preview', { themeId: button.dataset.preview })));
    // Pulling a card out of my list: a local edit, undone any time by "기본 프리셋으로 되돌리기".
    // (GitHub deletion is gone with the sync feature; shared cards are simply pulled out locally.)
    document.querySelectorAll('[data-remove-theme]').forEach(button => button.addEventListener('click', () => {
      const id = button.dataset.removeTheme;
      if (id === 'dance') { toast('댄스 안무 카드는 삭제할 수 없어요.'); return; }
      send('remove-theme', { themeId: id });
      toast('카드를 삭제했어요. 기본 프리셋으로 되돌리기로 다시 가져올 수 있어요.');
    }));
    const count = () => {
      const n = document.querySelectorAll('input[name=theme]:checked').length;
      $('#selected-count').textContent = `${n}개 선택`;
    };
    document.querySelectorAll('input[name=theme], input[name=theme-schedule]').forEach(input => input.addEventListener('change', () => { syncCardModes(); count(); }));
    document.querySelectorAll('.card-time').forEach(input => input.addEventListener('blur', () => { const t = parseClock(input.value); if (t) input.value = hms(t[0], t[1], t[2]); }));
    syncCardModes(); count();
  }
  // Rebuilds only the tiles, so choices the user ticked but has not saved yet survive a background sync.
  function refreshThemeGrid() {
    const grid = $('#theme-grid'); if (!grid) return;
    const checked = new Set(Array.from(document.querySelectorAll('input[name=theme]:checked'), x => x.value));
    const known = new Set(state.themes.map(t => t.id));
    const keep = new Set(Array.from(checked).filter(id => known.has(id)));
    if (!keep.size) state.settings.ThemeIds.filter(id => known.has(id)).forEach(id => keep.add(id));
    // Unsaved "몇 시에 실행" edits ride along the same way; with none in the DOM, the saved ones show.
    const schedEdits = {};
    document.querySelectorAll('input[name=theme-schedule]:checked').forEach(cb => {
      const time = cb.closest('.theme-card').querySelector('.card-time');
      const t = parseClock(time.value);
      if (known.has(cb.value)) schedEdits[cb.value] = (t ? hms(t[0], t[1], t[2]) : time.value);
    });
    grid.innerHTML = themeCards(keep, Object.keys(schedEdits).length ? schedEdits : (state.settings.Schedules || {})); bindThemeGrid(); updateSync();
  }
  function sharePanel() {
    const L = core.LIMITS;
    return `<dialog class="share-panel" id="share-panel"><div class="share-head"><h2>내 쉼 공유하기</h2><button id="share-close" class="quiet" type="button">닫기 ×</button></div>
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
  // The "새 쉼 카드 추가" dialog: a private card on this PC (never shared, never synced).
  function addThemePanel() {
    const L = core.LIMITS;
    return `<dialog class="share-panel" id="theme-add-panel"><div class="share-head"><h2>새 쉼 카드 추가</h2><button id="theme-add-close" class="quiet" type="button">닫기 ×</button></div>
      <p id="theme-add-note" class="share-note" role="status" aria-live="polite"></p>
      <form id="theme-add-form" class="share-form" autocomplete="off">
        <label>이름 <small>${L.name}자</small><input id="add-name" maxlength="${L.name}" placeholder="예: 손목 스트레칭"></label>
        <label>한 줄 설명 <small>${L.summary}자</small><input id="add-summary" maxlength="${L.summary}" placeholder="타일에 보이는 짧은 설명"></label>
        <label>동작 안내 <small>한 줄에 하나 · ${L.steps}줄까지</small><textarea id="add-steps" rows="4" placeholder="손바닥을 위로 해서 팔을 뻗어요&#10;손목을 천천히 돌려요"></textarea></label>
        <label>유튜브 영상 <small>선택 · 한 줄에 하나 · 링크 또는 ID | 제목 · ${L.videos}개까지</small><textarea id="add-videos" rows="2" placeholder="https://www.youtube.com/watch?v=영상ID | 영상 제목(선택)"></textarea></label>
        <label>주의사항 <small>필수 · ${L.safety}자</small><textarea id="add-safety" rows="2" maxlength="${L.safety}" placeholder="통증·저림이 생기면 바로 중단하세요."></textarea>
        <div class="share-buttons"><button class="primary" type="submit">카드 추가</button><button id="theme-add-cancel" class="secondary" type="button">취소</button></div>
      </form>
      <p class="small-help">이 PC에만 저장돼요. 공유되지 않고, '기본 프리셋으로 되돌리기'를 누르면 사라져요.</p></dialog>`;
  }
  function bindCardEditing() {
    // 쉼 카드 전체(카드 목록 + 각 카드의 하위 영상 목록)를 JSON 파일로 내보내기/가져오기.
    const exportCards = $('#export-cards'); if (exportCards) exportCards.addEventListener('click', () => send('export-cards', { json: buildCardsExport() }));
    const importCards = $('#import-cards'); if (importCards) importCards.addEventListener('click', () => send('import-cards-request'));
    const dlg = $('#theme-add-panel');
    if (!dlg) return;
    const note = text => { $('#theme-add-note').textContent = text || ''; };
    $('#add-theme-open').addEventListener('click', () => { note(''); if (dlg.showModal) dlg.showModal(); else dlg.setAttribute('open', ''); });
    $('#theme-add-close').addEventListener('click', () => dlg.close());
    $('#theme-add-cancel').addEventListener('click', () => dlg.close());
    $('#reset-themes').addEventListener('click', () => {
      // 296: 일정 알림 종소리 영상의 목록 편집도 함께 기본 프리셋으로 되돌린다(다른 카드의 목록 편집은
      // 각 목록 편집 패널의 '되돌리기'가 담당한다).
      try { localStorage.removeItem(editsKey('knox-bell')); } catch (_) { /* 저장소를 못 써도 카드 되돌리기는 계속 */ }
      send('reset-themes'); toast('카드 목록과 일정 알림 영상을 기본 프리셋으로 되돌렸어요.');
    });
    dlg.addEventListener('click', event => { if (event.target === dlg) dlg.close(); });
    $('#theme-add-form').addEventListener('submit', event => {
      event.preventDefault();
      const name = $('#add-name').value.trim(), summary = $('#add-summary').value.trim(), safety = $('#add-safety').value.trim();
      if (!name) { note('이름을 적어 주세요.'); return; }
      if (!summary) { note('한 줄 설명을 적어 주세요.'); return; }
      if (!safety) { note('주의사항은 안전을 위해 꼭 적어 주세요.'); return; }
      // Each video line: link-or-ID, an optional "| title" after it.
      const videoLines = [];
      for (const line of $('#add-videos').value.split(/\r?\n/)) {
        const text = line.trim();
        if (!text) continue;
        const bar = text.indexOf('|');
        const id = extractVideoId(bar < 0 ? text : text.slice(0, bar));
        if (!id) { note(`유튜브 영상 주소를 확인하세요: ${text.slice(0, 40)}`); return; }
        const title = (bar < 0 ? '' : text.slice(bar + 1)).trim();
        videoLines.push(title ? `${id}|${title}` : id);
        if (videoLines.length >= core.LIMITS.videos) break;
      }
      if (!$('#add-steps').value.trim() && !videoLines.length) { note('동작 안내를 한 줄 이상 적거나 영상을 하나 넣어 주세요.'); return; }
      send('add-theme', { name, summary, steps: $('#add-steps').value, videos: videoLines.join('\n'), safety });
      dlg.close();
      toast('새 쉼 카드를 추가했어요. 체크하면 알림에 포함돼요.');
    });
  }
  // ---- 쉼 카드 내보내기/가져오기: 카드 목록(내가 만든 카드·목록에서 뺀 카드·선택한 카드)과
  // 각 카드의 하위 영상 목록(목록 편집으로 넣고 뺀 것까지)을 JSON 파일 하나에 담아 백업하거나
  // 다른 PC로 옮긴다. 실제 영상 파일이 아니라 목록(제목·YouTube 영상 ID)만 저장하고,
  // 재생은 언제나 YouTube 재생기가 한다.
  function buildCardsExport() {
    const prefix = editsKey('');
    const listEdits = {};
    try {
      for (let i = 0; i < localStorage.length; i++) {
        const key = localStorage.key(i);
        if (key && key.indexOf(prefix) === 0) { try { listEdits[key.slice(prefix.length)] = JSON.parse(localStorage.getItem(key) || '{}'); } catch (_) { /* 못 읽은 값은 건너뛴다 */ } }
      }
    } catch (_) { /* localStorage 전체를 못 읽으면 빈 목록이라도 내보낸다 */ }
    // 종소리(일정 알림 영상) 목록 편집은 비어 있어도 항상 담는다 — 키가 없으면 가져온 쪽에서
    // '기본 4개'로 되돌릴 방법이 없어, 편집해 둔 종소리 목록이 못 지워진다(296).
    if (!('knox-bell' in listEdits)) listEdits['knox-bell'] = { removed: {}, added: [] };
    const cards = state.themes.map(t => ({
      id: t.id, name: t.name, summary: t.summary, safety: t.safety,
      user: Boolean(t.user), community: Boolean(t.community),
      instructions: (t.instructions || []).slice(),
      // baseVideos: 호스트가 저장한 이 카드의 영상 목록(가져오기가 카드를 되살릴 때 쓴다).
      baseVideos: (t.videos || []).map(v => ({ id: v.id, title: v.title })),
      // videos: 목록 편집까지 반영해 실제 보이는 하위 항목 전부(파일을 열어 확인용으로).
      videos: applyEdits(t.id, variantList(t)).map(v => ({ id: v.id, type: v.type || 'video', title: v.title, author: v.author || '', youtubeId: v.youtubeId || '' }))
    }));
    cards.push({ id: 'knox-bell', name: '일정 알림 영상', bell: true, videos: bellVideoList().map(v => ({ id: v.id, type: 'video', title: v.title, youtubeId: v.youtubeId || '' })) });
    return JSON.stringify({ format: 'teum-cards', version: 1, exportedAt: new Date().toISOString(),
      themeIds: (state.settings.ThemeIds || []).slice(),
      removedThemeIds: (state.removedThemeIds || []).slice(),
      cards, listEdits }, null, 2);
  }
  // 호스트가 선택한 파일을 읽어 내용을 돌려준 때: 목록 편집은 여기(localStorage)에 바로 두고,
  // 카드·뺀 목록·선택은 호스트에 맡긴다(호스트가 저장하고 화면을 다시 그린다).
  function applyCardsImport(raw) {
    let data = null;
    try { data = JSON.parse(raw); } catch (_) { data = null; }
    if (!data || data.format !== 'teum-cards' || !Array.isArray(data.cards)) { toast('틈의 \'영상 목록 내보내기\'로 저장한 파일만 가져올 수 있어요.'); return; }
    // 목록 편집은 '덮어쓰기'다: 이 PC의 편집을 전부 지우고 파일의 것으로 채운다. 내보내기 파일은
    // 내보낸 PC의 편집 전부를 담고 있으므로(종소리 포함), 가져온 뒤는 파일을 내보낸 PC와 같은
    // 목록이 된다 — 파일에 없는 편집이 남아 어긋나지 않게(296).
    try {
      const prefix = editsKey('');
      const stale = [];
      for (let i = 0; i < localStorage.length; i++) { const key = localStorage.key(i); if (key && key.indexOf(prefix) === 0) stale.push(key); }
      stale.forEach(key => localStorage.removeItem(key));
    } catch (_) { /* 저장소를 못 쓰면 파일에 담긴 편집만 덮어쓴다 */ }
    Object.keys(data.listEdits || {}).forEach(id => store.set(editsKey(id), JSON.stringify(data.listEdits[id])));
    send('apply-import', {
      // 호스트가 되살리는 건 내가 만든 카드뿐: 기본·공유 카드는 themes.json·동기화가 이미 채운다.
      cards: data.cards.filter(c => c && c.user).map(c => ({ id: c.id, name: c.name, summary: c.summary, safety: c.safety, instructions: c.instructions || [], videos: (c.baseVideos || []).map(v => ({ id: v.id, title: v.title })) })),
      removals: data.removedThemeIds || [],
      themeIds: data.themeIds || []
    });
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
  // ---- 쉬는 시간(점심 등) 여러 개: ＋로 한 줄씩 추가, －로 한 줄씩 뺀다 ----
  const BREAK_MAX = 8;
  let breakRows = []; // 설정 창이 그리고 있는 쉬는 시간 행(추가·삭제로 바뀐다)
  // 설정의 쉬는 시간 목록: 새 설정은 Breaks 목록을 그대로 쓰고, 옛 설정(Breaks 없음)은
  // 단일 필드 4개를 점심 한 줄로 되살린다.
  function breakList(s) {
    if (Array.isArray(s.Breaks)) return s.Breaks;
    return [{ StartHour: s.BreakStartHour == null ? 12 : s.BreakStartHour, StartMinute: s.BreakStartMinute == null ? 0 : (s.BreakStartMinute || 0), EndHour: s.BreakEndHour == null ? 13 : s.BreakEndHour, EndMinute: s.BreakEndMinute == null ? 0 : (s.BreakEndMinute || 0) }];
  }
  function breakRowHtml(b, i, on) {
    return `<div class="schedule-line break-times break-row">쉬는 시간 <input ${i === 0 ? 'id="break-start" ' : ''}class="time24 break-start" type="text" inputmode="numeric" maxlength="5" placeholder="HH:MM" value="${hm(b.StartHour, b.StartMinute)}" aria-label="쉬는 시간 ${i + 1} 시작 (24시간, 시:분)" ${on ? '' : 'disabled'}> 부터 <input ${i === 0 ? 'id="break-end" ' : ''}class="time24 break-end" type="text" inputmode="numeric" maxlength="5" placeholder="HH:MM" value="${hm(b.EndHour, b.EndMinute)}" aria-label="쉬는 시간 ${i + 1} 끝 (24시간, 시:분)" ${on ? '' : 'disabled'}> 까지 <button class="quiet break-remove" type="button" data-i="${i}" title="이 쉬는 시간을 목록에서 빼요" aria-label="쉬는 시간 ${i + 1} 빼기" ${on ? '' : 'disabled'}>－</button></div>`;
  }
  function renderBreakList(on) {
    const box = $('#break-list'); if (!box) return;
    box.innerHTML = breakRows.map((b, i) => breakRowHtml(b, i, on)).join('') +
      `<div class="schedule-line break-add-line"><button id="break-add" class="quiet" type="button" title="쉬는 시간을 한 줄 더 추가해요(최대 ${BREAK_MAX}개)" ${on && breakRows.length < BREAK_MAX ? '' : 'disabled'}>＋ 쉬는 시간 추가</button></div>`;
    box.querySelectorAll('.break-start, .break-end').forEach(input => input.addEventListener('blur', () => { const t = parseTime(input.value); if (t) input.value = hm(t[0], t[1]); }));
    box.querySelectorAll('.break-remove').forEach(btn => btn.addEventListener('click', () => { syncBreakRowsFromDom(); breakRows.splice(Number(btn.dataset.i), 1); renderBreakList(on); }));
    const add = $('#break-add');
    if (add) add.addEventListener('click', () => { syncBreakRowsFromDom(); breakRows.push({ StartHour: 12, StartMinute: 0, EndHour: 13, EndMinute: 0 }); renderBreakList(on); });
  }
  // 입력칸의 현재 값을 행 목록으로 옮긴다 — 추가·빼기·토글로 다시 그릴 때 사용자가 적은 시각이 사라지지 않게.
  // 해석 못 하는 값은 점심 기본값(12:00~13:00)으로 돌아간다(켜져 있을 때만 저장이 막힌다).
  function syncBreakRowsFromDom() {
    const rows = Array.from(document.querySelectorAll('#break-list .break-row'));
    breakRows = rows.map(row => {
      const st = parseTime(row.querySelector('.break-start').value) || [12, 0];
      const en = parseTime(row.querySelector('.break-end').value) || [13, 0];
      return { StartHour: st[0], StartMinute: st[1], EndHour: en[0], EndMinute: en[1] };
    });
  }
  function renderSettings() {
    const s = state.settings;
    root.innerHTML = `<div class="shell">
      <aside class="sidebar"><div class="brand">틈<small>TEUM · A LITTLE RESET${state.version ? ' · v' + escape(state.version) : ''}</small></div>
      <p class="side-note">몰입하는 시간 사이,<br>나를 돌보는 작은 틈.</p>
      <div class="side-badge"><i id="status-dot" class="dot"></i><span id="status-text"></span><br><span style="font-size:11px;color:#7d8c76">설정 창을 닫아도 트레이에 남아요.</span></div>
      <div class="next-label">다음 작은 쉼</div><div id="next-time" class="next-time"></div><div id="next-date" class="small-help"></div><div id="next-list" class="next-list" aria-live="polite"></div>
      <div class="side-bottom">내 PC 안에서만 동작합니다.<br>마이크·카메라·계정이 필요 없어요.<button id="hide-window" class="quiet">설정 창 닫기(트레이에 두기) ↗</button><button id="exit-app" class="quiet">알림기 완전히 종료</button></div></aside>
      <main class="main"><div class="heading"><div><div class="eyebrow">YOUR PERSONAL BREAK RITUAL</div><h1>잠깐, 나를 돌보는 시간.</h1><p>알림 간격과 하고 싶은 주제를 골라 주세요. 여러 개를 고르면 순서대로 만나요.</p></div></div>
      ${!host ? '<div class="native-warning">HTML 미리보기입니다. 창을 닫은 뒤에도 정해진 간격으로 전체 화면을 띄우려면 같은 폴더의 DeskHooker.exe를 실행하세요.</div>' : ''}
      <div class="settings-row"><section class="setting-card"><h2><span class="section-no">01</span>어느 간격으로 만날까요?</h2><div class="interval-display"><input id="start-time" class="time24" type="text" inputmode="numeric" maxlength="5" placeholder="HH:MM" value="${hm(s.StartHour, s.StartMinute)}" aria-label="시작 시각 (24시간, 시:분)" ${s.LimitHours ? '' : 'disabled'}><span>부터</span><input id="interval" type="number" min="1" max="240" value="${s.IntervalMinutes}" aria-label="팝업 간격, 분"><span>분마다</span></div><p class="small-help">시작 시각은 24시간제(시:분, 예: 13:30)로, 간격은 1~240 사이의 분으로 직접 입력하세요.<br>알림은 시작 시각을 기준으로 고정된 시각표(시작 시각 + 간격의 배수, 예: 09:00 시작 · 60분이면 10:00, 11:00, 12:00…)에 맞춰 떠요.<br>팝업을 늦게 닫아도 시각표가 밀리지 않고, 놓친 알림은 몰아서 띄우지 않아요.</p><button id="run-now" class="secondary" type="button" style="margin-top:14px">▶ 즉시 실행 (시험용)</button><p class="small-help">시간대·요일·일시정지와 관계없이, 저장된 주제 순서대로 팝업을 바로 띄웁니다.</p></section>
      <section class="setting-card"><h2><span class="section-no">02</span>언제 알림을 받을까요?</h2><label class="schedule-line"><input id="weekdays" type="checkbox" ${s.WeekdaysOnly ? 'checked' : ''}>월요일부터 금요일까지만</label><label class="schedule-line"><input id="limit-hours" type="checkbox" ${s.LimitHours ? 'checked' : ''}>시작·종료 시각 사이에만 알림 받기</label><div class="schedule-line">종료 <input id="end-time" class="time24" type="text" inputmode="numeric" maxlength="5" placeholder="HH:MM" value="${hm(s.EndHour, s.EndMinute)}" aria-label="종료 시각 (24시간, 시:분)" ${s.LimitHours ? '' : 'disabled'}> 전까지 (24시간제 · 24:00 또는 00:00은 자정)</div><label class="schedule-line"><input id="break-on" type="checkbox" ${s.BreakEnabled ? 'checked' : ''}>중간에 쉬는 시간(예: 점심)에는 알림 끄기</label><div id="break-list" class="break-list"></div><label class="schedule-line"><input id="login-start" type="checkbox" ${s.StartAtLogin ? 'checked' : ''}>Windows 로그인 때 자동 실행</label><label class="schedule-line"><input id="pause-idle" type="checkbox" ${s.PauseWhenIdle ? 'checked' : ''}>마우스·키보드 입력이 <input id="idle-min" type="number" min="1" max="240" value="${s.IdleMinutes || 10}" style="width:4em" ${s.PauseWhenIdle ? '' : 'disabled'}> 분 이상 없으면 자리비움으로 보고 알림 멈추기</label><p class="small-help">PC의 현지 시각 기준 · 잠금/절전 중에는 쉬어요.<br>자리비움으로 판단되면 열려 있는 쉼 창도 자동으로 닫아요 (일정 알림 창은 제외).<br>알림을 놓쳐도 한꺼번에 몰아서 띄우지 않아요.</p></section></div>
      <div class="section-head"><h2><span class="section-no">03</span>어떤 작은 쉼이 필요하세요?</h2><div class="mode-pick"><span id="selected-count"></span><span aria-hidden="true">·</span><select id="theme-mode-select" aria-label="쉼 여러 개를 골랐을 때 실행 방식" title="여러 개를 골랐을 때 알림마다 하나를 띄우는 방식이에요.">${themeModes.map(m => `<option value="${m.id}" ${m.id === (s.ThemeMode || 'ordered-one') ? 'selected' : ''}>${m.label}</option>`).join('')}</select></div></div>
      <div id="theme-grid" class="themes">${themeCards(new Set(s.ThemeIds), s.Schedules || {})}</div>
      <div class="card-edit-bar"><button id="add-theme-open" class="secondary" type="button">＋ 새 쉼 카드 추가</button><button id="reset-themes" class="quiet" type="button">기본 프리셋으로 되돌리기</button><span class="card-bar-spacer"></span><button id="export-cards" class="quiet" type="button" title="지금 가진 쉼 카드 전부와 각 카드의 하위 영상 목록(일정 알림 종소리 영상 포함)을 JSON 파일 하나로 저장해요. 다른 PC에서 '영상 목록 가져오기'로 그대로 되살릴 수 있어요.">영상 목록 내보내기 ⤓</button><button id="import-cards" class="quiet" type="button" title="'영상 목록 내보내기'로 저장한 JSON 파일에서 쉼 카드와 영상 목록(일정 알림 종소리 영상 포함)을 가져와요. 목록은 파일을 내보낸 PC와 같아져요.">영상 목록 가져오기 ⤒</button></div>
      <section class="setting-card knox-bell-card"><div class="knox-bell-head"><h2>일정 알림 영상</h2><span class="small-help">주기 알림에는 쓰지 않아요 · 일정 알림 화면에 무작위(기본) 또는 순서대로 하나가 재생돼요</span></div><article class="theme-card tone-bell"><label><div class="theme-top"><span class="theme-symbol">◈</span></div><h3>일정 알림</h3><p>일정 알림 화면에 쓰이는 종소리 영상 모음이에요. 영상을 넣거나 뺄 수 있어요.</p></label><div class="card-actions"><button class="preview" data-preview="knox-bell" type="button">영상 관리 ↗</button></div></article></section>
      ${addThemePanel()}
      ${sharePanel()}
      <div class="section-head"><h2><span class="section-no">04</span>회의 시작 전에도 알려드릴까요?</h2></div>
      <section class="setting-card"><label class="schedule-line"><input id="knox-on" type="checkbox" ${s.KnoxCalendarEnabled ? 'checked' : ''}>Knox 캘린더 일정을 시작 전에 따로 알려주기</label><div class="schedule-line">시작 <input id="knox-lead" type="number" min="0" max="60" value="${s.KnoxLeadMinutes}" style="width:4em" ${s.KnoxCalendarEnabled ? '' : 'disabled'}> 분 전에</div><div class="schedule-line knox-auto-line">일정 알림 자동 닫힘 <label><input type="radio" name="knox-auto-close" value="none" ${s.KnoxAutoClose === 'none' || !s.KnoxAutoClose ? 'checked' : ''} ${s.KnoxCalendarEnabled ? '' : 'disabled'}>없음</label><label><input type="radio" name="knox-auto-close" value="start" ${s.KnoxAutoClose === 'start' ? 'checked' : ''} ${s.KnoxCalendarEnabled ? '' : 'disabled'}>일정시작시간에</label><label><input type="radio" name="knox-auto-close" value="after" ${s.KnoxAutoClose === 'after' ? 'checked' : ''} ${s.KnoxCalendarEnabled ? '' : 'disabled'}>일정시작후 <input id="knox-auto-close-min" type="number" min="1" max="240" value="${s.KnoxAutoCloseMinutes || 5}" style="width:4em" ${s.KnoxCalendarEnabled && s.KnoxAutoClose === 'after' ? '' : 'disabled'}> 분후</label><label><input type="radio" name="knox-auto-close" value="end" ${s.KnoxAutoClose === 'end' ? 'checked' : ''} ${s.KnoxCalendarEnabled ? '' : 'disabled'}>일정끝난시간에</label></div><div class="schedule-line knox-fetch-line"><span id="knox-sync-text" class="small-help" style="margin:0;flex:1;min-width:180px"></span><button id="knox-fetch" class="quiet" type="button" title="15분 주기를 기다리지 않고 지금 바로 Knox 일정을 가져와 봅니다(테스트용)">지금 일정 가져오기 ↻</button></div><div class="schedule-line knox-fetch-line"><span class="small-help" style="margin:0;flex-shrink:0">알림 시험</span><select id="knox-test-pick" aria-label="알림을 시험할 일정"></select><button id="knox-test" class="quiet" type="button" title="선택한 일정의 시작 알림 팝업을 지금 바로 띄워 봅니다. 실제 알림에는 영향이 없어요.">일정 알리기 시험</button></div><details id="knox-log-box" class="knox-log-box"><summary>동작 기록 보기</summary><div id="knox-log" class="knox-log"></div></details><p class="small-help">회사 Knox 일정 서버에서 이 PC 로그인 계정 이름으로 내 일정을 직접 받아 옵니다(별도 로그인·설치 없음). 간격·주제 알림과는 별도로, 회의 제목과 시각만 보여주는 짧은 알림이 떠요. 시도·결과는 위 기록에 쌓여요. 알림이 뜨기 약 30초 전부터 영상을 미리 준비해 두고, 정해진 시각에 바로 띄워요. '자동 닫힘 · 없음'이면 직접 닫을 때까지 떠 있고, 종료 시각이 없는 일정에서 '일정끝난시간에'는 자동으로 닫히지 않아요.</p></section>
      <div class="actions"><button id="start-reminders" class="primary">${s.Enabled ? '변경 적용 · 알림 계속' : '선택한 주제로 알림 시작'} →</button><button id="save-settings" class="secondary">설정만 저장</button><button id="pause-reminders" class="quiet">알림 일시정지</button><small>팝업은 저절로 닫히지 않아요.<br>닫기 또는 Esc를 누르면 다음 쉼까지 대기해요.</small></div>
      <p class="small-help">일반적인 휴식·움직임 안내이며 치료·재활 처방이 아닙니다. 통증이 늘거나 저림·어지럼이 생기면 중단하세요. 허리 통증과 함께 새로 생긴 다리 힘 빠짐, 회음부 감각 저하, 대소변 조절 변화가 있으면 운동보다 즉시 진료가 우선입니다.</p>
      </main></div>`;
    bindThemeGrid(); bindCardEditing(); bindShare(); updateSync(); updateKnoxSync();
    ['#start-time', '#end-time'].forEach(sel => $(sel).addEventListener('blur', () => { const t = parseTime($(sel).value); if (t) $(sel).value = hm(t[0], t[1]); }));
    breakRows = breakList(s).slice(0, BREAK_MAX);
    renderBreakList(s.BreakEnabled);
    $('#break-on').addEventListener('change', event => { syncBreakRowsFromDom(); renderBreakList(event.target.checked); });
    // Off → grey + read-only, on → editable: every dependent input follows its controlling checkbox.
    $('#limit-hours').addEventListener('change', event => { $('#start-time').disabled = $('#end-time').disabled = !event.target.checked; });
    $('#pause-idle').addEventListener('change', event => { $('#idle-min').disabled = !event.target.checked; });
    const syncKnoxEditability = () => {
      const on = $('#knox-on').checked;
      $('#knox-lead').disabled = !on;
      document.querySelectorAll('input[name=knox-auto-close]').forEach(radio => { radio.disabled = !on; });
      // The auto-close minutes box needs both the Knox feature and the "일정시작후 N분후" option.
      const after = ((document.querySelector('input[name=knox-auto-close]:checked') || {}).value === 'after');
      $('#knox-auto-close-min').disabled = !on || !after;
    };
    $('#knox-on').addEventListener('change', syncKnoxEditability);
    document.querySelectorAll('input[name=knox-auto-close]').forEach(radio => radio.addEventListener('change', syncKnoxEditability));
    $('#run-now').addEventListener('click', () => send('run-now'));
    $('#knox-fetch').addEventListener('click', () => { if (!host) { toast('일정 가져오기는 DeskHooker.exe에서 동작해요.'); return; } send('sync-knox'); toast('지금 Knox 일정을 가져오는 중이에요. 결과는 동작 기록에 쌓여요.'); });
    $('#knox-test').addEventListener('click', () => {
      const uid = $('#knox-test-pick').value;
      if (!host) { toast('알림 시험은 DeskHooker.exe에서 동작해요.'); return; }
      if (!uid) { toast('시험할 일정이 없어요. 먼저 지금 일정 가져오기로 일정을 받아 주세요.'); return; }
      send('test-knox', { uid }); toast('선택한 일정의 시작 알림 팝업을 띄워 봅니다.');
    });
    $('#start-reminders').addEventListener('click', () => save(true));
    $('#save-settings').addEventListener('click', () => save(s.Enabled));
    $('#pause-reminders').addEventListener('click', () => save(false));
    $('#hide-window').addEventListener('click', () => send('close'));
    $('#exit-app').addEventListener('click', () => send('exit'));
    updateStatus(state);
  }
  function hm(h, m) { return String(h).padStart(2, '0') + ':' + String(m || 0).padStart(2, '0'); }
  function hms(h, m, s) { return hm(h, m) + ':' + String(s || 0).padStart(2, '0'); }
  // 24-hour "HH:MM" (also accepts "930" or "0930"); hour 0-24, minute 0-59, and 24 only as 24:00. Returns [h, m] or null.
  function parseTime(v) {
    let text = String(v || '').trim();
    if (/^\d{3,4}$/.test(text)) text = text.slice(0, -2) + ':' + text.slice(-2);
    const m = /^(\d{1,2}):(\d{2})$/.exec(text);
    if (!m) return null;
    const h = Number(m[1]), min = Number(m[2]);
    return h > 24 || min > 59 || (h === 24 && min !== 0) ? null : [h, min];
  }
  // "몇 시에 실행" 시각: 24-hour [시:분:초]. Seconds are optional ("13:30" reads as 13:30:00, so old
  // saved times keep loading); compact "133000"/"0930" work too. Hour is 0-23 (no 24:00 here — the
  // day has no 24:00:00 second). Returns [h, m, s] or null.
  function parseClock(v) {
    let text = String(v || '').trim();
    if (/^\d{3,4}$/.test(text)) text = text.slice(0, -2) + ':' + text.slice(-2) + ':00';
    else if (/^\d{5,6}$/.test(text)) text = text.slice(0, -4) + ':' + text.slice(-4, -2) + ':' + text.slice(-2);
    const m = /^(\d{1,2}):(\d{2})(?::(\d{2}))?$/.exec(text);
    if (!m) return null;
    const h = Number(m[1]), min = Number(m[2]), sec = m[3] === undefined ? 0 : Number(m[3]);
    return h > 23 || min > 59 || sec > 59 ? null : [h, min, sec];
  }
  function save(enabled) {
    const st = parseTime($('#start-time').value), et = parseTime($('#end-time').value);
    if (!st || !et || st[0] > 23) { toast('시작·종료 시각을 24시간제 시:분으로 입력하세요. (예: 09:00, 18:30)'); return; }
    // Quiet periods (e.g. lunch): one or more rows. Only used when switched on, so a half-typed
    // value never blocks saving while it is off (it falls back to the 12:00~13:00 default).
    const breakOn = $('#break-on').checked;
    const breaks = [];
    for (const row of document.querySelectorAll('#break-list .break-row')) {
      const st = parseTime(row.querySelector('.break-start').value), en = parseTime(row.querySelector('.break-end').value);
      if (breakOn && (!st || !en)) { toast('쉬는 시간을 24시간제 시:분으로 입력하세요. (예: 12:00 ~ 13:00)'); return; }
      breaks.push({ StartHour: st ? st[0] : 12, StartMinute: st ? st[1] : 0, EndHour: en ? en[0] : 13, EndMinute: en ? en[1] : 0 });
    }
    if (breakOn) {
      for (const b of breaks) {
        if (b.EndHour * 60 + b.EndMinute <= b.StartHour * 60 + b.StartMinute) { toast('쉬는 시간은 끝이 시작보다 늦어야 해요. (자정까지는 24:00)'); return; }
      }
    }
    const knoxLead = Number($('#knox-lead').value);
    if (!Number.isInteger(knoxLead) || knoxLead < 0 || knoxLead > 60) { toast('일정 알림은 0~60분 사이의 정수로 고르세요.'); return; }
    const knoxAutoClose = (document.querySelector('input[name=knox-auto-close]:checked') || {}).value || 'none';
    const knoxAutoCloseMinutes = Number($('#knox-auto-close-min').value);
    if (!Number.isInteger(knoxAutoCloseMinutes) || knoxAutoCloseMinutes < 1 || knoxAutoCloseMinutes > 240) { toast('자동 닫힘 분은 1~240 사이의 정수로 고르세요.'); return; }
    const idleMinutes = Number($('#idle-min').value);
    if (!Number.isInteger(idleMinutes) || idleMinutes < 1 || idleMinutes > 240) { toast('자리비움 판단 시간은 1~240분 사이의 정수로 고르세요.'); return; }
    // "몇 시에 실행" cards: { theme id → "HH:MM:SS" }. Each runs once a day at that clock time and is
    // left out of the periodic rotation. Seconds are optional when typing ("13:30" = 13:30:00), so old
    // saved times keep working. A bad time stops the save with a toast.
    const schedules = {};
    for (const cb of document.querySelectorAll('input[name=theme-schedule]:checked')) {
      const input = cb.closest('.theme-card').querySelector('.card-time');
      const t = parseClock(input.value);
      if (!t) { toast('실행 시각은 24시간제(시:분:초, 예: 13:30:05)로 적어 주세요.'); return; }
      schedules[cb.value] = hms(t[0], t[1], t[2]);
    }
    const settings = {
      IntervalMinutes: Number($('#interval').value), WeekdaysOnly: $('#weekdays').checked, LimitHours: $('#limit-hours').checked,
      StartHour: st[0], StartMinute: st[1], EndHour: (et[0] === 0 && et[1] === 0) || et[0] === 24 ? 24 : et[0], EndMinute: (et[0] === 0 && et[1] === 0) || et[0] === 24 ? 0 : et[1], StartAtLogin: $('#login-start').checked,
      BreakEnabled: breakOn, Breaks: breaks,
      // 옛 버전과 주고받는 단일 필드 4개는 첫째 줄을 그대로 비춘다(Breaks가 있으면 C#은 목록만 본다).
      BreakStartHour: breaks.length ? breaks[0].StartHour : 12, BreakStartMinute: breaks.length ? breaks[0].StartMinute : 0, BreakEndHour: breaks.length ? breaks[0].EndHour : 13, BreakEndMinute: breaks.length ? breaks[0].EndMinute : 0,
      ThemeIds: Array.from(document.querySelectorAll('input[name=theme]:checked'), x => x.value), Enabled: enabled,
      ThemeMode: $('#theme-mode-select').value, // 순차적으로/무작위로 — 03 섹션 머리글의 드롭다운에서 고름
      Schedules: schedules,
      Appearance: 'system', // the theme is locked to "시스템 따르기"; the radios are shown disabled
      KnoxCalendarEnabled: $('#knox-on').checked, KnoxLeadMinutes: knoxLead,
      KnoxAutoClose: knoxAutoClose, KnoxAutoCloseMinutes: knoxAutoCloseMinutes,
      PauseWhenIdle: $('#pause-idle').checked, IdleMinutes: idleMinutes
    };
    if (!Number.isInteger(settings.IntervalMinutes) || settings.IntervalMinutes < 1 || settings.IntervalMinutes > 240) { toast('간격은 1~240분 사이의 정수로 고르세요.'); return; }
    if (!settings.ThemeIds.length && !Object.keys(schedules).length) { toast('주기 실행 또는 몇 시에 실행 중 하나는 골라 주세요.'); return; }
    if (!Number.isInteger(settings.StartHour) || !Number.isInteger(settings.EndHour) || settings.StartHour < 0 || settings.StartHour > 23 || settings.EndHour < 1 || settings.EndHour > 24 || settings.StartHour * 60 + settings.StartMinute >= settings.EndHour * 60 + settings.EndMinute) { toast('종료 시각은 시작 시각보다 늦어야 해요. (자정까지는 00:00)'); return; }
    if (breakOn) {
      // 여러 쉬는 시간을 겹치는 구간까지 합쳐 봤을 때 알림 시간 전체를 덮으면 안 된다.
      const minutes = (h, m) => h * 60 + m;
      const allowedStart = settings.LimitHours ? minutes(settings.StartHour, settings.StartMinute) : 0;
      const allowedEnd = settings.LimitHours ? minutes(settings.EndHour, settings.EndMinute) : 1440;
      const spans = breaks.map(b => [minutes(b.StartHour, b.StartMinute), minutes(b.EndHour, b.EndMinute)]).sort((a, b) => a[0] - b[0]);
      let cursor = allowedStart, covered = true;
      for (const [s, e] of spans) { if (e <= cursor) continue; if (s > cursor) { covered = false; break; } if (e > cursor) cursor = e; }
      if (covered && cursor >= allowedEnd) { toast('쉬는 시간이 알림을 받는 시간 전체를 덮고 있어요. 범위를 줄여 주세요.'); return; }
    }
    send('save', { settings });
  }
  function popupFrame(theme) {
    return `<main class="popup"><header class="popup-bar"><div class="popup-brand">틈<span>A LITTLE RESET${state.version ? ' · v' + escape(state.version) : ''} · ${escape(theme.name)}</span></div>${state.remaining > 0 ? `<button class="popup-next" type="button">다음 쉼 ›<span style="font-size:10px;opacity:.6"> ${state.remaining}개 남음</span></button>` : ''}<button class="popup-close" type="button">닫기 × <span style="font-size:10px;opacity:.6">Esc</span></button></header><div class="popup-content"><div class="popup-heading"><div class="eyebrow">잠깐 쉬어가도 괜찮아요</div><h1>${escape(theme.name)}</h1><p>${escape(theme.summary)}</p></div><div id="popup-stage"></div></div><footer class="popup-footer"><span>창을 닫으면 다음 알림까지 대기해요. 자동으로 닫거나 입력을 강요하지 않아요.</span><span>움직임은 편한 범위에서 · 통증·저림·어지럼이 생기면 중단</span></footer>${volumeBadgeHtml()}</main>`;
  }
  // ---- 팝업 볼륨: 우하단 표시 + ↑/↓ 키 입력 ----
  // YouTube 임베드에 enablejsapi로 postMessage 명령(setVolume)을 보내 소리를 바꾼다(댄스 속도·이동과
  // 같은 경로). 마우스 휠 조작과 별도로, 키보드만으로 간편히 조절하기 위한 것이다. 저장값(teum.volume)은
  // 이 PC의 모든 팝업(쉼·댄스·일정 알림)에서 함께 쓰고 다음 팝업에도 그대로 적용된다.
  const YT_VOLUME_ORIGIN = 'https://www.youtube-nocookie.com';
  const volumeBadgeHtml = () => `<div id="volume-badge" class="volume-badge" role="status" title="키보드의 ↑·↓ 방향키로 이 화면의 영상 소리를 조절해요 (5씩 변합니다)"></div>`;
  function popupVolume() { const v = Number(store.get('teum.volume')); return Number.isFinite(v) && v >= 0 && v <= 100 ? v : 70; }
  // 지금 화면에서 소리를 낼 수 있는 임베드: 쉼 영상(#variant-frame), 댄스(#dance-frame), 종소리 배경(#knox-bell).
  function videoFrames() { return ['#variant-frame', '#dance-frame', '#knox-bell'].map(id => document.querySelector(id)).filter(f => f && f.contentWindow); }
  function ytVolumeCommand(frame, func, args) { try { frame.contentWindow.postMessage(JSON.stringify({ event: 'command', func, args }), YT_VOLUME_ORIGIN); } catch (e) { /* 닫히는 중일 수 있다 */ } }
  function ytListen(frame) { try { frame.contentWindow.postMessage(JSON.stringify({ event: 'listening', id: 1, channel: 'widget' }), YT_VOLUME_ORIGIN); } catch (e) { /* 무시 */ } }
  // 저장된 볼륨을 각 임베드에 실제로 반영한다. 재생기는 로드 직후엔 명령을 안 받으므로 load 이후와
  // 짧은 지연 뒤에 두 번 더 보낸다. prewarm(숨겨진 예열 창) 중에는 조작하지 않는다 — 실제 알림 순간에
  // teumRevealBell이 소리를 켠다.
  function applyPopupVolume() {
    const vol = popupVolume();
    document.querySelectorAll('#variant-frame, #dance-frame, #knox-bell').forEach(frame => {
      const send = () => { ytListen(frame); ytVolumeCommand(frame, 'setVolume', [vol]); };
      if (frame.dataset.volumeBound === '1') { send(); return; }
      frame.dataset.volumeBound = '1';
      frame.addEventListener('load', send);
      setTimeout(send, 700);
      setTimeout(send, 2000);
    });
    updateVolumeBadge();
  }
  function updateVolumeBadge(changed) {
    const badge = document.getElementById('volume-badge'); if (!badge) return;
    placeVolumeBadge(badge); // 항상 닫기 버튼 바로 아래 · 오른쪽 끝 정렬 (우상단)
    if (!document.querySelector('#variant-frame, #dance-frame, #knox-bell')) {
      badge.textContent = '🔊 이 화면엔 소리가 없어요';
      badge.classList.add('is-muted');
      return;
    }
    badge.classList.remove('is-muted');
    badge.textContent = `🔊 볼륨 ${popupVolume()} · Up/Down (키보드키)`;
    if (changed) { badge.classList.remove('bump'); void badge.offsetWidth; badge.classList.add('bump'); }
  }
  // 닫기 버튼의 실제 화면 위치를 읽어, 배지를 그 바로 아래에 오른쪽 끝이 맞게 둔다. 팝업(header 안의
  // 닫기)과 일정 알림(absolute 닫기) 모두에서 정확히 붙고, 창 크기가 바뀌어도 매번 다시 맞춰진다.
  function placeVolumeBadge(badge) {
    const close = document.querySelector('.popup-close');
    if (!close) return;
    const r = close.getBoundingClientRect();
    badge.style.top = `${Math.round(r.bottom + 8)}px`;
    badge.style.right = `${Math.max(0, Math.round(window.innerWidth - r.right))}px`;
    badge.style.bottom = '';
  }
  // The bell videos played with the meeting reminder — a per-PC editable list (same editor as the
  // other cards, keyed "knox-bell"). Only the ids are stored; the YouTube player streams them.
  // One is drawn per reminder — at random (default, never repeating the previous one) or in list
  // order, following the "순서" choice on the bell-manager screen (VariantModes['knox-bell']).
  const KNOX_BELL_VIDEO_IDS = ['ADRjuOE-yJA', 'LcbVqTVA_1A', '2WtTN3FVySM', 'PTXt2dmry0g', '8bch0KYfeOM'];
  const bellBaseList = () => KNOX_BELL_VIDEO_IDS.map(id => ({ id: 'yt-' + id, type: 'video', title: '종소리 영상 ' + (KNOX_BELL_VIDEO_IDS.indexOf(id) + 1), author: '', youtubeId: id, url: 'https://www.youtube.com/watch?v=' + id }));
  const bellVideoList = () => applyEdits('knox-bell', bellBaseList());
  function pickBellVideoId() {
    const ids = bellVideoList().map(v => v.youtubeId).filter(Boolean);
    if (!ids.length) return null;
    if (ids.length <= 1) return ids[0];
    let idx;
    if (variantMode('knox-bell') === 'random') {
      do { idx = Math.floor(Math.random() * ids.length); }
      while (ids[idx] === pickBellVideoId.last);
    } else {
      const lastIdx = pickBellVideoId.last == null ? -1 : ids.indexOf(pickBellVideoId.last);
      idx = lastIdx >= 0 && lastIdx < ids.length - 1 ? lastIdx + 1 : 0;
    }
    pickBellVideoId.last = ids[idx];
    return ids[idx];
  }
  // The meeting reminder is a single page: the bell video fills the screen, the time and title sit
  // on a soft dark veil so they stay readable over any footage. In prewarm mode (knoxPrewarm) the
  // host has opened this page ~30s early, off-screen, to buffer the stream — so it starts muted and
  // teumRevealBell() un-mutes and restarts the video exactly when the window is revealed.
  function knoxPopupFrame() {
    const start = state.knoxStart ? new Date(state.knoxStart) : null;
    const end = state.knoxEnd ? new Date(state.knoxEnd) : null;
    const fmt = d => d && !Number.isNaN(d.getTime()) ? d.toLocaleTimeString('ko-KR', { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }) : '';
    const when = fmt(start);
    const prewarm = Boolean(state.knoxPrewarm);
    const videoId = pickBellVideoId();
    // 한 줄 자동 닫힘 안내: 설정(일정 알림 자동 닫힘)을 그대로 말로 옮긴다. "없음"이면 닫는 건 사용자뿐이라는 뜻으로.
    const closeMode = (state.settings && state.settings.KnoxAutoClose) || 'none';
    const closeMinutes = (state.settings && state.settings.KnoxAutoCloseMinutes) || 5;
    const endTime = fmt(end);
    const closeAt =
      closeMode === 'start' ? (when ? when + ' 일정 시작 시각에 자동으로 닫혀요' : '일정 시작 시각에 자동으로 닫혀요')
      : closeMode === 'after' ? `일정 시작 ${closeMinutes}분 뒤 자동으로 닫혀요`
      : closeMode === 'end' ? (endTime ? `${endTime} 일정 종료 시각에 자동으로 닫혀요` : (when ? '이 일정에는 종료 시각이 없어서 직접 닫아야 해요' : '일정 종료 시각에 자동으로 닫혀요'))
      : '자동으로 닫히지 않아요 — 직접 닫거나 Esc를 눌러 주세요';
    const video = videoId ? `<div class="knox-video-bg" aria-hidden="true"><iframe id="knox-bell" title="종소리 영상" src="https://www.youtube-nocookie.com/embed/${videoId}?autoplay=1&rel=0&playsinline=1&enablejsapi=1${prewarm ? '&mute=1' : ''}${loopParams('knox-bell', videoId)}" referrerpolicy="strict-origin-when-cross-origin" allow="autoplay; encrypted-media; fullscreen; picture-in-picture" allowfullscreen></iframe></div>` : '<div class="knox-video-bg" aria-hidden="true" style="background:#101412"></div>';
    return `<main class="knox-simple">${video}<button class="popup-close" type="button">닫기 × <span style="font-size:10px;opacity:.6">Esc</span></button><div class="knox-veil"><div class="knox-eyebrow">곧 일정이 시작돼요</div><h1>${escape(state.knoxTitle || '제목 없는 일정')}</h1>${when ? `<div class="knox-when">${when} 시작</div>` : ''}<div class="knox-note">Knox 캘린더에서 가져온 알림입니다</div><div class="knox-autoclose">${closeAt}</div></div>${volumeBadgeHtml()}</main>`;
  }
  // Called by the host right after a prewarm window is revealed: un-mute the already-buffered video
  // and rewind it so the bell rings from the top at the exact reminder moment.
  window.teumRevealBell = () => {
    const frame = document.getElementById('knox-bell');
    if (!frame || !frame.contentWindow) return;
    const command = (func, args) => frame.contentWindow.postMessage(JSON.stringify({ event: 'command', func, args }), '*');
    command('unMute', []);
    command('seekTo', [0, true]);
    command('playVideo', []);
  };
  function renderPopup() {
    // "knox:" ids are the dedicated calendar-alert page: never routed to a stretch/breath theme.
    if (String(state.themeId || '').indexOf('knox:') === 0) {
      root.innerHTML = knoxPopupFrame();
      $('.popup-close').addEventListener('click', () => { send('close'); });
      if (!state.knoxPrewarm) applyPopupVolume(); // prewarm 창은 숨겨져 있으니 건드리지 않는다
      return;
    }
    // "knox-bell" manages the videos the calendar-alert screen plays — not a rotating theme.
    if (state.themeId === 'knox-bell') {
      const bellTheme = { id: 'knox-bell', name: '일정 알림 영상', summary: '일정 알림 화면에 무작위(기본) 또는 순서대로 하나가 재생돼요', safety: '영상 소리가 크면 PC 볼륨을 먼저 확인하세요.', instructions: [] };
      root.innerHTML = popupFrame(bellTheme);
      $('.popup-close').addEventListener('click', () => { send('close'); });
      renderBellManager();
      return;
    }
    const theme = state.themes.find(t => t.id === state.themeId) || state.themes[0];
    root.innerHTML = popupFrame(theme);
    $('.popup-close').addEventListener('click', () => { send('close'); });
    const nextBtn = $('.popup-next');
    if (nextBtn) nextBtn.addEventListener('click', () => { send('next'); });
    if (theme.id === 'dance') renderDances();
    else renderWellness(theme);
  }
  // The bell-video manager: the same dropdown + "순서" + "반복" + "목록 편집" pattern as every other
  // card, keyed "knox-bell". The alert picks at random by default, or in list order if so chosen.
  function renderBellManager(forcedIdx) {
    const base = bellBaseList();
    const list = applyEdits('knox-bell', base);
    let current = 0;
    if (list.length) {
      current = Number.isInteger(forcedIdx) ? Math.min(forcedIdx, list.length - 1) : Math.min(Number(store.get('teum.variantIdx.knox-bell')) || 0, list.length - 1);
      store.set('teum.variantIdx.knox-bell', String(current));
    }
    const video = list[current];
    const itemsOf = source => source.map(v => ({ id: v.id, title: v.title }));
    const picker = variantPicker('knox-bell', itemsOf(list), current, true, true);
    $('#popup-stage').innerHTML = `${picker}<p class="small-help">일정 알림 화면이 뜰 때 이 목록에서 하나가 골라져 종소리와 함께 재생돼요. '순서'에서 무작위 또는 순서대로를 고를 수 있어요. 반복은 기본이 켜짐이고, '반복 안 함'으로 끄면 영상이 한 번만 울려요. '목록 편집'으로 영상을 넣거나 뺄 수 있어요.</p>${video ? `<div class="video-stage"><iframe id="variant-frame" class="short-player" title="${escape(video.title)}" src="https://www.youtube-nocookie.com/embed/${video.youtubeId}?autoplay=1&rel=0&playsinline=1&enablejsapi=1${loopParams('knox-bell', video.youtubeId)}" referrerpolicy="strict-origin-when-cross-origin" allow="autoplay; encrypted-media; fullscreen; picture-in-picture" allowfullscreen></iframe><p class="video-credit">${escape(video.title)} · 재생기로만 보여 줘요</p></div>` : '<p class="small-help">재생할 영상이 없어요. 위 \'목록 편집\'에서 영상을 추가하세요.</p>'}`;
    bindPicker('knox-bell', itemsOf(list), itemsOf(base), renderBellManager);
    applyPopupVolume();
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
  // Per-card "반복": when on, the card's video replays from the start when it ends; lives in settings.json (LoopModes).
  // 릴렉스 음향과 일정 알림 영상(knox-bell)은 기본이 반복 — 소리가 짧게 끊기지 않도록 / 종소리가 알림 역할을 하도록.
  // 다른 카드는 기본 한 번 재생이고, 사용자가 고르면 그 선택이 우선한다.
  function loopOn(id) {
    const raw = ((state && state.settings && state.settings.LoopModes) || {})[id];
    if (raw === 'on') return true;
    if (raw === 'off') return false;
    return id === 'relax' || id === 'knox-bell';
  }
  function setLoopMode(id, on) {
    const value = on === 'on' ? 'on' : 'off';
    state.settings = { ...state.settings, LoopModes: { ...((state.settings && state.settings.LoopModes) || {}), [id]: value } };
    send('save-loop-mode', { themeId: id, on: value });
  }
  // YouTube only loops an embed when loop=1 is paired with playlist=<same video id>.
  const loopParams = (cardId, videoId) => (loopOn(cardId) && videoId ? `&loop=1&playlist=${videoId}` : '');
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
  // ---- Sub-item list editing: each card's list can gain user-added YouTube videos and lose built-ins.
  // Stored in localStorage (the WebView2 profile) as { removed: {id:true}, added: [{id,youtubeId,title,artist,url}] }.
  const editsKey = themeId => 'teum.listEdits.' + themeId;
  function readEdits(themeId) {
    try {
      const raw = JSON.parse(store.get(editsKey(themeId)) || '{}');
      return { removed: raw.removed || {}, added: (raw.added || []).filter(a => /^[A-Za-z0-9_-]{11}$/.test(a.youtubeId || '')) };
    } catch (_) { return { removed: {}, added: [] }; }
  }
  const writeEdits = (themeId, edits) => store.set(editsKey(themeId), JSON.stringify(edits));
  // Pulls an 11-character video ID out of a pasted YouTube link, Shorts link, or the bare ID itself.
  function extractVideoId(text) {
    const raw = String(text || '').trim();
    const m = raw.match(/[?&]v=([A-Za-z0-9_-]{11})/) || raw.match(/youtu\.be\/([A-Za-z0-9_-]{11})/) || raw.match(/\/shorts\/([A-Za-z0-9_-]{11})/) || raw.match(/^([A-Za-z0-9_-]{11})$/);
    return m ? m[1] : null;
  }
  // The effective list: built-ins minus removals, plus user additions (duplicates by video ID are dropped).
  function applyEdits(themeId, base) {
    const edits = readEdits(themeId);
    const kept = base.filter(item => !item.id || !edits.removed[item.id]);
    const seen = new Set(kept.map(item => item.youtubeId).filter(Boolean));
    const extra = edits.added.filter(a => !seen.has(a.youtubeId)).map(a => ({ id: a.id, type: 'video', title: a.title, author: a.artist || '', artist: a.artist || '', youtubeId: a.youtubeId, url: a.url }));
    return kept.concat(extra);
  }
  // The picker is one dropdown line (the old button row pushed the video far down) plus an edit panel.
  // withMode=false hides the order selector (the bell manager always picks at random); withLoop=false
  // hides the loop selector too. The bell manager keeps the loop toggle — the user may silence the
  // repeating bell on purpose ("반복 끄기").
  function variantPicker(id, items, current, withMode = true, withLoop = true) {
    const options = items.map((v, i) => `<option value="${i}" ${i === current ? 'selected' : ''}>#${i + 1} ${escape(v.title)}</option>`).join('');
    const modeSelect = withMode ? `<label class="variant-mode-label">순서 <select id="variant-mode"><option value="random" ${variantMode(id) === 'random' ? 'selected' : ''}>무작위</option><option value="sequential" ${variantMode(id) === 'sequential' ? 'selected' : ''}>순서대로</option></select></label>` : '';
    const loopSelect = withLoop ? `<label class="variant-mode-label">반복 <select id="variant-loop" aria-label="영상 반복"><option value="off" ${loopOn(id) ? '' : 'selected'}>안 함</option><option value="on" ${loopOn(id) ? 'selected' : ''}>끝나면 다시</option></select></label>` : '';
    return `<div class="stretch-picker"><div class="stretch-pick-row"><b id="variant-count">${items.length ? current + 1 : 0} / ${items.length}</b><select id="variant-pick" aria-label="영상 골라 보기">${options}</select>${modeSelect}${loopSelect}<button id="variant-edit" class="quiet" type="button">목록 편집</button></div></div>`;
  }
  // Keeps the edit panel open across the re-render that an add/remove triggers.
  let keepEditOpen = false;
  // Fullscreen popups swallow copy & paste (borderless, always-on-top); while the editor is open the
  // host drops the popup into a normal windowed mode and restores fullscreen when it closes.
  const setPopupEditMode = on => { if (host) send('popup-edit-mode', { on: String(on) }); };
  function bindPicker(id, items, baseItems, rerender) {
    const pick = $('#variant-pick');
    if (pick) pick.addEventListener('change', event => rerender(Number(event.target.value)));
    if ($('#variant-mode')) $('#variant-mode').addEventListener('change', event => setVariantMode(id, event.target.value));
    const loopSel = $('#variant-loop');
    if (loopSel) loopSel.addEventListener('change', event => {
      setLoopMode(id, event.target.value);
      keepEditOpen = Boolean(document.querySelector('#variant-edit-box')); // an open editor survives the re-render
      rerender(Number(store.get('teum.variantIdx.' + id)) || 0); // rebuild the iframe with the loop parameter
    });
    const editBtn = $('#variant-edit');
    // The edit panel exists in the DOM only while it is open — nothing extra is shown otherwise.
    const openEdit = () => { appendEditBox(id, items, baseItems, rerender); setPopupEditMode(true); };
    const closeEdit = () => { const box = $('#variant-edit-box'); if (box) box.remove(); setPopupEditMode(false); };
    if (editBtn) editBtn.addEventListener('click', () => {
      if ($('#variant-edit-box')) closeEdit();
      else openEdit();
    });
    if (keepEditOpen) { keepEditOpen = false; openEdit(); }
  }
  function appendEditBox(id, items, baseItems, rerender) {
    const picker = document.querySelector('.stretch-picker');
    if (!picker) return;
    const box = document.createElement('div');
    box.id = 'variant-edit-box';
    box.className = 'variant-edit-box';
    picker.appendChild(box);
    renderEditBox(id, items, baseItems, rerender);
  }
  function renderEditBox(id, items, baseItems, rerender) {
    const box = $('#variant-edit-box');
    if (!box) return;
    const edits = readEdits(id);
    const rows = items.map((v, i) => `<div class="edit-row"><span title="${escape(v.title)}">${i + 1}. ${escape(v.title)}</span><button type="button" class="quiet edit-remove" data-remove-id="${escape(v.id || '')}" ${v.id ? '' : 'disabled'}>삭제</button></div>`).join('');
    const removedRows = baseItems.filter(v => v.id && edits.removed[v.id]).map(v => `<div class="edit-row edit-removed"><span>${escape(v.title)}</span><button type="button" class="quiet edit-restore" data-restore-id="${escape(v.id)}">되돌리기</button></div>`).join('');
    box.innerHTML = `<p class="small-help" style="margin:0">'삭제'로 항목을 제거하고, 아래에 YouTube 링크를 붙여 넣어 추가할 수 있어요. 이 카드에 기억돼요.</p>${rows ? `<div class="edit-list">${rows}</div>` : '<p class="small-help" style="margin:0">목록이 비어 있어요. 아래에서 영상을 추가하세요.</p>'}${removedRows ? `<p class="small-help" style="margin:0">삭제한 항목</p><div class="edit-list">${removedRows}</div>` : ''}<div class="edit-add"><input id="edit-url" type="text" placeholder="YouTube 링크 또는 영상 ID 붙여 넣기"><input id="edit-title" type="text" placeholder="표시할 제목"><button id="edit-add-btn" class="secondary" type="button">추가</button></div><div class="edit-actions"><button id="edit-reset" class="quiet" type="button">기본 목록으로 되돌리기</button><button id="edit-close" class="quiet" type="button">편집 닫기</button></div>`;
    box.querySelectorAll('.edit-remove').forEach(button => button.addEventListener('click', () => {
      const itemId = button.dataset.removeId;
      const editsNow = readEdits(id);
      if (editsNow.added.some(a => a.id === itemId)) editsNow.added = editsNow.added.filter(a => a.id !== itemId);
      else editsNow.removed[itemId] = true;
      writeEdits(id, editsNow);
      keepEditOpen = true;
      rerender(Math.max(0, Number($('#variant-pick').value) || 0));
    }));
    box.querySelectorAll('.edit-restore').forEach(button => button.addEventListener('click', () => {
      const editsNow = readEdits(id);
      delete editsNow.removed[button.dataset.restoreId];
      writeEdits(id, editsNow);
      keepEditOpen = true;
      rerender(0);
    }));
    const addBtn = box.querySelector('#edit-add-btn');
    if (addBtn) addBtn.addEventListener('click', () => {
      const videoId = extractVideoId($('#edit-url').value);
      if (!videoId) { toast('YouTube 링크나 11자 영상 ID를 넣어 주세요.'); return; }
      const title = ($('#edit-title').value || '').trim() || '내가 추가한 영상';
      const editsNow = readEdits(id);
      const all = applyEdits(id, baseItems);
      if (all.some(v => v.youtubeId === videoId)) { toast('이미 목록에 있는 영상이에요.'); return; }
      editsNow.added.push({ id: 'custom-' + videoId, youtubeId: videoId, title, artist: '', url: 'https://www.youtube.com/watch?v=' + videoId });
      writeEdits(id, editsNow);
      keepEditOpen = true;
      rerender(all.length); // the new item lands at the end
    });
    const resetBtn = box.querySelector('#edit-reset');
    if (resetBtn) resetBtn.addEventListener('click', () => { store.set(editsKey(id), '{}'); keepEditOpen = true; rerender(0); });
    const closeBtn = box.querySelector('#edit-close');
    if (closeBtn) closeBtn.addEventListener('click', () => { box.remove(); setPopupEditMode(false); });
  }
  function renderWellness(theme, forcedIdx) {
    const base = variantList(theme);
    const list = applyEdits(theme.id, base);
    let current = 0, video = null;
    if (list.length >= 1) {
      current = Number.isInteger(forcedIdx) ? Math.min(forcedIdx, list.length - 1) : pickVariant(theme.id, list.length);
      if (Number.isInteger(forcedIdx)) store.set('teum.variantIdx.' + theme.id, String(current));
      if (list[current].type === 'video') video = list[current];
    }
    const itemsOf = source => source.map(v => ({ id: v.id, title: v.title }));
    // The picker is always shown, so the list can be edited even when it has been emptied.
    const picker = variantPicker(theme.id, itemsOf(list), current);
    const videoStage = video ? `<div class="video-stage"><iframe id="variant-frame" class="${video.wide ? 'wide-player' : 'short-player'}" title="${escape(video.title)}" src="https://www.youtube-nocookie.com/embed/${video.youtubeId}?autoplay=1&rel=0&playsinline=1&enablejsapi=1${loopParams(theme.id, video.youtubeId)}" referrerpolicy="strict-origin-when-cross-origin" allow="autoplay; encrypted-media; fullscreen; picture-in-picture" allowfullscreen></iframe><p class="video-credit">${escape(video.title)}${video.author ? ' · ' + escape(video.author) : ''} · 재생기로만 보여 줘요 <button id="variant-external" class="quiet" type="button">YouTube에서 열기 ↗</button></p><p class="video-safety">${escape(theme.safety)}</p></div>` : '';
    const visual = theme.file ? `<img class="motion-image" src="${escape(theme.file)}" alt="${escape(theme.name)} 따라 하기 움직이는 그림">` : `<div class="text-stage"><div class="text-symbol" aria-hidden="true">${symbolFor(theme)}</div></div>`;
    $('#popup-stage').innerHTML = video ? `${picker}${videoStage}` : `${picker}<div class="wellness-layout"><div class="motion-stage">${visual}</div><aside class="instruction-panel"><div class="eyebrow">천천히 따라 해 보세요</div><ol>${(theme.instructions || []).map(line => `<li>${escape(line)}</li>`).join('')}</ol><div class="safety">${escape(theme.safety)}</div></aside></div>`;
    bindPicker(theme.id, itemsOf(list), itemsOf(base), idx => renderWellness(theme, idx));
    if ($('#variant-external')) $('#variant-external').addEventListener('click', () => send('open-link', { url: video.url }));
    applyPopupVolume(); // 하위 항목을 바꾸면 임베드가 새로 만들어지므로 저장된 볼륨을 다시 붙인다
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
    const command = (func, args = []) => { window.danceCommandLog.push({ func, args }); if (func === 'seekTo') state.seekPending = true; if (frame.contentWindow) frame.contentWindow.postMessage(JSON.stringify({ event: 'command', func, args }), origin); };
    const label = () => { $('#practice-loop').textContent = state.a == null ? '반복 꺼짐' : state.b == null ? `A ${fmt(state.a)} 지정됨 · B를 정하세요` : `${fmt(state.a)} ~ ${fmt(state.b)} 반복 중`; };
    const startListening = () => { if (frame.contentWindow) frame.contentWindow.postMessage(JSON.stringify({ event: 'listening', id: 1, channel: 'widget' }), origin); };
    const onMessage = event => {
      if (event.origin !== origin || event.source !== frame.contentWindow) return;
      let data; try { data = typeof event.data === 'string' ? JSON.parse(event.data) : event.data; } catch (e) { return; }
      const info = data && (data.event === 'infoDelivery' ? data.info : data.event === 'initialDelivery' ? data.info : null);
      if (!info || typeof info.currentTime !== 'number') return;
      // "반복 안 함"을 실제로 보장한다: Shorts(세로 영상)는 loop 파라미터 없이도 끝나면 저절로 처음부터
      // 다시 시작한다. 크게 뒤로 점프한 시간 = 플레이어가 스스로 재시작했다는 뜻이므로, A-B 반복이 꺼져 있고
      // 그 점프가 우리가 방금 한 seek의 결과도 아니라면 재생을 정지시켜 한 번만 재생되게 한다.
      // (seekTo 다음의 점프는 seekPending 플래그로 한 번 소비한다.)
      const jumped = Math.abs(info.currentTime - state.time) > 2;
      if (jumped && state.seekPending) state.seekPending = false;
      else if (!loopOn('dance') && state.a == null && info.currentTime + 2 < state.time) command('pauseVideo');
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
    const base = (content.dances || []).filter(d => /^[a-zA-Z0-9_-]{11}$/.test(d.youtubeId || ''));
    const dances = applyEdits('dance', base);
    const itemsOf = source => source.map(d => ({ id: d.id, title: d.artist ? `${d.artist} · ${d.title}` : d.title }));
    let current = 0;
    if (dances.length) {
      current = Number.isInteger(forcedIdx) ? Math.min(forcedIdx, dances.length - 1) : pickVariant('dance', dances.length);
      if (Number.isInteger(forcedIdx)) store.set('teum.variantIdx.dance', String(current));
    }
    const dance = dances[current];
    const picker = variantPicker('dance', itemsOf(dances), current);
    // The stage stays usable even with an empty list, so the picker's edit panel can add videos back.
    $('#popup-stage').innerHTML = `${picker}<div class="dance-note">서서 하는 원본 안무입니다. 책상·의자와 거리를 두고 공간을 확보하세요. 점프나 깊은 굽힘이 부담되면 보기만 하거나 다른 주제로 바꾸세요. 허리 통증이 있으면 저강도 자세 전환을 먼저 고르세요.</div>${dance ? `<iframe id="dance-frame" class="${dance.vertical ? 'short-player' : 'video-player'}" title="${escape(dance.title)}" src="https://www.youtube-nocookie.com/embed/${dance.youtubeId}?autoplay=1&rel=0&playsinline=1&enablejsapi=1${loopParams('dance', dance.youtubeId)}" referrerpolicy="strict-origin-when-cross-origin" allow="autoplay; encrypted-media; fullscreen; picture-in-picture" allowfullscreen></iframe>${practiceControls()}<p class="small-help">${escape(dance.artist || '')}${dance.artist ? ' · ' : ''}${escape(dance.title)}${dance.artist ? ' (공식 채널)' : ''}. 영상이 임베드를 허용하지 않으면 아래 공식 링크로 여세요. 원본을 내려받거나 재편집하지 않습니다. 속도·구간 반복은 영상 재생기를 원격 조작하는 것이며, 반응이 없으면 영상 안의 ⚙ 메뉴에서 직접 바꿀 수 있습니다.</p><button id="video-external" class="secondary" type="button">공식 영상 브라우저에서 열기 ↗</button>` : '<p class="small-help">재생할 영상이 없어요. 위 \'목록 편집\'에서 영상을 추가하세요.</p>'}`;
    stopDance = dance ? bindPractice() : null;
    if ($('#video-external')) $('#video-external').addEventListener('click', () => send('open-link', { url: dance.url }));
    bindPicker('dance', itemsOf(dances), itemsOf(base), idx => renderDances(idx));
    applyPopupVolume();
  }
  function receive(message) {
    if (message.type === 'tick') { updateStatus(message); return; }
    if (message.type === 'error') { toast(message.message); return; }
    if (message.type === 'cards-import') { applyCardsImport(message.json); return; }
    if (message.type === 'themes') {
      if (!state || state.mode === 'popup') return;
      state.themes = message.themes; state.settings = { ...state.settings, ThemeIds: message.settings.ThemeIds, ThemeMode: message.settings.ThemeMode, Schedules: message.settings.Schedules || {} }; state.sync = message.sync; state.knoxSync = message.knoxSync;
      applyAppearance();
      refreshThemeGrid(); updateKnoxSync();
      if (message.notice && message.notice !== state.notice) { state.notice = message.notice; toast(message.notice); }
      return;
    }
    if (message.type !== 'state') return;
    state = message;
    if (state.testing && !window.__teumCards) window.__teumCards = { build: buildCardsExport, apply: applyCardsImport }; // UI 테스트 전용 후크(테스트 실행에서만 노출)
    applyAppearance();
    if (state.mode === 'popup') renderPopup(); else { renderSettings(); updateStatus(state); }
    if (state.notice && state.mode !== 'popup') toast(state.notice);
  }
  // Esc: 팝업 닫기. ↑/↓: 팝업 볼륨 조절(영상이 있는 화면에서만, 5씩). 목록 편집 입력칸에
  // 커서가 있을 때는 입력을 방해하지 않는다.
  document.addEventListener('keydown', event => {
    if (!state || state.mode !== 'popup') return;
    if (event.key === 'Escape') { send('close'); return; }
    if (event.key !== 'ArrowUp' && event.key !== 'ArrowDown') return;
    const tag = (event.target && event.target.tagName) || '';
    if (tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA') return;
    const frames = videoFrames();
    if (!frames.length) return; // 소리 낼 영상이 없는 화면 — 화살표는 원래 동작(스크롤)대로 둔다
    event.preventDefault();
    const vol = Math.min(100, Math.max(0, popupVolume() + (event.key === 'ArrowUp' ? 5 : -5)));
    store.set('teum.volume', String(vol));
    frames.forEach(frame => { ytListen(frame); ytVolumeCommand(frame, 'setVolume', [vol]); });
    updateVolumeBadge(true);
  });
  window.addEventListener('resize', () => { if (state && state.mode === 'popup') updateVolumeBadge(); });
  if (host) { host.addEventListener('message', event => receive(event.data)); send('ready'); }
  else {
    let stored = {};
    try { stored = JSON.parse(localStorage.getItem('teum-preview-settings') || '{}'); } catch (_) { /* Keep safe defaults. */ }
    const popup = location.hash.startsWith('#popup');
    receive({ type: 'state', mode: popup ? 'popup' : 'settings', themeId: decodeURIComponent(location.hash.split('=')[1] || 'stretch'), themes: [...(window.THEME_DATA || []), ...extraThemes()], settings: { ...defaults, ...stored }, sync: { offline: true }, knoxSync: { offline: true }, next: '', notice: '' });
  }
})();
