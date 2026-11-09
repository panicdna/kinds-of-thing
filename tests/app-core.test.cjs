const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const modulePath = path.join(__dirname, '..', 'app-core.js');
assert.ok(fs.existsSync(modulePath), 'app-core.js is missing');
const core = require(modulePath);

// ---- Community sharing ----
const form = { name: '목 풀기', summary: '목을 살짝 풀어요', steps: '고개를 천천히 숙여요\n\n반대쪽으로 기울여요  ', safety: '아프면 중단하세요.', author: '' };
const ok = core.validateShare(form, () => 0, () => 0);
assert.equal(ok.ok, true, ok.errors.join(','));
assert.equal(ok.item.id, 'u-000');
assert.deepEqual(ok.item.instructions, ['고개를 천천히 숙여요', '반대쪽으로 기울여요']);
assert.equal('author' in ok.item, false, 'blank author is omitted');
assert.match(core.validateShare(form).item.id, /^[a-z0-9][a-z0-9-]{2,39}$/, 'id satisfies the C# pattern');
for (const [patch, text] of [
  [{ name: '' }, '이름'], [{ summary: 'x'.repeat(61) }, '60자'], [{ safety: '' }, '주의사항'],
  [{ name: '<b>x</b>' }, '< >'], [{ summary: '보세요 https://x.example' }, '링크'], [{ summary: 'www.x.example' }, '링크'],
  [{ steps: '' }, '한 줄 이상'], [{ steps: '1\n2\n3\n4\n5\n6\n7' }, '6줄'], [{ author: 'y'.repeat(21) }, '20자']
]) {
  const bad = core.validateShare({ ...form, ...patch });
  assert.equal(bad.ok, false, `should reject ${JSON.stringify(patch)}`);
  assert.ok(bad.errors.join(' ').includes(text), `${text} not in ${bad.errors}`);
}
const url = new URL(core.buildShareUrl(ok.item));
assert.equal(url.origin + url.pathname, 'https://github.com/panicdna/kinds-of-thing/new/main/community');
assert.equal(url.searchParams.get('filename'), 'u-000.json');
assert.deepEqual(JSON.parse(url.searchParams.get('value')), ok.item, 'GitHub prefill round-trips to the exact item');
const longest = core.validateShare({ name: '가'.repeat(30), summary: '나'.repeat(60), steps: Array(6).fill('다'.repeat(60)).join('\n'), safety: '라'.repeat(150), author: '마'.repeat(20) });
assert.equal(longest.ok, true);
assert.doesNotThrow(() => core.buildShareUrl(longest.item), 'a maximum-size Korean item still fits in a GitHub URL');
console.log('PASS: community share form validates like the C# parser and builds a GitHub new-file URL');

// The seed item shipped in community/ must satisfy the same rules the app enforces.
const sampleDir = path.join(__dirname, '..', 'community');
for (const name of fs.readdirSync(sampleDir).filter(n => n.endsWith('.json') && n !== 'index.json')) {
  const item = JSON.parse(fs.readFileSync(path.join(sampleDir, name), 'utf8'));
  assert.equal(item.schema, 1); assert.equal(name, item.id + '.json');
  const check = core.validateShare({ name: item.name, summary: item.summary, steps: item.instructions.join('\n'), safety: item.safety, author: item.author });
  assert.equal(check.ok, true, `${name}: ${check.errors}`);
}
console.log('PASS: every community/*.json file is valid');

// index.json is what the app syncs from; it must list exactly the shared items, with git's own blob shas.
const { execFileSync } = require('node:child_process');
const index = JSON.parse(fs.readFileSync(path.join(sampleDir, 'index.json'), 'utf8'));
const shared = fs.readdirSync(sampleDir).filter(n => n.endsWith('.json') && n !== 'index.json').sort();
assert.deepEqual(index.items.map(i => i.name), shared, 'community/index.json is stale: run python community/build_index.py');
for (const item of index.items) {
  const sha = execFileSync('git', ['hash-object', path.join(sampleDir, item.name)], { encoding: 'utf8' }).trim();
  assert.equal(item.sha, sha, `${item.name}: index sha differs from the file; run python community/build_index.py`);
}
console.log('PASS: community/index.json matches the shared files and their git shas');

