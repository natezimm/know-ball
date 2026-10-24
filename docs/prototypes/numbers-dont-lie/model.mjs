export const leagues = ['nba', 'nfl', 'mlb', 'nhl'];
export const preferenceKey = 'knowball.numbers.prototype.leagues.v1';

export function readPreferences(storage) {
  try {
    const saved = JSON.parse(storage.getItem(preferenceKey));
    if (!Array.isArray(saved)) return [...leagues];
    const valid = leagues.filter(league => saved.includes(league));
    return valid.length ? valid : [...leagues];
  } catch { return [...leagues]; }
}

export function dailySet(questions, date) {
  if (date) {
    let seed = [...date].reduce((n, c) => (n * 31 + c.charCodeAt(0)) >>> 0, 7);
    return practiceSet(questions, leagues, () => ((seed = (1664525 * seed + 1013904223) >>> 0) / 4294967296));
  }
  // Fixed editorial preview, identical for every visitor. Not a date scheduler.
  const ids = ['nba-points-2024', 'nfl-passing-2023', 'mlb-homeRuns-2022', 'nhl-goals-2024', 'nba-threes-2024'];
  return ids.map(id => questions.find(q => q.id === id));
}

export function practiceSet(questions, selected, random = Math.random, filters = {}) {
  questions = questions.filter(q => (!filters.topics?.length || filters.topics.includes(q.topic || 'stats')) && (!filters.eras?.length || filters.eras.includes(era(q))));
  if (!selected.length || selected.some(l => !leagues.includes(l))) throw new Error('Choose at least one supported league.');
  const shuffled = values => {
    const copy = [...values];
    for (let i = copy.length - 1; i > 0; i--) {
      const j = Math.floor(random() * (i + 1));
      [copy[i], copy[j]] = [copy[j], copy[i]];
    }
    return copy;
  };
  const order = shuffled(leagues.filter(l => selected.includes(l)));
  const seen = filters.seen || [];
  const pools = Object.fromEntries(order.map(l => [l, shuffled(questions.filter(q => q.league === l)).sort((a,b) => Number(seen.includes(a.id)) - Number(seen.includes(b.id)))]));
  const round = [], topicsUsed = new Set();
  for (let i = 0; i < 5; i++) {
    const pool = pools[order[i % order.length]];
    let pos = pool.findIndex(q => !seen.includes(q.id) && !topicsUsed.has(q.topic || 'stats'));
    if (pos < 0) pos = 0;
    const q = pool.splice(pos, 1)[0];
    round.push(q); if (q) topicsUsed.add(q.topic || 'stats');
  }
  if (round.some(q => !q)) throw new Error('Not enough questions for this combination yet. Add topics or eras, or choose fewer leagues.');
  return shuffled(round).sort((a,b) => difficulty(a) - difficulty(b));
}

export const topics = ['stats', 'awards', 'teams', 'seasons'];
export const eras = ['Before 2000', '2000–2009', '2010–2019', '2020 onward'];
export const era = q => { const y = Number(q.season.slice(0,4)); return eras[y < 2000 ? 0 : y < 2010 ? 1 : y < 2020 ? 2 : 3]; };
const difficulty = q => ({approachable:0, medium:1, harder:2}[q.difficulty] ?? 1);
export function easternDate(now = new Date()) { return new Intl.DateTimeFormat('en-CA', {timeZone:'America/New_York',year:'numeric',month:'2-digit',day:'2-digit'}).format(now); }
export function completionStreak(dates, today) {
  const set = new Set(dates); let day = new Date(today + 'T12:00:00Z');
  if (!set.has(today)) day.setUTCDate(day.getUTCDate() - 1);
  let count = 0;
  while (set.has(day.toISOString().slice(0,10))) { count++; day.setUTCDate(day.getUTCDate() - 1); }
  return count;
}

export function score(answers) { return answers.filter(a => a.optionId === a.question.correctOptionId).length; }
