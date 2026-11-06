/**
 * Browser keys this game saves under. Until November 2026 the game was called Spordle and saved under
 * "knowball.spordle.*"; {@link adoptOldKeys} moves anything left there so stats and an open round carry over.
 */
export const KEYS = {
  stats: 'knowball.letter-jacket.stats',
  wordLength: 'knowball.letter-jacket.wordLength',
  round: 'knowball.letter-jacket.round',
} as const;

const OLD_PREFIX = 'knowball.spordle.';

/** Moves values saved under the old name to the new keys, unless the new key already has one. Safe to call often. */
export function adoptOldKeys(): void {
  try {
    for (const key of Object.values(KEYS)) {
      const old = OLD_PREFIX + key.slice(key.lastIndexOf('.') + 1);
      const value = localStorage.getItem(old);
      if (value === null) continue;
      if (localStorage.getItem(key) === null) localStorage.setItem(key, value);
      localStorage.removeItem(old);
    }
  } catch {
    // Storage blocked: nothing to carry over.
  }
}
