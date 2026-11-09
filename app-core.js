(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.TeumCore = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';
  function normalizeAnswer(value) {
    return String(value == null ? '' : value).normalize('NFC').replace(/\s+/gu, '');
  }
  function compareAnswer(answer, expected) {
    return normalizeAnswer(answer) === normalizeAnswer(expected);
  }
  function chooseQuestion(poem, random = Math.random) {
    const lines = (poem.lines || []).slice();
    let eligible = lines.map((line, index) => ({ line, index })).filter(item => normalizeAnswer(item.line).length >= 7);
    if (!eligible.length) eligible = lines.map((line, index) => ({ line, index })).filter(item => normalizeAnswer(item.line).length);
    if (!eligible.length) throw new Error('비울 수 있는 시의 한 줄이 없습니다.');
    const selected = eligible[Math.min(eligible.length - 1, Math.floor(Math.max(0, random()) * eligible.length))];
    return { poemId: poem.id, lines, blankIndex: selected.index, expected: selected.line };
  }
  function breathPhase(elapsedMs, timing) {
    const inhale = Number(timing.InhaleSeconds), hold = Number(timing.HoldSeconds), exhale = Number(timing.ExhaleSeconds);
    if (!Number.isFinite(inhale) || inhale < 1 || inhale > 10 || !Number.isFinite(hold) || hold < 0 || hold > 30 || !Number.isFinite(exhale) || exhale < 1 || exhale > 30) {
      throw new Error('들숨 1~10초, 멈춤 0~30초, 날숨 1~30초 안에서 선택하세요.');
    }
    let position = Math.max(0, elapsedMs / 1000) % (inhale + hold + exhale);
    for (const [phase, seconds] of [['inhale', inhale], ['hold', hold], ['exhale', exhale]]) {
      if (seconds && position < seconds) return { phase, remaining: Math.ceil(seconds - position), progress: position / seconds };
      position -= seconds;
    }
    return { phase: 'inhale', remaining: inhale, progress: 0 };
  }
  // Community sharing. Limits MUST match CommunityContent in ReminderCore.cs.
  const LIMITS = { name: 30, summary: 60, step: 60, steps: 6, safety: 150, author: 20 };
  const SHARE_REPO = { owner: 'panicdna', repo: 'kinds-of-thing', branch: 'main', dir: 'community' };
  const MAX_SHARE_URL = 7000;
  function cleanText(value, max, label, required, errors) {
    const text = String(value == null ? '' : value).normalize('NFC').trim();
    if (!text) { if (required) errors.push(`${label}을(를) 입력하세요.`); return ''; }
    if (text.length > max) errors.push(`${label}은(는) ${max}자 이하로 줄여 주세요. (지금 ${text.length}자)`);
    else if (/[\u0000-\u001f\u007f-\u009f<>]/u.test(text)) errors.push(`${label}에는 < > 기호나 제어문자를 쓸 수 없어요.`);
    else if (/:\/\/|www\./i.test(text)) errors.push(`${label}에는 링크를 넣을 수 없어요.`);
    return text;
  }
  function makeShareId(random = Math.random, now = Date.now) {
    return 'u-' + now().toString(36) + Math.floor(random() * 1296).toString(36).padStart(2, '0');
  }
  // form: { name, summary, steps (string, one per line), safety, author }
  function validateShare(form, random, now) {
    const errors = [];
    const item = {
      schema: 1, id: makeShareId(random, now),
      name: cleanText(form.name, LIMITS.name, '이름', true, errors),
      summary: cleanText(form.summary, LIMITS.summary, '한 줄 설명', true, errors),
      instructions: String(form.steps || '').split(/\r?\n/).map(x => x.trim()).filter(Boolean).map(x => cleanText(x, LIMITS.step, '동작 안내', true, errors)),
      safety: cleanText(form.safety, LIMITS.safety, '주의사항', true, errors),
      author: cleanText(form.author, LIMITS.author, '표시 이름', false, errors)
    };
    if (!item.instructions.length) errors.push('동작 안내를 한 줄 이상 적어 주세요.');
    if (item.instructions.length > LIMITS.steps) errors.push(`동작 안내는 ${LIMITS.steps}줄 이하로 적어 주세요.`);
    if (!item.author) delete item.author;
    return { ok: errors.length === 0, errors, item };
  }
  function shareJson(item) { return JSON.stringify(item, null, 2) + '\n'; }
  // Opens GitHub's "create new file" page pre-filled; GitHub itself handles login and the pull request.
  function buildShareUrl(item) {
    const r = SHARE_REPO;
    const url = `https://github.com/${r.owner}/${r.repo}/new/${r.branch}/${r.dir}?filename=${encodeURIComponent(item.id + '.json')}&value=${encodeURIComponent(shareJson(item))}`;
    if (url.length > MAX_SHARE_URL) throw new Error('내용이 너무 길어 GitHub 주소에 담을 수 없어요. 조금 줄여 주세요.');
    return url;
  }
  return { normalizeAnswer, compareAnswer, chooseQuestion, breathPhase, LIMITS, validateShare, shareJson, buildShareUrl };
});