// ---- YouTube videos in a shared item ----
for (const [input, id] of [
  ['https://www.youtube.com/watch?v=Md24PbVP72U', 'Md24PbVP72U'],
  ['https://www.youtube.com/shorts/-yOiTWEgU_Y', '-yOiTWEgU_Y'],
  ['https://youtu.be/x6y6SPzCQXQ?t=3', 'x6y6SPzCQXQ'],
  ['https://m.youtube.com/watch?v=x6y6SPzCQXQ&list=abc', 'x6y6SPzCQXQ'],
  ['x6y6SPzCQXQ', 'x6y6SPzCQXQ']
]) assert.equal(core.extractVideoId(input), id, input);
for (const bad of ['http://www.youtube.com/watch?v=x6y6SPzCQXQ', 'https://www.youtube.com.evil.invalid/watch?v=x6y6SPzCQXQ',
  'https://user@www.youtube.com/watch?v=x6y6SPzCQXQ', 'https://www.youtube.com:8443/watch?v=x6y6SPzCQXQ', 'https://vimeo.com/12345678901',
  'https://www.youtube.com/watch?v=short', 'https://www.youtube.com/', '', 'javascript:alert(1)']) assert.equal(core.extractVideoId(bad), null, bad);
const withVideos = core.validateShare({ ...form, videos: 'https://www.youtube.com/shorts/-yOiTWEgU_Y | 눈 쉬기 영상\nhttps://youtu.be/x6y6SPzCQXQ' }, () => 0, () => 0);
assert.equal(withVideos.ok, true, withVideos.errors.join(','));
assert.deepEqual(withVideos.item.videos, [{ id: '-yOiTWEgU_Y', title: '눈 쉬기 영상' }, { id: 'x6y6SPzCQXQ', title: '영상 2' }]);
assert.ok(!/:\/\//.test(JSON.stringify(withVideos.item)), 'the shared file stores ids, never links');
const videoOnly = core.validateShare({ ...form, steps: '', videos: 'x6y6SPzCQXQ' }, () => 0, () => 0);
assert.equal(videoOnly.ok, true, videoOnly.errors.join(','));
assert.deepEqual(videoOnly.item.instructions, []);
for (const bad of [
  { ...form, videos: 'https://vimeo.com/12345678901' }, { ...form, videos: 'x6y6SPzCQXQ\nx6y6SPzCQXQ' },
  { ...form, videos: 'x6y6SPzCQXQ | 보세요 http://x.example' }, { ...form, steps: '', videos: '' },
  { ...form, videos: ['AAAAAAAAAAA', 'BBBBBBBBBBB', 'CCCCCCCCCCC', 'DDDDDDDDDDD', 'EEEEEEEEEEE', 'FFFFFFFFFFF'].join('\n') }
]) assert.equal(core.validateShare(bad).ok, false, JSON.stringify(bad.videos));
console.log('PASS: shared items may carry YouTube video ids (and only YouTube), stored without links');

// ---- Tile icon of a shared item ----
assert.equal(core.ICONS[0], '✿', 'the first icon is the default');
assert.equal(core.validateShare({ ...form, icon: '✿' }).item.icon, undefined, 'the default icon is not written');
assert.equal(core.validateShare({ ...form, icon: '♪' }).item.icon, '♪');
for (const bad of ['😀', 'x', '✿✿', '<b>']) assert.equal(core.validateShare({ ...form, icon: bad }).ok, false, bad);
console.log('PASS: a shared item may choose its tile icon from the offered symbols');

// ---- Deleting a shared item = GitHub's delete page for its file ----
assert.equal(core.buildDeleteUrl('c-u-test-video'), 'https://github.com/panicdna/kinds-of-thing/delete/main/community/u-test-video.json');
for (const bad of ['c-index', 'stretch', 'c-', 'c-ab', 'c-../README', 'c-U-Upper', 'c-a/b-cd', '', null, undefined, 'c-' + 'a'.repeat(41)]) assert.equal(core.buildDeleteUrl(bad), null, String(bad));
console.log('PASS: a shared item maps to exactly its own community file on the GitHub delete page');
