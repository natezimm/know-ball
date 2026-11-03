import { nextEasternMidnight, nextGame } from './games';

describe('games', () => {
  it('goes on to the next game and wraps round', () => {
    expect(nextGame('spordle').slug).toBe('scouting-report');
    expect(nextGame('numbers-dont-lie').slug).toBe('spordle');
  });

  it('finds the next midnight in New York in winter and summer', () => {
    expect(nextEasternMidnight(new Date('2026-11-03T13:00:00Z'))).toBe('2026-11-04T05:00:00.000Z');
    expect(nextEasternMidnight(new Date('2026-07-04T03:30:00Z'))).toBe('2026-07-04T04:00:00.000Z');
    expect(nextEasternMidnight(new Date('2026-07-04T05:00:00Z'))).toBe('2026-07-05T04:00:00.000Z');
  });
});
