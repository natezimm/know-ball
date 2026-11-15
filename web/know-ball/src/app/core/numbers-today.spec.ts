import { useMemoryStorage } from '../games/letter-jacket/memory-storage';
import { numbersStreak, numbersToday, saveNumbersToday } from './numbers-today';

const round = { mode: 'daily', date: '2026-11-14', answeredCount: 5, correctCount: 4, complete: true, dailyStreak: 3 };

describe("today's quiz as the home page reads it", () => {
  beforeEach(() => useMemoryStorage());

  it('keeps a daily round for its own day only', () => {
    saveNumbersToday(round);
    expect(numbersToday('2026-11-14')).toEqual({ date: '2026-11-14', answered: 5, correct: 4, complete: true, streak: 3 });
    expect(numbersToday('2026-11-15')).toBeNull();
  });

  it('ignores practice rounds and older days reopened later', () => {
    saveNumbersToday(round);
    saveNumbersToday({ ...round, mode: 'practice', date: null, correctCount: 0 });
    saveNumbersToday({ ...round, date: '2026-11-10', correctCount: 1 });
    expect(numbersToday('2026-11-14')?.correct).toBe(4);
  });

  it('keeps the streak through the next day and drops it after a missed day', () => {
    saveNumbersToday(round);
    expect(numbersStreak('2026-11-14')).toBe(3);
    expect(numbersStreak('2026-11-15')).toBe(3);
    expect(numbersStreak('2026-11-16')).toBe(0);
    // The first of a month follows the last day of the month before.
    saveNumbersToday({ ...round, date: '2026-11-30' });
    expect(numbersStreak('2026-12-01')).toBe(3);
  });

  it('reads damaged or missing storage as not played', () => {
    expect(numbersToday('2026-11-14')).toBeNull();
    localStorage.setItem('knowball.numbers.today', '{"date":5');
    expect(numbersToday('2026-11-14')).toBeNull();
    expect(numbersStreak('2026-11-14')).toBe(0);
  });
});
