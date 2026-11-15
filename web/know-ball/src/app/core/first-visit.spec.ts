import { useMemoryStorage } from '../games/letter-jacket/memory-storage';
import { firstVisit, noteReturningPlayer } from './first-visit';

describe('first-visit help', () => {
  beforeEach(() => useMemoryStorage());

  it('is offered once for each game to a newcomer', () => {
    noteReturningPlayer();
    expect(firstVisit('career-path')).toBe(true);
    expect(firstVisit('career-path')).toBe(false);
    expect(firstVisit('team-photo')).toBe(true);
  });

  it('is not offered to a browser that already played before the help existed', () => {
    localStorage.setItem('knowball.anonId', 'a-returning-player');
    noteReturningPlayer();
    expect(firstVisit('career-path')).toBe(false);
    expect(firstVisit('letter-jacket')).toBe(false);
  });

  it('does not count a newcomer as returning on a later page load', () => {
    noteReturningPlayer();
    localStorage.setItem('knowball.anonId', 'made-on-the-first-request');
    noteReturningPlayer();
    expect(firstVisit('scouting-report')).toBe(true);
  });

  it('never opens help when storage is damaged or blocked', () => {
    localStorage.setItem('knowball.helpSeen', '{oops');
    expect(firstVisit('scouting-report')).toBe(false);
  });
});
