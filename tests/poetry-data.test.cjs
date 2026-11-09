'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');

const root = path.resolve(__dirname, '..');
const dataPath = path.join(root, 'poetry-data.js');
function loadLibrary() {
  assert.ok(fs.existsSync(dataPath), 'poetry-data.js must provide at least 110 real playable poems');
  const context = { window: { GAME_CONTENT: { dances: ['unchanged'] } } };
  vm.runInNewContext(fs.readFileSync(dataPath, 'utf8'), context, { timeout: 2000 });
  assert.deepEqual(context.window.GAME_CONTENT.dances, ['unchanged']);
  return context.window.POEM_LIBRARY;
}

function canonical(text) {
  return text.normalize('NFKC').toLowerCase().replace(/[\p{P}\p{Z}\s]/gu, '');
}

test('offline corpus contains 110 distinct playable poems, Korean majority, songs and Nobel poetry', () => {
  const library = loadLibrary();
  assert.match(library.checkedAt, /^\d{4}-\d{2}-\d{2}$/);
  const poems = library.poems;
  assert.ok(Array.isArray(poems) && poems.length >= 110, 'need at least 110 actual poems, not link-only entries');
  assert.ok(poems.filter(p => p.language === 'ko').length > poems.length / 2, 'Korean majority');
  assert.ok(new Set(poems.map(p => p.author)).size >= 3, 'at least three authors');
  assert.ok(poems.filter(p => p.category === 'nobel').length >= 10, 'at least ten Nobel poems');
  assert.ok(poems.filter(p => p.category === 'song').length >= 3, 'at least three PD songs');
  const ids = new Set();
  const bodies = new Set();
  const works = new Set();
  for (const poem of poems) {
    assert.match(poem.id, /^[a-z0-9][a-z0-9-]+$/);
    assert.ok(!ids.has(poem.id), `duplicate id ${poem.id}`); ids.add(poem.id);
    for (const key of ['title', 'author', 'edition', 'source']) assert.ok(typeof poem[key] === 'string' && poem[key].trim(), `${poem.id}: ${key}`);
    assert.ok(Number.isInteger(poem.year) && poem.year < 1931, `${poem.id}: only pre-1931 editions`);
    assert.ok(['ko', 'en'].includes(poem.language));
    assert.ok(['lyric', 'song', 'nobel'].includes(poem.category));
    assert.equal(poem.rights, 'public-domain');
    assert.equal(new URL(poem.source).protocol, 'https:');
    assert.ok(Array.isArray(poem.lines));
    assert.ok(poem.lines.every(line => typeof line === 'string' && line === line.trim() && !/[\r\n]/.test(line)), `${poem.id}: original line or paragraph units`);
    assert.ok(poem.lines.filter(line => line.trim()).length >= 2, `${poem.id}: needs at least two original units`);
    assert.ok(poem.lines.filter(line => canonical(line).length > 0).length >= 2, `${poem.id}: at least two nonempty original question units, including short verses`);
    const body = canonical(poem.lines.join(''));
    assert.ok(!bodies.has(body), `${poem.id}: duplicate body`); bodies.add(body);
    const work = canonical(poem.author + '/' + poem.title);
    assert.ok(!works.has(work), `${poem.id}: duplicate author/title`); works.add(work);
    if (poem.category === 'nobel') assert.ok([1913, 1923].includes(poem.nobelYear), `${poem.id}: Nobel provenance`);
  }
});

test('explicit source verse breaks are not collapsed into prose-like stanza strings', () => {
  const poems = loadLibrary().poems;
  for (const [title, expected] of [['가을 저녁에', 12], ['개여울의 노래', 12], ['개여울', 15]]) {
    const poem = poems.find(poem => poem.author === '김소월' && poem.title === title);
    assert.equal(poem.lines.filter(Boolean).length, expected, `${title}: preserve every source br line break`);
  }
});

test('every playable text has pinned provenance, copyright evidence and source integrity hashes', () => {
  const library = loadLibrary();
  const manifest = JSON.parse(fs.readFileSync(path.join(root, 'poetry-sources.json'), 'utf8'));
  const { createHash } = require('node:crypto');
  const hash = text => createHash('sha256').update(text).digest('hex');
  assert.equal(manifest.checkedAt, library.checkedAt);
  assert.ok(Array.isArray(manifest.copyrightEvidence) && manifest.copyrightEvidence.length >= 7, 'retrieved copyright/author/Nobel evidence is required');
  assert.match(manifest.editorialLicense.name, /Attribution-ShareAlike 4\.0/);
  const records = new Map(manifest.records.map(record => [record.id, record]));
  assert.equal(records.size, library.poems.length);
  for (const poem of library.poems) {
    const record = records.get(poem.id);
    assert.ok(record, poem.id);
    assert.equal(record.source, poem.source);
    assert.equal(Number(new URL(poem.source).searchParams.get('oldid')), record.revisionId);
    assert.ok(Number.isInteger(record.revisionId) && record.revisionId > 0);
    assert.equal(hash(record.rawFragment), record.fragmentSha256);
    assert.equal(hash(JSON.stringify(poem.lines, null, 2)), record.linesSha256);
    assert.match(record.renderSha256, /^[a-f0-9]{64}$/);
    assert.ok(manifest.copyrightEvidence.some(evidence => evidence.author === poem.author && evidence.publicDomainDesignation), `${poem.id}: verified author PD designation`);
    assert.ok(record.editorialLicenseNotice && record.editorialLicenseNotice.links.some(url => url.includes('creativecommons.org/licenses/by-sa/')), `${poem.id}: retain actual edition footer terms`);
    for (const page of record.transcludedPages) {
      assert.ok(page.title && Number.isInteger(page.revisionId) && page.revisionId > 0, `${poem.id}: transcluded source revision`);
    }
    if (poem.category === 'nobel') {
      assert.equal(record.unitType, 'original-paragraph');
      assert.match(poem.edition, /original prose-poem paragraphs/);
      assert.match(record.rawFragment, /prp-pages-output/);
    }
    assert.ok(!poem.lines.some(line => /라이선스|Public domain|Creative Commons|\[편집\]/.test(line)), `${poem.id}: no navigation/license text in verse`);
  }
});
