(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.TeumCore = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';
  // Community sharing. Limits MUST match CommunityContent in ReminderCore.cs.
  const LIMITS = { name: 30, summary: 60, step: 60, steps: 6, safety: 150, author: 20, videos: 5, videoTitle: 60 };
  const VIDEO_ID = /^[A-Za-z0-9_-]{11}$/;
  // Tile symbols a shared item may choose (the first is the default). MUST match CommunityContent.Icons in ReminderCore.cs.
  const ICONS = ['✿','❀','☾','☀','★','♥','♪','♫','◎','◌','↟','↗','≈','♧','☕','✦'];
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
  // Accepts a YouTube address (watch?v=, /shorts/, /embed/, youtu.be/) or a bare 11-character id; anything else is null.
  function extractVideoId(text) {
    const raw = String(text == null ? '' : text).trim();
    if (VIDEO_ID.test(raw)) return raw;
    let url;
    try { url = new URL(raw); } catch (_) { return null; }
    if (url.protocol !== 'https:' || url.username || url.password || url.port) return null;
    const host = url.hostname.toLowerCase();
    const parts = url.pathname.split('/').filter(Boolean);
    let id = null;
    if (host === 'youtu.be') id = parts[0];
    else if (host === 'youtube.com' || host === 'www.youtube.com' || host === 'm.youtube.com') {
      if (parts[0] === 'watch') id = url.searchParams.get('v');
      else if (['shorts', 'embed', 'live'].includes(parts[0])) id = parts[1];
    }
    return id && VIDEO_ID.test(id) ? id : null;
  }
  function makeShareId(random = Math.random, now = Date.now) {
    return 'u-' + now().toString(36) + Math.floor(random() * 1296).toString(36).padStart(2, '0');
  }
  // form: { name, summary, steps (string, one per line), videos (optional, one per line: address [| title]), safety, author }
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
    const videos = [];
    const seen = new Set();
    String(form.videos || '').split(/\r?\n/).map(x => x.trim()).filter(Boolean).forEach(line => {
      const bar = line.indexOf('|');
      const address = (bar < 0 ? line : line.slice(0, bar)).trim();
      const id = extractVideoId(address);
      if (!id) { errors.push(`유튜브 영상 주소를 확인하세요: ${address.slice(0, 40)}`); return; }
      if (seen.has(id)) { errors.push('같은 영상이 두 번 들어 있어요.'); return; }
      seen.add(id);
      const title = cleanText(bar < 0 ? '' : line.slice(bar + 1), LIMITS.videoTitle, '영상 제목', false, errors) || `영상 ${videos.length + 1}`;
      videos.push({ id, title });
    });
    if (videos.length > LIMITS.videos) errors.push(`영상은 ${LIMITS.videos}개 이하로 넣어 주세요.`);
    if (videos.length) item.videos = videos;
    if (form.icon && form.icon !== ICONS[0]) {
      if (ICONS.includes(form.icon)) item.icon = form.icon;
      else errors.push('아이콘은 목록에서 골라 주세요.');
    }
    if (!item.instructions.length && !videos.length) errors.push('동작 안내를 한 줄 이상 적거나 유튜브 영상을 넣어 주세요.');
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
  // GitHub's "delete this file" page for a shared item's file (theme ids of shared items are "c-" + file id).
  // GitHub itself handles the login and turns the deletion into a pull request; the app never holds a token.
  function buildDeleteUrl(themeId) {
    const match = /^c-([a-z0-9][a-z0-9-]{2,39})$/.exec(String(themeId || ''));
    if (!match || match[1] === 'index') return null; // community/index.json is the list the app syncs from; never offer to delete it
    const r = SHARE_REPO;
    return `https://github.com/${r.owner}/${r.repo}/delete/${r.branch}/${r.dir}/${match[1]}.json`;
  }
  return { LIMITS, ICONS, validateShare, shareJson, buildShareUrl, extractVideoId, buildDeleteUrl };
});
