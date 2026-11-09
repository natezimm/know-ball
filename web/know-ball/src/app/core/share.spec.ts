import { missHeadline, shareHeadline } from './share';

describe('shareHeadline', () => {
  it('names the game, the puzzle and the score', () => {
    expect(shareHeadline('Scouting Report', 142, 4, 8, true)).toBe('Know Ball · Scouting Report #142 4/8');
  });

  it('marks a loss with an X', () => {
    expect(shareHeadline('Career Path', 57, 6, 6, false)).toBe('Know Ball · Career Path #57 X/6');
  });

  it('says practice when the round has no number', () => {
    expect(shareHeadline('Career Path', null, 2, 6, true)).toBe('Know Ball · Career Path practice 2/6');
  });
});

describe('missHeadline', () => {
  it('scores a win by its misses', () => {
    expect(missHeadline('Team Photo', 12, 0, 4, true)).toBe('Know Ball · Team Photo #12 0 misses');
    expect(missHeadline('Team Photo', 12, 1, 4, true)).toBe('Know Ball · Team Photo #12 1 miss');
  });

  it('marks a loss with an X and says practice without a number', () => {
    expect(missHeadline('Team Photo', 12, 4, 4, false)).toBe('Know Ball · Team Photo #12 X/4');
    expect(missHeadline('Team Photo', null, 2, 4, true)).toBe('Know Ball · Team Photo practice 2 misses');
  });
});
