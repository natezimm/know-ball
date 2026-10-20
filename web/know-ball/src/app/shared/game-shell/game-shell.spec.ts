import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { GameStore } from '../../core/game-store';
import { gameState, guess } from '../../core/testing';
import { GameShell } from './game-shell';

@Component({
  imports: [GameShell],
  providers: [GameStore],
  template: `
    <app-game-shell name="Scouting Report" practice [shareRows]="['🟩⬜']">
      <p help>How it works.</p>
      <span answer class="extra">Celtics</span>
      <p class="board">The board</p>
    </app-game-shell>
  `,
})
class Host {}

describe('GameShell', () => {
  let fixture: ComponentFixture<Host>;
  let http: HttpTestingController;
  let store: GameStore;
  let el: HTMLElement;

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    fixture = TestBed.createComponent(Host);
    http = TestBed.inject(HttpTestingController);
    store = fixture.debugElement.children[0].injector.get(GameStore);
    el = fixture.nativeElement;
    store.load('scouting-report');
    await fixture.whenStable();
  });

  afterEach(() => http.verify());

  async function flushDaily(state = gameState()) {
    http.expectOne('/api/scouting-report/daily').flush(state);
    await fixture.whenStable();
  }

  it('shows a loading note, then the puzzle number, the board and the guess box', async () => {
    expect(el.querySelector('.state')?.textContent).toContain('Loading');
    expect(el.querySelector('.board')).toBeNull();

    await flushDaily();

    expect(el.querySelector('h1')?.textContent).toBe('Scouting Report');
    expect(el.querySelector('.meta .kb-label')?.textContent).toContain('No. 12');
    expect(el.querySelector('.meta .kb-label')?.textContent).toContain('Oct 13');
    expect(el.querySelector('.board')?.textContent).toBe('The board');
    expect(el.querySelector('input')?.getAttribute('placeholder')).toBe('Guess 1 of 8: type a name');
    expect(el.querySelector('.result')).toBeNull();
  });

  it('marks one dot per guess used', async () => {
    await flushDaily(gameState({ guesses: [guess('A'), guess('B')] }));

    const dots = [...el.querySelectorAll('.dots i')].map((d) => d.className);
    expect(dots).toEqual(['miss', 'miss', '', '', '', '', '', '']);
    expect(el.querySelector('.dots')?.getAttribute('aria-label')).toBe('2 of 8 guesses used');
    expect(el.querySelector('input')?.getAttribute('placeholder')).toBe('Guess 3 of 8: type a name');
  });

  it('swaps the guess box for the answer when the game is won', async () => {
    await flushDaily(
      gameState({
        status: 'won',
        guesses: [guess('Jalen Brunson'), guess('Jayson Tatum', true)],
        answer: { playerId: 1, name: 'Jayson Tatum' },
        nextPuzzleAt: new Date(Date.now() + 3_600_000).toISOString(),
      }),
    );

    expect(el.querySelector('input')).toBeNull();
    expect(el.querySelector('.result')?.classList.contains('won')).toBe(true);
    expect(el.querySelector('.who .kb-label')?.textContent).toBe('Solved in 2 guesses');
    expect(el.querySelector('.who strong')?.textContent).toBe('Jayson Tatum');
    expect(el.querySelector('.who .extra')?.textContent).toBe('Celtics');
    expect(el.querySelector('.count')?.textContent).toMatch(/Next puzzle in\s+0\d:\d\d:\d\d/);
    expect([...el.querySelectorAll('.dots i')].map((d) => d.className).slice(0, 3)).toEqual(['miss', 'hit', '']);
    expect([...el.querySelectorAll('.next .kb-btn')].map((b) => b.textContent?.trim())).toEqual(['Share', 'Practice round']);
  });

  it('names the answer after a loss, and offers the way back from a practice round', async () => {
    await flushDaily(
      gameState({
        status: 'lost',
        practice: true,
        number: null,
        date: null,
        guesses: Array.from({ length: 8 }, (_, i) => guess(`Wrong ${i}`, false, null, i)),
        answer: { playerId: 1, name: 'Jayson Tatum' },
      }),
    );

    expect(el.querySelector('.meta .kb-label')?.textContent?.trim()).toBe('Practice round');
    expect(el.querySelector('.who .kb-label')?.textContent).toBe('The answer was');
    expect(el.querySelector('.result')?.classList.contains('won')).toBe(false);
    expect(el.querySelector('.next a')?.textContent).toBe("Today's puzzle");
  });

  it('explains when there is nothing to play', async () => {
    http.expectOne('/api/scouting-report/daily').flush({ error: 'There is no Scouting Report puzzle today.' }, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();

    expect(el.querySelector('.state')?.textContent).toContain('There is no Scouting Report puzzle today.');
    expect(el.querySelector('.board')).toBeNull();
  });

  it('shows statistics for this game with a bar per number of guesses', async () => {
    await flushDaily();
    el.querySelector<HTMLButtonElement>('[aria-label="Statistics"]')!.click();
    http.expectOne('/api/me/stats').flush([
      { game: 'career-path', name: 'Career Path', maxGuesses: 6, played: 9, wins: 9, currentStreak: 9, bestStreak: 9, distribution: [9, 0, 0, 0, 0, 0] },
      { game: 'scouting-report', name: 'Scouting Report', maxGuesses: 8, played: 4, wins: 3, currentStreak: 2, bestStreak: 3, distribution: [0, 1, 2, 0, 0, 0, 0, 0] },
    ]);
    await fixture.whenStable();

    expect([...el.querySelectorAll('.stats dd')].map((d) => d.textContent)).toEqual(['4', '75%', '2', '3']);
    const bars = [...el.querySelectorAll<HTMLElement>('.bars i')];
    expect(bars.map((b) => b.textContent)).toEqual(['0', '1', '2', '0', '0', '0', '0', '0']);
    expect(bars[2].style.width).toBe('100%');
    expect(bars[1].style.width).toBe('50%');
  });

  it('links every archive row to its explicit puzzle id, including the first row', async () => {
    await flushDaily();
    el.querySelector<HTMLButtonElement>('[aria-label="Past puzzles"]')!.click();
    http.expectOne('/api/scouting-report/archive').flush([
      { puzzleId: 7, number: 12, date: '2026-10-13', status: 'playing', guesses: 2 },
      { puzzleId: 6, number: 11, date: '2026-10-12', status: 'won', guesses: 4 },
      { puzzleId: 5, number: 10, date: '2026-10-11', status: 'lost', guesses: 8 },
      { puzzleId: 4, number: 9, date: '2026-10-10', status: 'new', guesses: 0 },
    ]);
    await fixture.whenStable();

    const rows = [...el.querySelectorAll<HTMLAnchorElement>('.archive a')];
    expect(rows.map((a) => a.getAttribute('href'))).toEqual([
      '/scouting-report/puzzle/7',
      '/scouting-report/puzzle/6',
      '/scouting-report/puzzle/5',
      '/scouting-report/puzzle/4',
    ]);
    expect(rows.map((a) => a.querySelector('em')?.textContent)).toEqual(['2 guessed', 'Solved in 4', 'Missed', 'Not played']);
  });
});
