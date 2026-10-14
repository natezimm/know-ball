import { formatCountdown } from './countdown';

describe('formatCountdown', () => {
  it('shows hours, minutes and seconds', () => {
    expect(formatCountdown(5 * 3_600_000 + 12 * 60_000 + 33_000)).toBe('05:12:33');
  });

  it('rounds a part second up, so it never shows zero early', () => {
    expect(formatCountdown(400)).toBe('00:00:01');
  });

  it('stops at zero', () => {
    expect(formatCountdown(0)).toBe('00:00:00');
    expect(formatCountdown(-5000)).toBe('00:00:00');
  });
});
