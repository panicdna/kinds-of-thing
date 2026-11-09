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
  return { normalizeAnswer, compareAnswer, chooseQuestion, breathPhase };
});
