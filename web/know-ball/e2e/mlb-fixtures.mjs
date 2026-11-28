// Writes a small made-up MLB league in the layout of MLB's Stats API, as MlbStatsHistorySource and
// MlbStatsRosterSource read it from disk, for the browser tests: enough current players to lose a game of Scouting
// Report and enough well-travelled ones for Career Path. Each player spends each season with one club, so no
// traded season needs the year-by-year lines. The seasons run up to this year, so the importer reads them as recent
// whatever the date.
// Usage: node mlb-fixtures.mjs <out-dir>; prints the first season, for Sources__MlbStats__FirstSeason.
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const out = process.argv[2];
if (!out) throw new Error('usage: node mlb-fixtures.mjs <out-dir>');

const year = new Date().getUTCFullYear();
const seasons = [year - 4, year - 3, year - 2, year - 1, year, year + 1];

const AL = { id: 103, name: 'American League' };
const NL = { id: 104, name: 'National League' };
// Id, city, club, abbreviation, league, division.
const teams = [
  [147, 'New York', 'Yankees', 'NYY', AL, 'East'], [111, 'Boston', 'Red Sox', 'BOS', AL, 'East'],
  [145, 'Chicago', 'White Sox', 'CWS', AL, 'Central'], [117, 'Houston', 'Astros', 'HOU', AL, 'West'],
  [121, 'New York', 'Mets', 'NYM', NL, 'East'], [143, 'Philadelphia', 'Phillies', 'PHI', NL, 'East'],
  [112, 'Chicago', 'Cubs', 'CHC', NL, 'Central'], [119, 'Los Angeles', 'Dodgers', 'LAD', NL, 'West'],
  [137, 'San Francisco', 'Giants', 'SF', NL, 'West'], [136, 'Seattle', 'Mariners', 'SEA', AL, 'West'],
];
const teamId = Object.fromEntries(teams.map(([id, , , abbr]) => [abbr, id]));

// Name, position, birth year, jersey, then the club each of the first five seasons (one per season, oldest first).
const players = [
  ['Rocco Longball', 'RF', 1995, '99', ['NYY', 'NYY', 'NYY', 'NYY', 'NYY']],
  ['Gus Fireballer', 'P', 1993, '45', ['SEA', 'HOU', 'BOS', 'NYM', 'NYM']],
  ['Milo Wanderer', 'CF', 1992, '8', ['CHC', 'SF', 'PHI', 'LAD', 'LAD']],
  ['Hank Slider', 'P', 1994, '33', ['CWS', 'CWS', 'SEA', 'SF', 'CHC']],
  ['Tito Glovework', 'SS', 1997, '2', ['HOU', 'HOU', 'HOU', 'HOU', 'HOU']],
  ['Skip Basepath', '2B', 1996, '4', ['PHI', 'NYM', 'BOS', 'BOS', 'BOS']],
  ['Dex Backstop', 'C', 1994, '24', ['LAD', 'LAD', 'LAD', 'LAD', 'LAD']],
  ['Moe Corner', '3B', 1995, '13', ['SF', 'SF', 'SF', 'SF', 'SF']],
  ['Lenny Firstbase', '1B', 1993, '44', ['BOS', 'CHC', 'CWS', 'NYY', 'NYY']],
  ['Ace Closer', 'P', 1996, '38', ['PHI', 'PHI', 'PHI', 'PHI', 'PHI']],
  ['Buzz Leftfield', 'LF', 1998, '7', ['SEA', 'SEA', 'SEA', 'SEA', 'SEA']],
  ['Ziggy Knuckler', 'P', 1991, '49', ['NYM', 'LAD', 'HOU', 'CWS', 'CWS']],
  ['Cal Utility', '2B', 1995, '15', ['CHC', 'CHC', 'CHC', 'CHC', 'CHC']],
  ['Rex Bigswing', '1B', 1994, '25', ['CWS', 'CWS', 'CWS', 'CWS', 'CWS']],
];
const pitcher = (pos) => pos === 'P';
const starter = (i) => i % 3 !== 0;
const teamOf = (p, s) => p[4][Math.min(s, 4)];

