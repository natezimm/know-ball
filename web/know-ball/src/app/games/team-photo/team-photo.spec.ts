import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { GuessRecord } from '../../core/game-api';
import { gameState, guess } from '../../core/testing';
import { PhotoPayload, TeamPhoto, photoRows, seasonName, statLine, superBowl } from './team-photo';

const slot = (position: string, row: 'back' | 'front', points: number) => ({ position, row, games: 80, points, rebounds: 5, assists: 3 });
const payload: PhotoPayload = {
  team: { city: 'Golden State', name: 'Warriors', abbreviation: 'GSW', logoUrl: null },
  season: 2015,
  wins: 73,
  losses: 9,
  finals: 'lost',
  slots: [slot('SF', 'back', 11.7), slot('PF', 'back', 14), slot('C', 'back', 5.4), slot('PG', 'front', 30.1), slot('SG', 'front', 22.1)],
};
const starter = (name: string, index: number): GuessRecord => guess(name, true, { slot: index }, 100 + index);
const bench = (name: string): GuessRecord => ({ ...guess(name, false, { bench: true }, 50), miss: false });
const answer = {
  slots: ['Harrison Barnes', 'Draymond Green', 'Andrew Bogut', 'Stephen Curry', 'Klay Thompson'].map((name, i) => ({ playerId: 100 + i, name, headshotUrl: null })),
};

describe('Team Photo', () => {
  let fixture: ComponentFixture<TeamPhoto>;
  let http: HttpTestingController;
  let el: HTMLElement;

  async function open(overrides = {}) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withXhr()), provideHttpClientTesting(), provideRouter([])] });
    fixture = TestBed.createComponent(TeamPhoto);
    http = TestBed.inject(HttpTestingController);
    el = fixture.nativeElement;
    await fixture.whenStable();
    http.expectOne('/api/team-photo/daily').flush(gameState({ game: 'team-photo', maxGuesses: 4, payload, ...overrides }));
    await fixture.whenStable();
  }

  const plates = (row: string) => [...el.querySelectorAll(`${row} .plate`)].map((p) => p.textContent?.replace(/\s+/g, ' ').trim());
  const dots = () => [...el.querySelectorAll('.dots i')].map((i) => i.className);

  afterEach(() => http.verify());

  it('shows the plaque and a blank for each starter, with his position and line', async () => {
    await open();

    expect(el.querySelector('.plaque b')?.textContent).toBe('2015–16 Golden State Warriors');
    expect(el.querySelector('.plaque small')?.textContent).toBe('73–9 · Lost in the Finals');
    expect([...el.querySelectorAll('.row:not(.front) .pos')].map((p) => p.textContent)).toEqual(['SF', 'PF', 'C']);
    expect([...el.querySelectorAll('.row.front .pos')].map((p) => p.textContent)).toEqual(['PG', 'SG']);
    expect(plates('.row.front')[0]).toBe('30.1 pts5.0 reb · 3.0 ast');
    expect(el.querySelector('.count')?.textContent).toBe('0 of 5 in the photo');
    expect(dots()).toEqual(['', '', '', '']);
    expect(el.querySelector('input')?.getAttribute('placeholder')).toBe('Name a player · 4 misses left');
  });

  it('puts named starters in their spots and lets bench names go free', async () => {
    await open({ guesses: [starter('Stephen Curry', 3), guess('Kevin Durant', false, null, 7), bench('Andre Iguodala')] });

    // A named starter keeps the first half of his line under his name.
    expect(plates('.row.front')[0]).toMatch(/^Curry30\.1 pts/);
    expect(el.querySelectorAll('.spot.found').length).toBe(1);
    expect(el.querySelector('.count')?.textContent).toBe('1 of 5 in the photo');
    expect(el.querySelector('.note')?.textContent).toContain('Andre Iguodala came off the bench · no miss');
    expect([...el.querySelectorAll('.bench li > span')].map((li) => li.textContent)).toEqual(['Andre Iguodala']);
    expect([...el.querySelectorAll('.misses s')].map((s) => s.textContent)).toEqual(['Kevin Durant']);
    // Only the miss uses a dot.
    expect(dots()).toEqual(['miss', '', '', '']);
    expect(el.querySelector('input')?.getAttribute('placeholder')).toBe('Name a player · 3 misses left');
  });

  it('fills the missing faces in grey after a loss', async () => {
    const misses = ['A', 'BB', 'CCC', 'DDDD'].map((n) => guess(n));
    await open({ status: 'lost', guesses: [starter('Stephen Curry', 3), ...misses], answer });

    expect(el.querySelectorAll('.spot.found').length).toBe(1);
    expect(el.querySelectorAll('.spot.missed').length).toBe(4);
    expect(plates('.row:not(.front)')[1]).toContain('Green');
    expect(el.querySelector('.kb-result-who strong')?.textContent).toBe('2015–16 Golden State Warriors');
    expect(el.querySelector('.kb-result-who .kb-label')?.textContent).toBe('Out of misses');
    // The answer card says it all: no note about the last miss under it.
    expect(el.querySelector('.note')).toBeNull();
  });

  it('calls a win with no misses a full photo in gold', async () => {
    await open({ status: 'won', guesses: [...answer.slots.map((p, i) => starter(p.name, i)), bench('Andre Iguodala')], answer });

    expect(el.querySelectorAll('.spot.found').length).toBe(5);
    expect(el.querySelector('.kb-result-who .kb-label')?.textContent).toBe('Every name, no misses');
    expect(el.querySelector('.kb-result')?.getAttribute('data-tier')).toBe('gold');
  });
});

