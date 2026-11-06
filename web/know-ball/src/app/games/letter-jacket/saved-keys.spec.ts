import { useMemoryStorage } from './memory-storage';
import { adoptOldKeys } from './saved-keys';
import { getStats } from './letter-jacket-stats';

describe('saved keys from before the rename', () => {
  beforeEach(() => useMemoryStorage());

  it('carries stats, word length and an open round over to the new keys', () => {
    localStorage.setItem('knowball.spordle.stats', JSON.stringify({ byLength: { '5': { totalGames: 4, wins: 3 } } }));
    localStorage.setItem('knowball.spordle.wordLength', '6');
    localStorage.setItem('knowball.spordle.round', '{"gameId":"g1"}');

    adoptOldKeys();

    expect(localStorage.getItem('knowball.letter-jacket.wordLength')).toBe('6');
    expect(localStorage.getItem('knowball.letter-jacket.round')).toBe('{"gameId":"g1"}');
    expect(getStats(5)).toMatchObject({ totalGames: 4, wins: 3 });
    expect(localStorage.getItem('knowball.spordle.stats')).toBeNull();
    expect(localStorage.getItem('knowball.spordle.round')).toBeNull();
  });

  it('never overwrites a value already saved under the new name', () => {
    localStorage.setItem('knowball.letter-jacket.wordLength', '4');
    localStorage.setItem('knowball.spordle.wordLength', '6');

    adoptOldKeys();

    expect(localStorage.getItem('knowball.letter-jacket.wordLength')).toBe('4');
    expect(localStorage.getItem('knowball.spordle.wordLength')).toBeNull();
  });
});
