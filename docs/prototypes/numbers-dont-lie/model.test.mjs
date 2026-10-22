import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { leagues, dailySet, practiceSet, readPreferences, score } from './model.mjs';
const { questions } = JSON.parse(readFileSync(new URL('./questions.json', import.meta.url)));

test('24 distinct, sourced facts have four distinct choices and one correct answer', () => {
  assert.equal(questions.length, 24);
  assert.equal(new Set(questions.map(q => q.id)).size, 24);
  assert.equal(new Set(questions.map(q => q.factFamily)).size, 24);
  for (const league of leagues) assert.equal(questions.filter(q => q.league === league).length, 6);
  for (const q of questions) {
    assert.equal(q.scope, 'regular-season');
    assert.match(q.prompt, /^Among these four,/);
    assert.equal(q.options.length, 4);
    assert.equal(new Set(q.options.map(o => o.label)).size, 4);
    assert.equal(new Set(q.options.map(o => o.id)).size, 4);
    assert.ok(q.options.every(o => Number.isInteger(o.value) && o.value >= 0));
    const target = q.template === 'match-value' ? q.targetValue : Math.max(...q.options.map(o => o.value));
    assert.deepEqual(q.options.filter(o => o.value === target).map(o => o.id), [q.correctOptionId], q.id);
    assert.equal(new URL(q.source.url).protocol, 'https:');
    assert.ok(q.source.credit && q.explanation && q.checkedAt);
    assert.equal(q.publicationStatus, 'prototype-only');
  }
});

test('all 15 practice filters stay balanced, distinct and inside the selected leagues', () => {
  for (let mask = 1; mask < 16; mask++) {
    const selected = leagues.filter((_, i) => mask & (1 << i));
    for (const random of [() => 0, () => 0.5, () => 0.999999]) {
      const set = practiceSet(questions, selected, random);
      assert.equal(set.length, 5);
      assert.equal(new Set(set.map(q => q.id)).size, 5);
      assert.ok(set.every(q => selected.includes(q.league)));
      const counts = selected.map(l => set.filter(q => q.league === l).length);
      assert.ok(Math.max(...counts) - Math.min(...counts) <= 1);
    }
  }
  assert.throws(() => practiceSet(questions, []));
  assert.throws(() => practiceSet(questions, ['unsupported']));
  assert.throws(() => practiceSet([], ['nba']));
});

test('daily set is stable and independent of practice preferences', () => {
  const daily = dailySet(questions);
  assert.equal(daily.length, 5);
  assert.ok(daily.every(Boolean));
  assert.equal(new Set(daily.map(q => q.league)).size, 4);
  assert.equal(new Set(daily.map(q => q.options.find(o => o.id === q.correctOptionId).label)).size, 5);
  practiceSet(questions, ['nhl']);
  assert.deepEqual(dailySet(questions), daily);
  assert.equal(score(daily.map(question => ({ question, optionId:question.correctOptionId }))), 5);
  assert.equal(score(daily.map(question => ({ question, optionId:'wrong' }))), 0);
});

test('preferences tolerate blocked, malformed, empty and obsolete storage', () => {
  assert.deepEqual(readPreferences(undefined), leagues);
  assert.deepEqual(readPreferences({ getItem() { throw new Error(); } }), leagues);
  for (const raw of ['bad json', '{}', '[]', '["gone"]']) assert.deepEqual(readPreferences({ getItem:() => raw }), leagues);
  assert.deepEqual(readPreferences({ getItem:() => '["nhl","nba","nba","gone"]' }), ['nba','nhl']);
});
