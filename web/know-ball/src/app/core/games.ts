/** The six games in the order the site presents them: the home page, and the "next game" button after a result. */
export const GAMES = [
  { slug: 'letter-jacket', name: 'Letter Jacket' },
  { slug: 'scouting-report', name: 'Scouting Report' },
  { slug: 'career-path', name: 'Career Path' },
  { slug: 'back-of-the-card', name: 'Back of the Card' },
  { slug: 'team-photo', name: 'Team Photo' },
  { slug: 'numbers-dont-lie', name: "Numbers Don't Lie" },
] as const;

export type GameRoute = (typeof GAMES)[number]['slug'];

/** The game after this one, wrapping round to the first. */
export function nextGame(current: GameRoute): (typeof GAMES)[number] {
  const i = GAMES.findIndex((g) => g.slug === current);
  return GAMES[(i + 1) % GAMES.length];
}

/** The next midnight in New York, when the daily games change, as an ISO time. */
export function nextEasternMidnight(now: Date = new Date()): string {
  // Eastern is UTC-5 or UTC-4; find the offset that makes the wall clock there read midnight tomorrow.
  const parts = new Intl.DateTimeFormat('en-US', { timeZone: 'America/New_York', year: 'numeric', month: '2-digit', day: '2-digit' })
    .formatToParts(now);
  const get = (type: string) => Number(parts.find((p) => p.type === type)!.value);
  const tomorrow = Date.UTC(get('year'), get('month') - 1, get('day') + 1);
  for (const hours of [4, 5]) {
    const candidate = new Date(tomorrow + hours * 3_600_000);
    const hour = new Intl.DateTimeFormat('en-US', { timeZone: 'America/New_York', hour: '2-digit', hourCycle: 'h23' }).format(candidate);
    if (hour === '00') return candidate.toISOString();
  }
  return new Date(tomorrow + 5 * 3_600_000).toISOString();
}

/** Today's date in New York as yyyy-mm-dd, the calendar the daily games run on. */
export function easternDate(now: Date = new Date()): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'America/New_York', year: 'numeric', month: '2-digit', day: '2-digit' }).format(now);
}
