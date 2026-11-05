import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TodayEntry } from '../../core/game-api';
import { Home, cardStatus } from './home';

const entry = (overrides: Partial<TodayEntry>): TodayEntry => ({
  game: 'scouting-report',
  name: 'Scouting Report',
  available: true,
  number: 12,
  status: 'new',
  guesses: 0,
  maxGuesses: 8,
  ...overrides,
});

describe('Home', () => {
  let stats: unknown[] = [];
  beforeEach(() => { stats = []; });

  async function render(today: TodayEntry[] | 'down') {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withXhr()), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(Home);
    const http = TestBed.inject(HttpTestingController);
    const request = http.expectOne('/api/me/today');
    if (today === 'down') request.flush('boom', { status: 500, statusText: 'Server Error' });
    else request.flush(today);
    http.expectOne('/api/me/stats').flush(stats);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  const badges = (el: HTMLElement) => [...el.querySelectorAll('.badge')].map((b) => b.textContent?.trim());

  it("shows the four games with today's state", async () => {
    const el = await render([
      entry({ status: 'playing', guesses: 3 }),
      entry({ game: 'career-path', name: 'Career Path', status: 'won', guesses: 2, maxGuesses: 6 }),
    ]);

    expect([...el.querySelectorAll('.grid h2')].map((h) => h.textContent)).toEqual(['Spordle', 'Scouting Report', 'Career Path', "Numbers Don't Lie"]);
    expect(badges(el)).toEqual(['Play anytime', 'Carry on · 3 of 8', 'Solved in 2', "Play today's quiz"]);
    expect([...el.querySelectorAll('a.badge')].map((a) => a.getAttribute('href'))).toEqual(['/spordle', '/scouting-report', '/career-path', '/numbers-dont-lie']);
    expect(el.querySelector('h1')?.textContent).toContain('Four ways');
    expect(el.querySelector('.badge.done')?.textContent?.trim()).toBe('Solved in 2');
    // A daily won today turns foil; the others stay plain card stock.
    expect([...el.querySelectorAll('.card.kb-foil h2')].map((h) => h.textContent)).toEqual(['Career Path']);
  });

  it('names each kind of game and shows a running daily streak', async () => {
    stats = [{ game: 'career-path', played: 4, wins: 4, currentStreak: 3, bestStreak: 3, distribution: [0, 1, 2, 1, 0, 0] }];
    const el = await render([entry({}), entry({ game: 'career-path' })]);

    expect([...el.querySelectorAll('.no')].map((n) => n.textContent)).toEqual(['Word game', 'Daily', 'Daily', 'Daily quiz']);
    expect([...el.querySelectorAll('.streak')].map((n) => n.getAttribute('aria-label'))).toEqual(['3-day streak']);
  });

  it('counts four ways when every game has a puzzle', async () => {
    const el = await render([entry({}), entry({ game: 'career-path' })]);

    expect(el.querySelector('h1')?.textContent).toContain('Four ways');
    expect(badges(el)).toEqual(['Play anytime', 'Play No. 12', 'Play No. 12', "Play today's quiz"]);
  });

  it('still links to every game when the API is down', async () => {
    const el = await render('down');

    expect(el.querySelectorAll('a.badge').length).toBe(4);
  });

  it('waits for availability before offering daily games', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withXhr()), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(Home);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(badges(el)).toEqual(['Play anytime', 'Checking today…', 'Checking today…', "Play today's quiz"]);
    expect([...el.querySelectorAll('a.badge')].map((a) => a.getAttribute('href'))).toEqual(['/spordle', '/numbers-dont-lie']);

    TestBed.inject(HttpTestingController).expectOne('/api/me/today').flush([
      entry({}), entry({ game: 'career-path', available: false, number: null }),
    ]);
    await fixture.whenStable();
    expect(badges(el)).toEqual(['Play anytime', 'Play No. 12', 'No puzzle today', "Play today's quiz"]);
    expect(el.querySelector('a[href="/career-path"]')).toBeNull();
  });

  it('words each state of a card', () => {
    const daily = { daily: 'scouting-report' as const };
    expect(cardStatus(daily, entry({ status: 'won', guesses: 1 }), true)).toEqual({ label: 'Solved in 1 guess', open: true, done: true, foil: 'gold' });
    expect(cardStatus(daily, entry({ status: 'won', guesses: 4 }), true).foil).toBe('holo');
    expect(cardStatus(daily, entry({ status: 'lost', guesses: 8 }), true)).toEqual({ label: 'Missed · see the answer', open: true, done: true });
    expect(cardStatus(daily, undefined, true)).toEqual({ label: 'Coming soon', open: false });
    expect(cardStatus(daily, undefined, false)).toEqual({ label: 'Play today', open: true });
  });
});
