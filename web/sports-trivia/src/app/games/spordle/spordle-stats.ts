export const WORD_LENGTHS = [4, 5, 6] as const;
export type WordLength = (typeof WORD_LENGTHS)[number];
export const DEFAULT_WORD_LENGTH: WordLength = 5;

export interface LengthStats {
  totalGames: number;
  wins: number;
  currentStreak: number;
  longestStreak: number;
  fastestSolveMs: number | null;
  fewestGuesses: number | null;
}

interface StoredStats {
  version: 1;
  byLength: Record<string, LengthStats>;
}

const STORAGE_KEY = 'knowball.spordle.stats';

const emptyStats = (): LengthStats => ({
  totalGames: 0,
  wins: 0,
  currentStreak: 0,
  longestStreak: 0,
  fastestSolveMs: null,
  fewestGuesses: null,
});

const emptyAll = (): StoredStats => ({
  version: 1,
  byLength: Object.fromEntries(WORD_LENGTHS.map((length) => [String(length), emptyStats()])),
});

export const isWordLength = (value: unknown): value is WordLength => WORD_LENGTHS.includes(value as WordLength);

function loadAll(): StoredStats {
  const all = emptyAll();
  try {
    const parsed = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? 'null');
    for (const length of WORD_LENGTHS) {
      const saved = parsed?.byLength?.[String(length)];
      if (saved && typeof saved === 'object') all.byLength[String(length)] = { ...emptyStats(), ...saved };
    }
  } catch {
    // Unreadable or blocked storage: start from zero.
  }
  return all;
}

function saveAll(all: StoredStats): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(all));
  } catch {
    // Storage blocked (private window): stats last for this visit only.
  }
}

/** Stats are kept separately for each word length. */
export function getStats(wordLength: WordLength): LengthStats {
  return loadAll().byLength[String(wordLength)];
}

export function recordGame(
  wordLength: WordLength,
  game: { won: boolean; guesses: number; timeMs: number | null },
): LengthStats {
  const all = loadAll();
  const stats = all.byLength[String(wordLength)];

  stats.totalGames += 1;
  if (game.won) {
    stats.wins += 1;
    stats.currentStreak += 1;
    stats.longestStreak = Math.max(stats.longestStreak, stats.currentStreak);
    if (game.timeMs !== null && (stats.fastestSolveMs === null || game.timeMs < stats.fastestSolveMs)) {
      stats.fastestSolveMs = game.timeMs;
    }
    if (stats.fewestGuesses === null || game.guesses < stats.fewestGuesses) stats.fewestGuesses = game.guesses;
  } else {
    stats.currentStreak = 0;
  }

  saveAll(all);
  return stats;
}

export function resetStats(scope: WordLength | 'all'): void {
  const all = scope === 'all' ? emptyAll() : loadAll();
  if (scope !== 'all') all.byLength[String(scope)] = emptyStats();
  saveAll(all);
}

/** 83000 becomes "1:23"; no time yet becomes "--". */
export function formatTime(ms: number | null): string {
  if (ms === null) return '--';
  const seconds = Math.floor(ms / 1000);
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
}
