import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { leagues, dailySet, practiceSet, readPreferences, score, easternDate, completionStreak, era } from './model.mjs';
const { questions } = JSON.parse(readFileSync(new URL('./questions.json', import.meta.url)));

test('56 distinct, sourced facts have four distinct choices and one correct answer', () => {
  assert.equal(questions.length, 56);
  assert.equal(new Set(questions.map(q => q.id)).size, 56);
  assert.equal(new Set(questions.map(q => q.factFamily)).size, 56);
  for (const league of leagues) assert.equal(questions.filter(q => q.league === league).length, 14);
  for (const q of questions) {
    assert.equal(q.options.length, 4);
    assert.equal(new Set(q.options.map(o => o.label)).size, 4);
    assert.equal(new Set(q.options.map(o => o.id)).size, 4);
    if (q.template !== 'trivia') {
    assert.ok(q.options.every(o => Number.isInteger(o.value) && o.value >= 0));
    const target = q.template === 'match-value' ? q.targetValue : Math.max(...q.options.map(o => o.value));
    assert.deepEqual(q.options.filter(o => o.value === target).map(o => o.id), [q.correctOptionId], q.id);
    }
    assert.equal(new URL(q.source.url).protocol, 'https:');
    assert.ok(q.source.credit && q.explanation && q.checkedAt);
    assert.equal(q.publicationStatus, 'prototype-only');
  }
});

test('dated daily rounds are stable, diverse and change over time', () => {
  const a = dailySet(questions, '2026-10-24');
  assert.deepEqual(a, dailySet(questions, '2026-10-24'));
  assert.notDeepEqual(a, dailySet(questions, '2026-10-25'));
  assert.equal(new Set(a.map(q => q.league)).size, 4);
  assert.ok(new Set(a.map(q => q.topic)).size >= 3);
});

test('filters are enforced and fresh questions are used before repeats', () => {
  const first = practiceSet(questions, ['nba']);
  const second = practiceSet(questions, ['nba'], Math.random, {seen:first.map(q => q.id)});
  assert.ok(second.every(q => !first.some(p => p.id === q.id)));
  const awards = practiceSet(questions, leagues, Math.random, {topics:['awards']});
  assert.ok(awards.every(q => q.topic === 'awards'));
  const modern = practiceSet(questions, leagues, Math.random, {eras:['2020 onward']});
  assert.ok(modern.every(q => era(q) === '2020 onward'));
  assert.throws(() => practiceSet(questions, ['nba'], Math.random, {topics:['awards'],eras:['Before 2000']}));
});

test('completion streaks count consecutive days, allow today unfinished, and reset after a gap', () => {
  assert.equal(completionStreak(['2026-10-22','2026-10-23'], '2026-10-24'), 2);
  assert.equal(completionStreak(['2026-10-22'], '2026-10-24'), 0);
  assert.equal(completionStreak(['2026-10-23','2026-10-24','2026-10-24'], '2026-10-24'), 2);
  assert.equal(easternDate(new Date('2026-10-24T03:59:59Z')), '2026-10-23');
  assert.equal(easternDate(new Date('2026-10-24T04:00:00Z')), '2026-10-24');
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
