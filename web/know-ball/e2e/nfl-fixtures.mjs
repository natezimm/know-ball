// Writes a small made-up NFL league in the layout of the nflverse releases, for the browser tests:
// enough current players to lose a game of Scouting Report, enough well-travelled ones for Career Path, and five
// seasons of stats for Back of the Card.
// The seasons run up to this year, so the importer reads them as recent whatever the date.
// Usage: node nfl-fixtures.mjs <out-dir>; prints the first season, for Sources__Nflverse__FirstSeason.
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const out = process.argv[2];
if (!out) throw new Error('usage: node nfl-fixtures.mjs <out-dir>');

const year = new Date().getUTCFullYear();
const seasons = [year - 4, year - 3, year - 2, year - 1, year];

// Name, position, height, jersey, birth year, then the team each season (one per season, oldest first). Each
// player starts a season early with his first team, so the paths below keep the stops they had with four seasons.
const players = [
  ['Rex Launcher', 'QB', 76, '9', 1996, ['KC', 'KC', 'KC', 'KC']],
  ['Milo Gunslinger', 'QB', 75, '12', 1994, ['DEN', 'SEA', 'CHI', 'CHI']],
  ['Dash Burner', 'RB', 70, '22', 1998, ['SF', 'SF', 'SF', 'SF']],
  ['Hank Bulldozer', 'RB', 72, '28', 1995, ['DAL', 'PIT', 'NE', 'NE']],
  ['Skye Hands', 'WR', 73, '11', 1997, ['BUF', 'BUF', 'BUF', 'BUF']],
  ['Ziggy Route', 'WR', 71, '17', 1996, ['MIA', 'NYJ', 'LV', 'LV']],
  ['Tate Seam', 'TE', 77, '87', 1995, ['PHI', 'PHI', 'PHI', 'PHI']],
  ['Moose Anchor', 'T', 79, '72', 1994, ['GB', 'GB', 'GB', 'GB']],
  ['Brick Center', 'C', 76, '63', 1996, ['ARI', 'ARI', 'ARI', 'ARI']],
  ['Storm Rusher', 'DE', 77, '94', 1997, ['LA', 'LA', 'LA', 'LA']],
  ['Tank Plugger', 'DT', 75, '97', 1995, ['DET', 'DET', 'DET', 'DET']],
  ['Hawk Blitzer', 'OLB', 75, '55', 1996, ['BAL', 'BAL', 'BAL', 'BAL']],
  ['Lock Shutdown', 'CB', 71, '24', 1998, ['CIN', 'CIN', 'CIN', 'CIN']],
  ['Ace Ballhawk', 'FS', 72, '31', 1997, ['MIN', 'MIN', 'MIN', 'MIN']],
  ['Lefty Boot', 'K', 72, '3', 1993, ['TB', 'TB', 'TB', 'TB']],
];

for (const p of players) p[5].unshift(p[5][0]);

const id = (i) => `00-01${String(i + 1).padStart(5, '0')}`;
const csv = (rows) => rows.map((r) => r.join(',')).join('\n') + '\n';
const write = (path, rows) => {
  mkdirSync(join(out, path, '..'), { recursive: true });
  writeFileSync(join(out, path), csv(rows));
};

write('players/players.csv', [
  ['gsis_id', 'display_name', 'common_first_name', 'first_name', 'last_name', 'birth_date', 'position_group', 'position', 'height', 'weight',
    'headshot', 'college_name', 'jersey_number', 'rookie_season', 'last_season', 'latest_team', 'status', 'draft_year', 'draft_round', 'draft_pick', 'draft_team'],
  ...players.map(([name, pos, height, jersey, born, teams], i) => {
    const [first, last] = name.split(' ');
    return [id(i), name, first, first, last, `${born}-03-0${(i % 9) + 1}`, pos, pos, height, 220, '', 'State', jersey,
      seasons[0], seasons.at(-1), teams.at(-1), 'ACT', '', '', '', ''];
  }),
]);

// The importer needs the file; a league of undrafted players leaves it empty.
write('draft_picks/draft_picks.csv', [
  ['season', 'round', 'pick', 'team', 'gsis_id', 'pfr_player_name', 'hof', 'position', 'allpro', 'probowls', 'seasons_started', 'w_av', 'car_av', 'games'],
]);

for (const [s, season] of seasons.entries()) {
  write(`rosters/roster_${season}.csv`, [
    ['season', 'team', 'position', 'depth_chart_position', 'jersey_number', 'status', 'full_name', 'first_name', 'last_name', 'birth_date',
      'height', 'weight', 'college', 'gsis_id', 'headshot_url', 'football_name', 'rookie_year', 'entry_year', 'draft_number'],
    ...players.map(([name, pos, height, jersey, born, teams], i) => {
      const [first, last] = name.split(' ');
      return [season, teams[s], pos, pos, jersey, 'ACT', name, first, last, `${born}-03-0${(i % 9) + 1}`, height, 220, 'State', id(i), '',
        first, seasons[0], seasons[0], ''];
    }),
  ]);
}

// Season totals in the nflverse stats layout, by position group; linemen and kickers have none a card prints.
const statColumns = ['player_id', 'recent_team', 'games', 'completions', 'attempts', 'passing_yards', 'passing_tds', 'passing_interceptions',
  'carries', 'rushing_yards', 'rushing_tds', 'receptions', 'receiving_yards', 'receiving_tds',
  'def_tackles_solo', 'def_tackle_assists', 'def_sacks', 'def_interceptions', 'def_fumbles_forced', 'fantasy_points_ppr'];
const statsFor = (pos, s, i) => {
  const n = 1 + ((s + i) % 4);
  switch (pos) {
    case 'QB': return { completions: 300 + 10 * n, attempts: 480 + 5 * n, passing_yards: 3800 + 150 * n, passing_tds: 22 + n, passing_interceptions: 9 - n };
    case 'RB': return { carries: 220 + 10 * n, rushing_yards: 1000 + 60 * n, rushing_tds: 7 + n, receptions: 30 + n };
    case 'WR': case 'TE': return { receptions: 70 + 4 * n, receiving_yards: 900 + 70 * n, receiving_tds: 5 + n };
    case 'T': case 'C': case 'K': return null;
    default: return { def_tackles_solo: 40 + 3 * n, def_tackle_assists: 15 + n, def_sacks: n + 0.5, def_interceptions: n % 3, def_fumbles_forced: n % 2 };
  }
};
for (const [s, season] of seasons.entries()) {
  const rows = players.flatMap(([, pos, , , , teams], i) => {
    const stats = statsFor(pos, s, i);
    if (!stats) return [];
    const row = { player_id: id(i), recent_team: teams[s], games: 17, fantasy_points_ppr: 100, ...stats };
    return [statColumns.map((c) => row[c] ?? 0)];
  });
  write(`stats_player/stats_player_reg_${season}.csv`, [statColumns, ...rows]);
}

process.stdout.write(`${seasons[0]}\n`);
