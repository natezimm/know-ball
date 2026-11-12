import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TodayEntry } from '../../core/game-api';
import { Home, cardStatus } from './home';

const entry = (overrides: Partial<TodayEntry>): TodayEntry => ({
  game: 'scouting-report',
  league: 'nba',
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

  async function render(today: TodayEntry[] | 'down', paused: string[] = []) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withXhr()), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(Home);
    const http = TestBed.inject(HttpTestingController);
    const request = http.expectOne('/api/me/today');
    if (today === 'down') request.flush('boom', { status: 500, statusText: 'Server Error' });
    else request.flush(today);
    http.expectOne('/api/me/stats').flush(stats);
    http.expectOne('/api/games/paused').flush(paused);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  const badges = (el: HTMLElement) => [...el.querySelectorAll('.badge')].map((b) => b.textContent?.trim());

  it("shows the six games with today's state", async () => {
    const el = await render([
      entry({ status: 'playing', guesses: 3 }),
      entry({ game: 'career-path', name: 'Career Path', status: 'won', guesses: 2, maxGuesses: 6 }),
      entry({ game: 'back-of-the-card', name: 'Back of the Card', number: 1 }),
    ]);

    expect([...el.querySelectorAll('.grid h2')].map((h) => h.textContent)).toEqual(['Letter Jacket', 'Scouting Report', 'Career Path', 'Back of the Card', 'Team Photo', "Numbers Don't Lie"]);
    expect(badges(el)).toEqual(['Play anytime', 'Carry on · 3 of 8', 'Solved in 2', 'Play No. 1', 'Coming soon', "Play today's quiz"]);
    expect([...el.querySelectorAll('a.badge')].map((a) => a.getAttribute('href'))).toEqual(['/letter-jacket', '/scouting-report', '/career-path', '/back-of-the-card', '/numbers-dont-lie']);
    // Team Photo has no puzzle in this list, so five games are open.
    expect(el.querySelector('h1')?.textContent).toContain('Five ways');
    expect(el.querySelector('.badge.done')?.textContent?.trim()).toBe('Solved in 2');
    // A daily won today turns foil; the others stay plain card stock.
    expect([...el.querySelectorAll('.card.kb-foil h2')].map((h) => h.textContent)).toEqual(['Career Path']);
  });

  it('names each kind of game and shows a running daily streak', async () => {
    stats = [{ game: 'career-path', played: 4, wins: 4, currentStreak: 3, bestStreak: 3, distribution: [0, 1, 2, 1, 0, 0] }];
    const el = await render([entry({}), entry({ game: 'career-path' }), entry({ game: 'back-of-the-card' })]);

    expect([...el.querySelectorAll('.no')].map((n) => n.textContent)).toEqual(['Word game', 'Daily', 'Daily', 'Daily', 'Daily', 'Daily quiz']);
    expect([...el.querySelectorAll('.streak')].map((n) => n.getAttribute('aria-label'))).toEqual(['3-day streak']);
  });

  it('counts six ways when every game has a puzzle', async () => {
    const el = await render([entry({}), entry({ game: 'career-path' }), entry({ game: 'back-of-the-card' }), entry({ game: 'team-photo', maxGuesses: 4 })]);

    expect(el.querySelector('h1')?.textContent).toContain('Six ways');
    expect(badges(el)).toEqual(['Play anytime', 'Play No. 12', 'Play No. 12', 'Play No. 12', 'Play No. 12', "Play today's quiz"]);
  });

  it('shows a paused game as taking a break, with no link', async () => {
    const el = await render([entry({}), entry({ game: 'career-path' }), entry({ game: 'back-of-the-card' }), entry({ game: 'team-photo' })], ['career-path', 'letter-jacket']);

    expect(badges(el)).toEqual(['Taking a break', 'Play No. 12', 'Taking a break', 'Play No. 12', 'Play No. 12', "Play today's quiz"]);
    expect(el.querySelector('a[href="/career-path"]')).toBeNull();
    expect(el.querySelector('h1')?.textContent).toContain('Four ways');
  });

  it('still links to every game when the API is down', async () => {
    const el = await render('down');

    expect(el.querySelectorAll('a.badge').length).toBe(6);
  });

  it('waits for availability before offering daily games', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withXhr()), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(Home);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(badges(el)).toEqual(['Play anytime', 'Checking today…', 'Checking today…', 'Checking today…', 'Checking today…', "Play today's quiz"]);
    expect([...el.querySelectorAll('a.badge')].map((a) => a.getAttribute('href'))).toEqual(['/letter-jacket', '/numbers-dont-lie']);

    TestBed.inject(HttpTestingController).expectOne('/api/me/today').flush([
      entry({}), entry({ game: 'career-path', available: false, number: null }), entry({ game: 'back-of-the-card' }), entry({ game: 'team-photo' }),
    ]);
    await fixture.whenStable();
    expect(badges(el)).toEqual(['Play anytime', 'Play No. 12', 'No puzzle today', 'Play No. 12', 'Play No. 12', "Play today's quiz"]);
    expect(el.querySelector('a[href="/career-path"]')).toBeNull();
  });

  it('shows an NFL badge with its own link once the NFL edition is live', async () => {
    const el = await render([
      entry({ status: 'won', guesses: 1 }),
      entry({ league: 'nfl', number: 2 }),
      entry({ game: 'career-path', name: 'Career Path', status: 'won', guesses: 1 }),
      entry({ game: 'career-path', name: 'Career Path', league: 'nfl', status: 'won', guesses: 1 }),
      entry({ game: 'back-of-the-card' }),
      entry({ game: 'team-photo' }),
    ]);

    expect(badges(el)).toEqual(['Play anytime', 'NBA · Solved in 1 guess', 'NFL · Play No. 2', 'NBA · Solved in 1 guess', 'NFL · Solved in 1 guess', 'Play No. 12', 'Play No. 12', "Play today's quiz"]);
    expect([...el.querySelectorAll('a.badge.edition')].map((a) => a.getAttribute('href'))).toEqual(['/scouting-report/nfl', '/career-path/nfl']);
    // Foil only once both editions are solved.
    expect([...el.querySelectorAll('.card.kb-foil h2')].map((h) => h.textContent)).toEqual(['Career Path']);
  });

  it('words each state of a card', () => {
    const daily = { daily: 'scouting-report' as const };
    expect(cardStatus(daily, entry({ status: 'won', guesses: 1 }), true)).toEqual({ label: 'Solved in 1 guess', open: true, done: true, foil: 'gold' });
    expect(cardStatus(daily, entry({ status: 'won', guesses: 4 }), true).foil).toBe('holo');
    expect(cardStatus(daily, entry({ status: 'lost', guesses: 8 }), true)).toEqual({ label: 'Missed · see the answer', open: true, done: true });
    expect(cardStatus(daily, undefined, true)).toEqual({ label: 'Coming soon', open: false });
    expect(cardStatus(daily, undefined, false)).toEqual({ label: 'Play today', open: true });
  });

  it('words Team Photo by misses, not guesses', () => {
    const photo = { daily: 'team-photo' as const, limitsMisses: true };
    const today = (o: Partial<TodayEntry>) => entry({ game: 'team-photo', maxGuesses: 4, ...o });
    expect(cardStatus(photo, today({ status: 'won', guesses: 0 }), true)).toEqual({ label: 'Full photo', open: true, done: true, foil: 'gold' });
    expect(cardStatus(photo, today({ status: 'won', guesses: 2 }), true).foil).toBe('holo');
    expect(cardStatus(photo, today({ status: 'playing', guesses: 1 }), true).label).toBe('Carry on · 1 of 4 misses');
    expect(cardStatus(photo, today({ status: 'lost', guesses: 4 }), true).label).toBe('Missed · see the answer');
  });
});
