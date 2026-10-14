import { shareHeadline } from './share';

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
