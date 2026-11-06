import { mergeKeyboardStatuses } from './scoring';

describe('mergeKeyboardStatuses', () => {
  it('colours each guessed letter with its result', () => {
    expect(mergeKeyboardStatuses({}, 'punt', ['absent', 'present', 'absent', 'correct'])).toEqual({
      p: 'absent',
      u: 'present',
      n: 'absent',
      t: 'correct',
    });
  });

  it('never downgrades a key and keeps letters it was not told about', () => {
    const next = mergeKeyboardStatuses({ t: 'correct', u: 'present', z: 'absent' }, 'tutu', ['present', 'absent', 'absent', 'correct']);
    expect(next).toEqual({ t: 'correct', u: 'correct', z: 'absent' });
  });

  it('upgrades a letter that scores better within the same guess', () => {
    expect(mergeKeyboardStatuses({}, 'eerie', ['correct', 'absent', 'absent', 'absent', 'correct'])['e']).toBe('correct');
  });

  it('leaves the previous statuses untouched', () => {
    const current = { a: 'absent' as const };
    mergeKeyboardStatuses(current, 'a', ['correct']);
    expect(current).toEqual({ a: 'absent' });
  });
});
