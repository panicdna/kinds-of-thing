/* Synchronous native WebView2 UI acceptance test; no external requests. */
(() => {
  const $ = selector => document.querySelector(selector);
  const check = (condition, message) => { if (!condition) throw new Error(message); };
  const change = (selector, value, event = 'change') => {
    const input = $(selector); check(input, `${selector} is missing`);
    input.value = value; input.dispatchEvent(new Event(event, { bubbles: true }));
  };
  try {
    check($('#poem-category') && $('#poem-picker') && $('#poem-line-choice'), 'Poetry library controls are missing');
    const poems = window.POEM_LIBRARY.poems;
    check(poems.length >= 100, 'Fewer than 100 playable poems');
    check($('#poem-picker').options.length === poems.length, 'Not every poem can be selected');
    const initial = window.teumGameQuestion;
    $('#poem-next').click();
    check(window.teumGameQuestion.poemId !== initial.poemId, 'Random next work immediately repeated');
    const before = window.teumGameQuestion;
    $('#poem-reroll').click();
    check(window.teumGameQuestion.poemId === before.poemId && window.teumGameQuestion.blankIndex !== before.blankIndex, 'Reroll did not choose another line in the same work');
    const explicit = Array.from($('#poem-line-choice').options).find(option => option.value !== 'random');
    change('#poem-line-choice', explicit.value);
    check(window.teumGameQuestion.blankIndex === Number(explicit.value), 'Explicit line selection was ignored');
    $('#poem-answer').value = window.teumGameQuestion.expected;
    $('#poem-submit').click();
    check($('#poem-result').dataset.correct === 'true' && $('.canonical-answer').textContent === window.teumGameQuestion.expected, 'Correct answer failed');
    $('#poem-answer').value = '<img src=x onerror=alert(1)>';
    $('#poem-submit').click();
    check($('.user-answer').textContent === '<img src=x onerror=alert(1)>' && !$('.user-answer img'), 'Answer rendered executable HTML');
    $('#poem-reroll').click();
    check($('#poem-result').hidden && $('#poem-answer').value === '', 'Previous answer leaked into the new question');
    change('#poem-category', 'song');
    check($('#poem-picker').options.length > 0, 'Song lyrics are missing');
    check(Array.from($('#poem-picker').options).every(option => poems.find(poem => poem.id === option.value).category === 'song'), 'Song category mixed other works');
    change('#poem-category', 'nobel');
    check($('#poem-picker').options.length > 0, 'Nobel poetry is missing');
    check(Array.from($('#poem-picker').options).every(option => poems.find(poem => poem.id === option.value).category === 'nobel'), 'Nobel category mixed other works');
    change('#poem-language', 'ko');
    check(Array.from($('#poem-picker').options).every(option => poems.find(poem => poem.id === option.value).language === 'ko'), 'Language filter is ignored');
    change('#poem-category', 'all'); change('#poem-language', 'all');
    change('#poem-search', 'zzzz-no-such-poem-없음', 'input');
    check($('#poem-empty') && $('#poem-next').disabled && window.teumGameQuestion === null, 'Empty search did not reset stale question');
    change('#poem-search', '', 'input');
    check($('#poem-picker').options.length === poems.length && $('#poem-answer'), 'Clearing search did not restore the library');
    $('#poem-reading-tab').click();
    check(document.querySelectorAll('.reading-card').length >= 6, 'Official contemporary reading links are missing');
    check(!$('#poem-answer'), 'External-only works were treated as playable full texts');
    $('#poem-quiz-tab').click();
    check($('#poem-answer') && $('#poem-picker').options.length === poems.length, 'Returning from reading links lost quiz state');
    return { passed: true, playable: poems.length, reading: window.POEM_READING.items.length, randomWorkAndLine: true, filtersAndEmptyState: true, explicitLine: true, answerEscaping: true };
  } catch (error) { return { passed: false, error: error.message }; }
})();