const write = (path, obj) => {
  mkdirSync(join(out, path, '..'), { recursive: true });
  writeFileSync(join(out, path), JSON.stringify(obj));
};

const teamJson = ([id, city, club, abbr, league, division]) => ({
  id, name: `${city} ${club}`, clubName: club, teamName: club, abbreviation: abbr, league,
  division: { id: 1, name: `${league.name} ${division}` }, venue: { id: 1, name: `${club} Park` }, sport: { id: 1 },
});
const person = ([name, pos, born, number], i, s) => ({
  id: 9001 + i, fullName: name, primaryNumber: number, birthDate: `${born}-05-0${(i % 9) + 1}`,
  birthCity: i % 4 === 0 ? 'Santo Domingo' : 'Springfield', birthCountry: i % 4 === 0 ? 'Dominican Republic' : 'USA',
  ...(i % 4 === 0 ? {} : { birthStateProvince: 'IL' }), height: `6' ${i % 5}"`, weight: 200 + i, active: true,
  primaryPosition: { abbreviation: pos }, mlbDebutDate: `${seasons[0]}-04-01`, currentTeam: { id: teamId[teamOf(players[i], s)] },
});
const split = (i, s, stat, position) => ({
  season: String(seasons[s]), stat, team: { id: teamId[teamOf(players[i], s)] },
  player: { id: 9001 + i, fullName: players[i][0] }, numTeams: 1, ...(position ? { position: { abbreviation: position } } : {}),
});
const page = (rows) => ({ stats: [{ totalSplits: rows.length, splits: rows }] });

for (const [s, season] of seasons.entries()) {
  write(`teams/${season}.json`, { teams: teams.map(teamJson) });
  write(`players/${season}.json`, { people: players.map((p, i) => person(p, i, s)) });
  const n = (i) => 1 + ((s + i) % 4);
  write(`stats/hitting/${season}.json`, page(players.flatMap((p, i) => pitcher(p[1]) ? [] : [split(i, s, {
    gamesPlayed: 140 + n(i), atBats: 500 + 10 * n(i), hits: 130 + 8 * n(i), homeRuns: 15 + 4 * n(i), rbi: 60 + 9 * n(i),
    runs: 70 + 5 * n(i), doubles: 25 + n(i), triples: n(i) % 3, stolenBases: 5 + 3 * n(i), baseOnBalls: 50 + n(i), strikeOuts: 120 - n(i),
  })])));
  write(`stats/pitching/${season}.json`, page(players.flatMap((p, i) => !pitcher(p[1]) ? [] : [split(i, s, starter(i)
    ? { gamesPitched: 30, gamesStarted: 30, wins: 10 + n(i), losses: 8, outs: 540, earnedRuns: 70 - n(i), hits: 160, baseOnBalls: 50, strikeOuts: 180 + 10 * n(i) }
    : { gamesPitched: 65, gamesStarted: 0, wins: 4, losses: 3, saves: 25 + n(i), outs: 195, earnedRuns: 20, hits: 50, baseOnBalls: 20, strikeOuts: 75 + n(i) })])));
  write(`stats/fielding/${season}.json`, page(players.map((p, i) => split(i, s, { games: pitcher(p[1]) ? (starter(i) ? 30 : 65) : 140 }, p[1]))));
  write(`standings/${season}.json`, {
    records: [{ teamRecords: teams.map(([id], t) => ({ team: { id }, wins: 70 + 2 * t, losses: 92 - 2 * t, clinched: false, divisionChamp: false })) }],
  });
}

// The 40-man rosters: every made-up player is on the club he ended with.
for (const [id, , , abbr] of teams) {
  const last = seasons.length - 1;
  write(`rosters/${id}.json`, {
    roster: players.flatMap((p, i) => teamOf(p, last) !== abbr ? [] : [{
      person: person(p, i, last), jerseyNumber: p[3], position: { abbreviation: p[1] }, status: { code: 'A' },
    }]),
  });
}

process.stdout.write(`${seasons[0]}\n`);
