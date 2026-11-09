'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');
const core = require('../app-core.js');
const root = path.resolve(__dirname, '..');
const context = { window: {} };
for (const file of ['poetry-data.js', 'poetry-reading.js']) vm.runInNewContext(fs.readFileSync(path.join(root, file), 'utf8'), context, { timeout: 2000 });
const library = context.window.POEM_LIBRARY;
const reading = context.window.POEM_READING;

test('every real source unit can be randomly blanked, directly selected and answered without changing its text', () => {
  let units = 0;
  for (const poem of library.poems) {
    const snapshot = JSON.stringify(poem);
    const eligible = poem.lines.map((line, index) => ({ line, index })).filter(item => core.normalizeAnswer(item.line).length);
    const reached = new Set();
    eligible.forEach((item, position) => {
      const selected = core.chooseQuestion(poem, () => (position + 0.5) / eligible.length, { includeShort: true });
      assert.equal(selected.blankIndex, item.index, poem.id);
      reached.add(selected.blankIndex);
      const direct = core.chooseQuestion(poem, () => 0, { includeShort: true, blankIndex: item.index });
      assert.equal(direct.expected, item.line);
      assert.equal(core.compareAnswer(item.line, direct.expected), true);
      const rerolled = core.chooseQuestion(poem, () => 0, { includeShort: true, previousIndex: item.index });
      assert.notEqual(rerolled.blankIndex, item.index, poem.id);
      units++;
    });
    assert.equal(reached.size, eligible.length);
    assert.equal(JSON.stringify(poem), snapshot);
  }
  console.log(`Verified ${library.poems.length} actual works and ${units} complete source units`);
});

test('contemporary reading links are metadata-only and remain separate from playable texts', () => {
  assert.match(reading.checkedAt, /^\d{4}-\d{2}-\d{2}$/);
  assert.ok(reading.items.length >= 6);
  const allowed = new Set(['www.changbi.com', 'www.bobdylan.com', 'www.penguinrandomhouse.com', 'www.penguin.co.uk', 'www.ndbooks.com']);
  const ids = new Set();
  for (const item of reading.items) {
    assert.ok(item.id && item.title && item.author && item.note);
    assert.ok(['recent', 'song', 'nobel'].includes(item.category));
    const url = new URL(item.url);
    assert.equal(url.protocol, 'https:'); assert.equal(url.username, ''); assert.equal(url.port, '');
    assert.ok(allowed.has(url.hostname));
    for (const property of ['lines', 'text', 'excerpt']) assert.equal(item[property], undefined, `${item.id}: copyrighted text must not be bundled`);
    assert.ok(!ids.has(item.id)); ids.add(item.id);
    assert.ok(!library.poems.some(poem => poem.id === item.id));
  }
  for (const category of ['recent', 'song', 'nobel']) assert.ok(reading.items.filter(item => item.category === category).length >= 2);
});

test('the actual app loads all local corpus assets before UI execution without weakening CSP', () => {
  const html = fs.readFileSync(path.join(root, 'app.html'), 'utf8');
  assert.match(html, /connect-src 'none'/);
  for (const file of ['poetry-data.js', 'poetry-reading.js', 'poetry.css']) {
    assert.ok(fs.existsSync(path.join(root, file)), file);
    assert.ok(html.includes(`"${file}"`));
  }
  assert.ok(html.indexOf('poetry-data.js') < html.indexOf('src="app.js"'));
  assert.ok(html.indexOf('poetry-reading.js') < html.indexOf('src="app.js"'));
});
