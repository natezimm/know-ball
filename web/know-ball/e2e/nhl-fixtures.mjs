// Writes a small made-up NHL league as the answers of the NHL's stats and site APIs, for the browser tests: enough
// current players to lose a game of Scouting Report, enough well-travelled ones for Career Path, five seasons
// each for Back of the Card, and a Florida six (three forwards, two defencemen and a goalie) for Team Photo, whose
// dailies are the Stanley Cup Final teams: Florida played in three from 2022-23 to 2024-25. The seasons run
// up to the one being played now, so the importer reads them as recent whatever the date.
// Usage: node nhl-fixtures.mjs <out-dir>; prints the first season, for Sources__Nhl__FirstSeason.
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const out = process.argv[2];
if (!out) throw new Error('usage: node nhl-fixtures.mjs <out-dir>');

const teamFile = resolve(dirname(fileURLToPath(import.meta.url)), '../../../src/KnowBall.Infrastructure/Data/nhl-teams.json');
const current = JSON.parse(readFileSync(teamFile, 'utf8')).current;
const fullName = Object.fromEntries(current.map((t) => [t.key, t.fullName]));

// An NHL season starts in October and is named for the year it starts.
const now = new Date();
const latest = now.getUTCMonth() >= 9 ? now.getUTCFullYear() : now.getUTCFullYear() - 1;
const seasons = [latest - 4, latest - 3, latest - 2, latest - 1, latest];

// Name, position, height, sweater, birth year, country, then the team each season (oldest first).
const players = [
  ['Sunrise Winger', 'L', 72, 19, 1997, 'USA', ['FLA', 'FLA', 'FLA', 'FLA', 'FLA']],
  ['Sunrise Pivot', 'C', 74, 16, 1995, 'FIN', ['FLA', 'FLA', 'FLA', 'FLA', 'FLA']],
  ['Sunrise Flank', 'R', 73, 13, 1995, 'CAN', ['FLA', 'FLA', 'FLA', 'FLA', 'FLA']],
  ['Sunrise Anchor', 'D', 75, 42, 1996, 'SWE', ['FLA', 'FLA', 'FLA', 'FLA', 'FLA']],
  ['Sunrise Partner', 'D', 74, 62, 1994, 'CAN', ['FLA', 'FLA', 'FLA', 'FLA', 'FLA']],
  ['Sunrise Crease', 'G', 74, 72, 1988, 'RUS', ['FLA', 'FLA', 'FLA', 'FLA', 'FLA']],
  ['Blade Runner', 'C', 72, 97, 1997, 'CAN', ['EDM', 'EDM', 'EDM', 'EDM', 'EDM']],
  ['Wrist Shot', 'L', 71, 8, 1994, 'RUS', ['WSH', 'WSH', 'WSH', 'WSH', 'WSH']],
  ['Top Shelf', 'R', 73, 86, 1993, 'USA', ['TBL', 'TBL', 'TBL', 'TBL', 'TBL']],
  ['Blue Line', 'D', 75, 8, 1998, 'USA', ['COL', 'COL', 'COL', 'COL', 'COL']],
  ['Glove Save', 'G', 76, 31, 1995, 'FIN', ['NJD', 'NJD', 'NJD', 'NJD', 'NJD']],
  ['Hat Trick', 'C', 74, 34, 1997, 'USA', ['TOR', 'TOR', 'TOR', 'TOR', 'TOR']],
  ['Slap Shot', 'R', 72, 16, 1999, 'SWE', ['MIN', 'MIN', 'MIN', 'MIN', 'MIN']],
  ['Back Check', 'L', 70, 21, 1996, 'CZE', ['BOS', 'BOS', 'BOS', 'BOS', 'BOS']],
  ['Power Play', 'C', 71, 40, 1995, 'FIN', ['CAR', 'NYR', 'NYR', 'PIT', 'VGK']],
  ['Road Trip', 'R', 73, 12, 1992, 'CAN', ['ANA', 'SJS', 'SEA', 'CHI', 'NSH']],
  ['Ice Time', 'D', 74, 5, 1993, 'SWE', ['MTL', 'OTT', 'BUF', 'DET', 'CGY']],
  ['Five Hole', 'G', 77, 30, 1994, 'SVK', ['WPG', 'STL', 'DAL', 'LAK', 'UTA']],
];

