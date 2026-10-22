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

export function dailySet(questions) {
  // Fixed editorial preview, identical for every visitor. Not a date scheduler.
  const ids = ['nba-points-2024', 'nfl-passing-2023', 'mlb-homeRuns-2022', 'nhl-goals-2024', 'nba-threes-2024'];
  return ids.map(id => questions.find(q => q.id === id));
}

export function practiceSet(questions, selected, random = Math.random) {
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
  const pools = Object.fromEntries(order.map(l => [l, shuffled(questions.filter(q => q.league === l))]));
  const round = Array.from({ length: 5 }, (_, i) => pools[order[i % order.length]].pop());
  if (round.some(q => !q)) throw new Error('Not enough questions for these leagues.');
  return shuffled(round);
}

export function score(answers) { return answers.filter(a => a.optionId === a.question.correctOptionId).length; }
