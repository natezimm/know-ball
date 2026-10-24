import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { gameState, guess } from '../../core/testing';
import { CareerPath, CareerStop, stopYears } from './career-path';

const stops: CareerStop[] = [
  { team: 'Seattle SuperSonics', abbreviation: 'SEA', logoUrl: null, from: 2007, to: 2008, seasons: 1 },
  { team: 'Oklahoma City Thunder', abbreviation: 'OKC', logoUrl: 'https://logos/okc.svg', from: 2008, to: 2016, seasons: 8 },
  { team: 'Golden State Warriors', abbreviation: 'GSW', logoUrl: 'https://logos/gsw.svg', from: 2016, to: null, seasons: 4 },
];

const hints = (open: number) => ({
  hints: [
    { label: 'Position', value: open > 0 ? 'F-G' : null },
    { label: 'Drafted', value: open > 1 ? '2007, pick 2' : null },
    { label: 'College', value: open > 2 ? 'Texas' : null },
    { label: 'Initials', value: open > 3 ? 'K. D.' : null },
  ],
});

describe('Career Path', () => {
  let fixture: ComponentFixture<CareerPath>;
  let http: HttpTestingController;
  let el: HTMLElement;

  async function open(overrides = {}) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withXhr()), provideHttpClientTesting(), provideRouter([])] });
    fixture = TestBed.createComponent(CareerPath);
    http = TestBed.inject(HttpTestingController);
    el = fixture.nativeElement;
    await fixture.whenStable();
    http.expectOne('/api/career-path/daily').flush(gameState({ game: 'career-path', maxGuesses: 6, payload: { stops }, clues: hints(0), ...overrides }));
    await fixture.whenStable();
  }

  afterEach(() => http.verify());

  it('lists every stop in order with its years and length', async () => {
    await open();

    const rows = [...el.querySelectorAll('.rail li')];
    expect(rows.map((r) => r.querySelector('b')?.textContent)).toEqual(['Seattle SuperSonics', 'Oklahoma City Thunder', 'Golden State Warriors']);
    expect(rows.map((r) => r.querySelector('small')?.textContent)).toEqual(['2007–08 · 1 season', '2008–16 · 8 seasons', '2016– · 4 seasons']);
    expect(rows.map((r) => r.querySelector<HTMLElement>('.bar i')?.style.width)).toEqual(['12.5%', '100%', '50%']);
  });

  it('gives a team with no logo a lettered badge, and one whose logo fails to load too', async () => {
    await open();
    const logos = () => [...el.querySelectorAll('.logo')];

    expect(logos()[0].classList.contains('badge')).toBe(true);
    expect(logos()[0].textContent?.trim()).toBe('SEA');
    expect(logos()[1].querySelector('img')?.getAttribute('src')).toBe('https://logos/okc.svg');

    logos()[1].querySelector('img')!.dispatchEvent(new Event('error'));
    await fixture.whenStable();
    expect(logos()[1].textContent?.trim()).toBe('OKC');
  });

  it('keeps hints as labelled stubs until they open', async () => {
    await open({ clues: hints(2) });

    const stubs = [...el.querySelectorAll('.hints li')];
    expect(stubs.map((s) => s.classList.contains('open'))).toEqual([true, true, false, false]);
    expect(stubs.map((s) => s.textContent?.replace(/\s+/g, ' ').trim())).toEqual(['PositionF-G', 'Drafted2007, pick 2', 'Hint 3College', 'Hint 4Initials']);
  });

  it('crosses out wrong guesses, newest first, and says which stops each one shared', async () => {
    await open({
      clues: hints(2),
      guesses: [guess('Nikola Jokić', false, { shared: [] }), guess('Klay Thompson', false, { shared: [2] }), guess('Jeff Green', false, { shared: [0, 1] })],
    });

    const misses = [...el.querySelectorAll('.misses li')];
    expect(misses.map((m) => m.querySelector('s')?.textContent)).toEqual(['Jeff Green', 'Klay Thompson', 'Nikola Jokić']);
    expect(misses.map((m) => m.querySelector('small')?.textContent)).toEqual(['Also played for SEA and OKC', 'Also played for GSW', 'None of these stops']);
  });

  it('mentions other players who share the path once the game is over', async () => {
    await open({ status: 'won', guesses: [guess('Kevin Durant', true, { shared: [0, 1, 2] })], clues: hints(4), answer: { playerId: 1, name: 'Kevin Durant', alsoAccepted: 1 } });

    expect(el.querySelector('.also')?.textContent).toBe('1 other player shares this exact path.');
    expect(el.querySelectorAll('.misses li').length).toBe(0);
  });

  it('writes a stop as a span of years', () => {
    expect(stopYears({ from: 2011, to: 2017 })).toBe('2011–17');
    expect(stopYears({ from: 2008, to: 2009 })).toBe('2008–09');
    expect(stopYears({ from: 1998, to: 2004 })).toBe('1998–2004');
    expect(stopYears({ from: 2025, to: null })).toBe('2025–');
  });
});