const id = (i) => 8100001 + i;
const L = (s) => ({ default: s });
const seasonId = (s) => s * 10000 + s + 1;
const write = (path, value) => {
  mkdirSync(join(out, path, '..'), { recursive: true });
  writeFileSync(join(out, path), JSON.stringify(value));
};

for (const [i, [name, pos, height, sweater, born, country, teams]] of players.entries()) {
  const [first, last] = name.split(' ');
  const goalie = pos === 'G';
  const seasonTotals = seasons.map((season, s) => {
    const n = 1 + ((s + i) % 4);
    const line = { season: seasonId(season), gameTypeId: 2, leagueAbbrev: 'NHL', teamName: L(fullName[teams[s]]), sequence: 1, gamesPlayed: 60 + n * 5 };
    return goalie
      ? { ...line, gamesStarted: 55 + n * 4, wins: 28 + n * 2, losses: 20 - n, otLosses: 4 + (n % 2), goalsAgainst: 150 + n, shotsAgainst: 1700 + 30 * n, shutouts: n, timeOnIce: `${3400 + 60 * n}:00` }
      : { ...line, goals: 15 + 5 * n, assists: 25 + 6 * n, points: 40 + 11 * n, plusMinus: n * 3 - 4, pim: 10 + 2 * n, powerPlayGoals: 3 + n, gameWinningGoals: n, shots: 150 + 20 * n, avgToi: '18:30' };
  });
  write(`players/${id(i)}.json`, {
    playerId: id(i), firstName: L(first), lastName: L(last), position: pos, sweaterNumber: sweater, heightInInches: height, weightInPounds: 195,
    birthDate: `${born}-0${(i % 9) + 1}-1${i % 9}`, birthCountry: country, inHHOF: 0, headshot: '',
    draftDetails: { year: born + 18, overallPick: 1 + i * 3 },
    careerTotals: { regularSeason: { points: goalie ? 0 : seasonTotals.reduce((t, l) => t + l.points, 0), wins: goalie ? seasonTotals.reduce((t, l) => t + l.wins, 0) : 0 } },
    seasonTotals, awards: [],
  });
}

const rows = (goalies) => players.flatMap((p, i) => ((p[1] === 'G') === goalies ? [{ playerId: id(i), gamesPlayed: 330 }] : []));
write('stats/skaters.json', { data: rows(false), total: rows(false).length });
write('stats/goalies.json', { data: rows(true), total: rows(true).length });

// A record for every team that has a player in the season, so each one has a season to show.
for (const [s, season] of seasons.entries()) {
  const teams = [...new Set(players.map((p) => p[6][s]))].sort();
  write(`stats/teams-${season}-2.json`, { data: teams.map((t, i) => ({ teamFullName: fullName[t], gamesPlayed: 82, wins: 40 + i, losses: 30 - i, otLosses: 12, ties: null })) });
  write(`stats/teams-${season}-3.json`, { data: teams.slice(0, 4).map((t) => ({ teamFullName: fullName[t], gamesPlayed: 6, wins: 3, losses: 3 })) });
}

const groups = { C: 'forwards', L: 'forwards', R: 'forwards', D: 'defensemen', G: 'goalies' };
for (const team of current) {
  const roster = { forwards: [], defensemen: [], goalies: [] };
  for (const [i, [name, pos, height, sweater, born, country, teams]] of players.entries()) {
    if (teams.at(-1) !== team.key) continue;
    const [first, last] = name.split(' ');
    roster[groups[pos]].push({ id: id(i), firstName: L(first), lastName: L(last), positionCode: pos, sweaterNumber: sweater, heightInInches: height,
      weightInPounds: 195, birthDate: `${born}-0${(i % 9) + 1}-1${i % 9}`, birthCountry: country, headshot: '' });
  }
  write(`rosters/${team.key}.json`, roster);
}

process.stdout.write(`${seasons[0]}\n`);
