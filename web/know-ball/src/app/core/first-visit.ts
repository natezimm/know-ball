const KEY = 'knowball.helpSeen';
const ANON_KEY = 'knowball.anonId';
/** The games that existed when first-visit help was added; anyone who played before then has seen them. */
const GAMES = ['scouting-report', 'career-path', 'back-of-the-card', 'team-photo', 'numbers-dont-lie', 'letter-jacket'];

function seen(): string[] | null {
  const value: unknown = JSON.parse(localStorage.getItem(KEY) ?? 'null');
  return Array.isArray(value) ? value.filter((v) => typeof v === 'string') : null;
}

/**
 * Called once as the app starts, before anything asks the API. A browser that already plays here but
 * has no record of the help it has seen is a returning player, and is not shown How to play again.
 */
export function noteReturningPlayer(): void {
  try {
    if (seen()) return;
    localStorage.setItem(KEY, JSON.stringify(localStorage.getItem(ANON_KEY) ? GAMES : []));
  } catch { /* Blocked storage: help is never opened unasked. */ }
}

/**
 * Whether this is the browser's first time at a game, so its How to play should open by itself. True once
 * per game; asking records the visit. Without storage it is always false, so the help never nags.
 */
export function firstVisit(game: string): boolean {
  try {
    const games = seen() ?? [];
    if (games.includes(game)) return false;
    localStorage.setItem(KEY, JSON.stringify([...games, game]));
    return true;
  } catch {
    return false;
  }
}
