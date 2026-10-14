export interface MockGame {
  path: string;
  name: string;
}

export const MOCK_GAMES: MockGame[] = [
  { path: 'themes', name: 'Themes' },
  { path: 'stat-quiz', name: 'Stat Quiz' },
  { path: 'geo-sports', name: 'Geo Sports' },
  { path: 'sportle', name: 'Sportle' },
  { path: 'sound-it-out', name: 'Sound It Out' },
  { path: 'goat', name: "Who's the GOAT?" },
];

/** Last two digits of each team's NBA id (1610612737 is Atlanta). */
const TEAM_IDS: Record<string, number> = {
  ATL: 37, BOS: 38, CLE: 39, NOP: 40, CHI: 41, DAL: 42, DEN: 43, GSW: 44, HOU: 45, LAC: 46,
  LAL: 47, MIA: 48, MIL: 49, MIN: 50, BKN: 51, NYK: 52, ORL: 53, IND: 54, PHI: 55, PHX: 56,
  POR: 57, SAC: 58, SAS: 59, OKC: 60, TOR: 61, UTA: 62, MEM: 63, WAS: 64, DET: 65, CHA: 66,
};

// NBA CDN art, hot-linked for these sketches only.
export const logo = (abbr: string): string =>
  `https://cdn.nba.com/logos/nba/${1610612700 + TEAM_IDS[abbr]}/primary/L/logo.svg`;

export const headshot = (id: number, size: '260x190' | '1040x760' = '260x190'): string =>
  `https://cdn.nba.com/headshots/nba/latest/${size}/${id}.png`;
