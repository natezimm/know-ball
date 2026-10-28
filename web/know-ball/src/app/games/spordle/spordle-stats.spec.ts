import { useMemoryStorage } from './memory-storage';
import { formatTime, getStats, recordGame, resetStats } from './spordle-stats';

describe('spordle stats', () => {
  beforeEach(() => useMemoryStorage());

  it('starts empty', () => {
    expect(getStats(5)).toEqual({
      totalGames: 0,
      wins: 0,
      currentStreak: 0,
      longestStreak: 0,
      fastestSolveMs: null,
      fewestGuesses: null,
    });
  });

  it('tracks wins, streaks and personal bests', () => {
    recordGame(5, { won: true, guesses: 4, timeMs: 60_000 });
    recordGame(5, { won: true, guesses: 3, timeMs: 90_000 });

    expect(getStats(5)).toEqual({
      totalGames: 2,
      wins: 2,
      currentStreak: 2,
      longestStreak: 2,
      fastestSolveMs: 60_000,
      fewestGuesses: 3,
    });
  });

  it('a loss ends the streak but keeps the best one', () => {
    recordGame(5, { won: true, guesses: 2, timeMs: 10_000 });
    recordGame(5, { won: false, guesses: 6, timeMs: null });

    const stats = getStats(5);
    expect(stats.currentStreak).toBe(0);
    expect(stats.longestStreak).toBe(1);
    expect(stats.totalGames).toBe(2);
    expect(stats.fewestGuesses).toBe(2);
  });

  it('keeps each word length separate', () => {
    recordGame(4, { won: true, guesses: 5, timeMs: 30_000 });

    expect(getStats(4).totalGames).toBe(1);
    expect(getStats(5).totalGames).toBe(0);
    expect(getStats(6).totalGames).toBe(0);
  });

  it('resets one length or all of them', () => {
    recordGame(4, { won: true, guesses: 5, timeMs: 30_000 });
    recordGame(5, { won: true, guesses: 5, timeMs: 30_000 });

    resetStats(4);
    expect(getStats(4).totalGames).toBe(0);
    expect(getStats(5).totalGames).toBe(1);

    resetStats('all');
    expect(getStats(5).totalGames).toBe(0);
  });

  it('survives unreadable saved data', () => {
    localStorage.setItem('knowball.spordle.stats', '{not json');
    expect(getStats(5).totalGames).toBe(0);
  });

  it('formats solve times', () => {
    expect(formatTime(null)).toBe('--');
    expect(formatTime(7_400)).toBe('0:07');
    expect(formatTime(83_000)).toBe('1:23');
  });

  it('records each resumed round only once, including after a stats reset', () => {
    const win = { won: true, guesses: 2, timeMs: 10_000 };
    recordGame(5, win, 'round-one');
    recordGame(5, win, 'round-one');
    expect(getStats(5)).toMatchObject({ totalGames: 1, wins: 1, currentStreak: 1 });
    resetStats('all');
    recordGame(5, win, 'round-one');
    expect(getStats(5).totalGames).toBe(0);
    recordGame(5, win, 'round-two');
    recordGame(5, { won: false, guesses: 6, timeMs: null }, 'round-three');
    recordGame(5, { won: false, guesses: 6, timeMs: null }, 'round-three');
    expect(getStats(5)).toMatchObject({ totalGames: 2, wins: 1, currentStreak: 0, longestStreak: 1 });
  });
});