describe('Team Photo NFL', () => {
  const spot = (position: string, row: 'back' | 'front', stats: Record<string, number>) => ({ position, row, games: 16, stats });
  const chiefs: PhotoPayload = {
    team: { city: 'Kansas City', name: 'Chiefs', abbreviation: 'KC', logoUrl: null },
    season: 2019,
    wins: 12,
    losses: 4,
    ties: 0,
    finals: 'won',
    slots: [
      spot('WR', 'back', { rec: 58, wyd: 860, wtd: 7 }), spot('TE', 'back', { rec: 97, wyd: 1229, wtd: 5 }), spot('WR', 'back', { rec: 52, wyd: 673, wtd: 3 }),
      spot('QB', 'front', { pyd: 4031, ptd: 26, int: 5 }), spot('RB', 'front', { car: 111, ryd: 498, rtd: 5 }),
    ],
  };
  let el: HTMLElement;
  let http: HttpTestingController;

  async function open(p: PhotoPayload) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withXhr()), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(TeamPhoto);
    fixture.componentRef.setInput('league', 'nfl');
    http = TestBed.inject(HttpTestingController);
    el = fixture.nativeElement;
    await fixture.whenStable();
    http.expectOne('/api/team-photo/daily?league=nfl').flush(gameState({ game: 'team-photo', league: 'nfl', maxGuesses: 4, payload: p }));
    await fixture.whenStable();
  }

  afterEach(() => http.verify());

  it('names the season by its year and the Super Bowl it ended in, with the receivers at the back', async () => {
    await open(chiefs);

    expect(el.querySelector('.plaque b')?.textContent).toBe('2019 Kansas City Chiefs');
    expect(el.querySelector('.plaque small')?.textContent).toBe('12–4 · Won Super Bowl LIV');
    expect([...el.querySelectorAll('.row:not(.front) .pos')].map((p) => p.textContent)).toEqual(['WR', 'TE', 'WR']);
    expect([...el.querySelectorAll('.row.front .plate')].map((p) => p.textContent?.replace(/\s+/g, ' ').trim()))
      .toEqual(['4,031 pass yds26 TD · 5 INT', '498 rush yds111 car · 5 TD']);
    expect(el.querySelector('.help')?.textContent).toContain('two wide receivers and tight end');
  });

  it('shows a tie in the record, and a practice team as a playoff team', async () => {
    await open({ ...chiefs, season: 2012, wins: 11, losses: 4, ties: 1, finals: null });
    expect(el.querySelector('.plaque small')?.textContent).toBe('11–4–1 · Made the playoffs');
  });
});

describe('Team Photo helpers', () => {
  it('names the season across the century', () => {
    expect(seasonName(1999)).toBe('1999–00');
    expect(seasonName(2015)).toBe('2015–16');
  });

  it('prints a stat line in two parts', () => {
    expect(statLine(slot('C', 'back', 5.4))).toEqual(['5.4 pts', '5.0 reb · 3.0 ast']);
    expect(statLine({ position: 'TE', row: 'back', games: 16, stats: { rec: 97, wyd: 1229, wtd: 5 } })).toEqual(['1,229 rec yds', '97 rec · 5 TD']);
    expect(statLine({ position: 'QB', row: 'front', games: 16, stats: { pyd: 300 } })).toEqual(['300 pass yds', '0 TD · 0 INT']);
    expect(statLine({ position: '3B', row: 'middle', games: 155, stats: { avg: 0.292, hr: 39, rbi: 102 } })).toEqual(['.292 avg', '39 HR · 102 RBI']);
    expect(statLine({ position: 'SP', row: 'front', games: 32, stats: { w: 19, l: 5, era: 2.5, k: 197 } })).toEqual(['2.50 ERA', '19–5 · 197 K']);
  });

  it('numbers the Super Bowl from the season it closed', () => {
    expect(superBowl(1999)).toBe('XXXIV');
    expect(superBowl(2012)).toBe('XLVII');
    expect(superBowl(2019)).toBe('LIV');
    expect(superBowl(2025)).toBe('LX');
  });

  it('keeps slot numbers when laying out the rows', () => {
    const rows = photoRows(payload, [starter('Klay Thompson', 4)], null);
    expect(rows.back.map((s) => s.index)).toEqual([0, 1, 2]);
    expect(rows.front.map((s) => [s.index, s.person?.name ?? null])).toEqual([[3, null], [4, 'Klay Thompson']]);
    expect(rows.middle).toEqual([]);
  });

  it('stands a baseball infield in a middle row', () => {
    const spot = (position: string, row: 'back' | 'middle' | 'front') => ({ position, row, games: 150, stats: { avg: 0.3, hr: 20, rbi: 80 } });
    const baseball: PhotoPayload = {
      ...payload,
      slots: [spot('LF', 'back'), spot('CF', 'back'), spot('RF', 'back'), spot('3B', 'middle'), spot('SS', 'middle'), spot('2B', 'middle'),
        spot('1B', 'middle'), spot('SP', 'front'), spot('C', 'front')],
    };
    const rows = photoRows(baseball, [], null);
    expect([rows.back.length, rows.middle.length, rows.front.length]).toEqual([3, 4, 2]);
    expect(rows.middle.map((s) => s.slot.position)).toEqual(['3B', 'SS', '2B', '1B']);
  });
});
