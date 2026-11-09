const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const modulePath = path.join(__dirname, '..', 'app-core.js');
assert.ok(fs.existsSync(modulePath), 'Poem-answer comparison has not been implemented');
const core = require(modulePath);
assert.equal(core.normalizeAnswer('  말없이   고이 보내 드리우리다.  '), '말없이고이보내드리우리다.');
assert.equal(core.compareAnswer(' 말없이 고이 보내 드리우리다. ', '말없이 고이 보내 드리우리다.'), true);
assert.equal(core.compareAnswer('말없이 보내 드리우리다.', '말없이 고이 보내 드리우리다.'), false);
console.log('PASS: poem answer comparison ignores whitespace, not words or punctuation');
assert.equal(typeof core.chooseQuestion, 'function', 'A whole-line poem question generator is missing');
const poem = { id: 'test', lines: ['짧다', '충분히 길고 의미 있는 첫 번째 줄', '', '충분히 길고 의미 있는 두 번째 줄'] };
const q = core.chooseQuestion(poem, () => 0);
assert.equal(q.blankIndex, 1);
assert.equal(q.expected, poem.lines[1]);
assert.deepEqual(q.lines, poem.lines);
assert.equal(core.chooseQuestion(poem, () => 0.999).blankIndex, 3);
assert.throws(() => core.chooseQuestion({ lines: ['', ''] }), /비울/);
console.log('PASS: poem game blanks one meaningful whole line, preserving the canonical poem');
assert.equal(typeof core.breathPhase, 'function', 'The optional, stoppable breathing guide timer is missing');
const timing = { InhaleSeconds: 4, HoldSeconds: 0, ExhaleSeconds: 6 };
assert.equal(core.breathPhase(0, timing).phase, 'inhale');
assert.equal(core.breathPhase(3999, timing).phase, 'inhale');
assert.equal(core.breathPhase(4000, timing).phase, 'exhale');
assert.equal(core.breathPhase(10000, timing).phase, 'inhale');
assert.equal(core.breathPhase(0, timing).remaining, 4);
assert.equal(core.breathPhase(4000, { ...timing, HoldSeconds: 30 }).phase, 'hold');
assert.equal(core.breathPhase(34000, { ...timing, HoldSeconds: 30 }).phase, 'exhale');
assert.throws(() => core.breathPhase(0, { ...timing, HoldSeconds: 31 }), /30/);
console.log('PASS: breath timer skips holds by default and supports an optional hold up to 30 seconds');

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
