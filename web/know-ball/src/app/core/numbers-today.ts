import { easternDate } from './games';

const KEY = 'knowball.numbers.today';

/** How this browser stands in a daily Numbers Don't Lie quiz, kept so the home page can show it without a request. */
export interface NumbersToday {
  /** The quiz's Eastern date, as YYYY-MM-DD. */
  date: string;
  answered: number;
  correct: number;
  complete: boolean;
  streak: number;
}

/** Called by the quiz whenever it shows a daily round. Practice rounds are not kept. */
export function saveNumbersToday(round: {
  mode: string; date: string | null; answeredCount: number; correctCount: number; complete: boolean; dailyStreak?: number | null;
}): void {
  if (round.mode !== 'daily' || !round.date) return;
  const saved: NumbersToday = {
    date: round.date, answered: round.answeredCount, correct: round.correctCount, complete: round.complete, streak: round.dailyStreak ?? 0,
  };
  try {
    // An older day reopened from its address must not replace a newer one.
    const kept = read();
    if (kept && kept.date > saved.date) return;
    localStorage.setItem(KEY, JSON.stringify(saved));
  } catch { /* Blocked storage: the home page just offers the quiz. */ }
}

/** Today's quiz as last seen in this browser, or null when it has not been opened today. */
export function numbersToday(today: string = easternDate()): NumbersToday | null {
  const saved = read();
  return saved?.date === today ? saved : null;
}

/** The daily streak to show: it stands while the last finished quiz was today's or yesterday's. */
export function numbersStreak(today: string = easternDate()): number {
  const saved = read();
  if (!saved?.streak) return 0;
  if (saved.date === today) return saved.streak;
  const yesterday = new Date(`${today}T12:00:00Z`);
  yesterday.setUTCDate(yesterday.getUTCDate() - 1);
  return saved.complete && saved.date === yesterday.toISOString().slice(0, 10) ? saved.streak : 0;
}

function read(): NumbersToday | null {
  try {
    const value = JSON.parse(localStorage.getItem(KEY) ?? 'null');
    const valid = typeof value?.date === 'string' && [value.answered, value.correct, value.streak].every(Number.isFinite);
    return valid ? { ...value, complete: value.complete === true } : null;
  } catch {
    return null;
  }
}
