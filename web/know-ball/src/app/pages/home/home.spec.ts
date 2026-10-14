import { provideHttpClient } from '@angular/common/http';
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
  async function render(today: TodayEntry[] | 'down') {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(Home);
    const request = TestBed.inject(HttpTestingController).expectOne('/api/me/today');
    if (today === 'down') request.flush('boom', { status: 500, statusText: 'Server Error' });
    else request.flush(today);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  const badges = (el: HTMLElement) => [...el.querySelectorAll('.badge')].map((b) => b.textContent?.trim());

  it("shows each game with today's state, and holds back one that has no puzzle", async () => {
    const el = await render([
      entry({ status: 'playing', guesses: 3 }),
      entry({ game: 'career-path', name: 'Career Path', status: 'won', guesses: 2, maxGuesses: 6 }),
      entry({ game: 'whos-that', name: "Who's That Sportsman?", available: false, number: null }),
    ]);

    expect([...el.querySelectorAll('h2')].map((h) => h.textContent)).toEqual(['Spordle', 'Scouting Report', 'Career Path', "Who's That Sportsman?"]);
    expect(badges(el)).toEqual(['Play anytime', 'Carry on · 3 of 8', 'Solved in 2', 'Coming soon']);
    expect([...el.querySelectorAll('a.badge')].map((a) => a.getAttribute('href'))).toEqual(['/spordle', '/scouting-report', '/career-path']);
    expect(el.querySelector('h1')?.textContent).toContain('Three ways');
    expect(el.querySelector('.badge.done')?.textContent?.trim()).toBe('Solved in 2');
  });

  it('counts four ways when every game has a puzzle', async () => {
    const el = await render([entry({}), entry({ game: 'career-path' }), entry({ game: 'whos-that', number: 4 })]);

    expect(el.querySelector('h1')?.textContent).toContain('Four ways');
    expect(badges(el)).toEqual(['Play anytime', 'Play No. 12', 'Play No. 12', 'Play No. 4']);
  });

  it('still links to every game when the API is down', async () => {
    const el = await render('down');

    expect(el.querySelectorAll('a.badge').length).toBe(4);
  });

  it('words each state of a card', () => {
    const daily = { daily: 'scouting-report' as const };
    expect(cardStatus(daily, entry({ status: 'won', guesses: 1 }), true).label).toBe('Solved in 1 guess');
    expect(cardStatus(daily, entry({ status: 'lost', guesses: 8 }), true)).toEqual({ label: 'Missed · see the answer', open: true, done: true });
    expect(cardStatus(daily, undefined, true)).toEqual({ label: 'Coming soon', open: false });
    expect(cardStatus(daily, undefined, false)).toEqual({ label: 'Play today', open: true });
  });
});
