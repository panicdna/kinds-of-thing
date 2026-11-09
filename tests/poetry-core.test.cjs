const assert = require('node:assert/strict');
const core = require('../app-core.js');

const poems = [
  { id: 'a', title: '봄날', author: '가', category: 'lyric', language: 'ko', lines: ['짧다', '', '조금 더 긴 첫 번째 줄', '   ', '마지막 줄'] },
  { id: 'b', title: '노래', author: '나', category: 'song', language: 'ko', lines: ['첫 줄', '다음 줄'] },
  { id: 'c', title: 'The Lake', author: 'Yeats', category: 'nobel', language: 'en', lines: ['An English line', 'Another line'] }
];

assert.equal(typeof core.filterPoems, 'function', 'A searchable category/language poetry library is missing');
assert.deepEqual(core.filterPoems(poems, { category: 'song' }).map(p => p.id), ['b']);
assert.deepEqual(core.filterPoems(poems, { language: 'en' }).map(p => p.id), ['c']);
assert.deepEqual(core.filterPoems(poems, { query: ' YEATS ' }).map(p => p.id), ['c']);
assert.deepEqual(core.filterPoems(poems, { category: 'lyric', query: '봄' }).map(p => p.id), ['a']);
assert.deepEqual(core.filterPoems(poems, { category: 'song', query: '없는 시' }), []);
assert.equal(core.filterPoems(poems).length, 3);
assert.equal(poems.length, 3, 'Filtering must not mutate the source corpus');
console.log('PASS: poetry filters combine category, language and case-insensitive title/author search');

assert.equal(typeof core.choosePoem, 'function', 'Random work selection is missing');
assert.equal(core.choosePoem(poems, '', () => 0).id, 'a');
assert.equal(core.choosePoem(poems, 'a', () => 0).id, 'b');
assert.equal(core.choosePoem(poems, 'a', () => 0.99999).id, 'c');
assert.equal(core.choosePoem([poems[0]], 'a', () => 0).id, 'a');
assert.equal(core.choosePoem([], '', () => 0), null);
for (const value of [-1, 1, 9, NaN, Infinity]) assert.ok(core.choosePoem(poems, '', () => value));
assert.equal(poems.length, 3);
console.log('PASS: random work choice covers the pool and avoids an immediate repeat when possible');

const whole = { includeShort: true };
assert.equal(core.chooseQuestion(poems[0], () => 0, whole).blankIndex, 0, 'Short lines must also be available in all-line mode');
assert.equal(core.chooseQuestion(poems[0], () => 0.4, whole).blankIndex, 2);
assert.equal(core.chooseQuestion(poems[0], () => 0.9999, whole).blankIndex, 4);
assert.equal(core.chooseQuestion(poems[0], () => 0, { ...whole, previousIndex: 0 }).blankIndex, 2);
assert.equal(core.chooseQuestion(poems[0], () => 0, { ...whole, blankIndex: 4 }).expected, '마지막 줄');
assert.throws(() => core.chooseQuestion(poems[0], () => 0, { ...whole, blankIndex: 1 }), /줄/);
assert.throws(() => core.chooseQuestion(poems[0], () => 0, { ...whole, blankIndex: -1 }), /줄/);
assert.throws(() => core.chooseQuestion(poems[0], () => 0, { ...whole, blankIndex: 1.5 }), /줄/);
assert.equal(core.chooseQuestion({ id: 'one', lines: ['', '하나'] }, () => 0, { ...whole, previousIndex: 1 }).blankIndex, 1);
for (const value of [-5, 1, 8, NaN, Infinity]) assert.ok(core.chooseQuestion(poems[0], () => value, whole).expected.trim());
const result = core.chooseQuestion(poems[0], () => 0, whole);
result.lines[0] = 'changed';
assert.equal(poems[0].lines[0], '짧다', 'Questions must not mutate canonical text');
console.log('PASS: every nonblank line can be random or explicitly selected; rerolls avoid the previous line');
