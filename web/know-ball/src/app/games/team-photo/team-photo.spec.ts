import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { GuessRecord } from '../../core/game-api';
import { gameState, guess } from '../../core/testing';
import { PhotoPayload, TeamPhoto, photoRows, seasonName, statLine } from './team-photo';

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
  });

  it('puts named starters in their spots and lets bench names go free', async () => {
    await open({ guesses: [starter('Stephen Curry', 3), guess('Kevin Durant', false, null, 7), bench('Andre Iguodala')] });

    expect(plates('.row.front')[0]).toContain('Curry');
    expect(el.querySelectorAll('.spot.found').length).toBe(1);
    expect(el.querySelector('.count')?.textContent).toBe('1 of 5 in the photo');
    expect(el.querySelector('.note')?.textContent).toContain('Andre Iguodala came off the bench · no miss');
    expect([...el.querySelectorAll('.bench li > span')].map((li) => li.textContent)).toEqual(['Andre Iguodala']);
    expect([...el.querySelectorAll('.misses s')].map((s) => s.textContent)).toEqual(['Kevin Durant']);
    // Only the miss uses a dot.
    expect(dots()).toEqual(['miss', '', '', '']);
  });

  it('fills the missing faces in grey after a loss', async () => {
    const misses = ['A', 'BB', 'CCC', 'DDDD'].map((n) => guess(n));
    await open({ status: 'lost', guesses: [starter('Stephen Curry', 3), ...misses], answer });

    expect(el.querySelectorAll('.spot.found').length).toBe(1);
    expect(el.querySelectorAll('.spot.missed').length).toBe(4);
    expect(plates('.row:not(.front)')[1]).toContain('Green');
    expect(el.querySelector('.kb-result-who strong')?.textContent).toBe('2015–16 Golden State Warriors');
    expect(el.querySelector('.kb-result-who .kb-label')?.textContent).toBe('Out of misses');
  });

  it('calls a win with no misses a full photo in gold', async () => {
    await open({ status: 'won', guesses: [...answer.slots.map((p, i) => starter(p.name, i)), bench('Andre Iguodala')], answer });

    expect(el.querySelectorAll('.spot.found').length).toBe(5);
    expect(el.querySelector('.kb-result-who .kb-label')?.textContent).toBe('Every name, no misses');
    expect(el.querySelector('.kb-result')?.getAttribute('data-tier')).toBe('gold');
  });
});

describe('Team Photo helpers', () => {
  it('names the season across the century', () => {
    expect(seasonName(1999)).toBe('1999–00');
    expect(seasonName(2015)).toBe('2015–16');
  });

  it('prints a stat line in two parts', () => {
    expect(statLine(slot('C', 'back', 5.4))).toEqual(['5.4 pts', '5.0 reb · 3.0 ast']);
  });

  it('keeps slot numbers when laying out the rows', () => {
    const rows = photoRows(payload, [starter('Klay Thompson', 4)], null);
    expect(rows.back.map((s) => s.index)).toEqual([0, 1, 2]);
    expect(rows.front.map((s) => [s.index, s.person?.name ?? null])).toEqual([[3, null], [4, 'Klay Thompson']]);
  });
});
