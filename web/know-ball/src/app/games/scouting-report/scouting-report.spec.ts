import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { gameState, guess } from '../../core/testing';
import { ScoutingFeedback, ScoutingReport, shareRows } from './scouting-report';

const feedback = (overrides: Partial<ScoutingFeedback> = {}): ScoutingFeedback => ({
  team: { value: 'NYK', state: 'miss', label: 'Knicks' },
  conference: { value: 'East', state: 'ok' },
  division: { value: 'Atlantic', state: 'ok' },
  position: { value: 'G', state: 'close' },
  height: { value: '6′2″', state: 'miss', arrow: 'up' },
  age: { value: '30', state: 'close', arrow: 'down' },
  number: { value: '11', state: 'miss', arrow: 'down' },
  ...overrides,
});

describe('Scouting Report', () => {
  let fixture: ComponentFixture<ScoutingReport>;
  let http: HttpTestingController;
  let el: HTMLElement;

  async function open(state = gameState(), puzzleId?: string) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withXhr()), provideHttpClientTesting(), provideRouter([])] });
    fixture = TestBed.createComponent(ScoutingReport);
    if (puzzleId) fixture.componentRef.setInput('puzzleId', puzzleId);
    http = TestBed.inject(HttpTestingController);
    el = fixture.nativeElement;
    await fixture.whenStable();
    http.expectOne(puzzleId ? `/api/scouting-report/puzzles/${puzzleId}` : '/api/scouting-report/daily').flush(state);
    await fixture.whenStable();
  }

  afterEach(() => http.verify());

  it('starts with a blank card that names the seven clues', async () => {
    await open();

    expect([...el.querySelectorAll('.empty .chips small')].map((s) => s.textContent)).toEqual(['Team', 'Conf', 'Division', 'Pos', 'Height', 'Age', 'No.']);
    expect(el.querySelectorAll('.cards .card').length).toBe(0);
  });

  it('shows each guess as a card of coloured clues, newest first', async () => {
    await open(gameState({ guesses: [guess('Nikola Jokić', false, feedback({ conference: { value: 'West', state: 'miss' } })), guess('Jalen Brunson', false, feedback())] }));

    const cards = [...el.querySelectorAll('.cards .card')];
    expect(cards.map((c) => c.querySelector('b')?.textContent)).toEqual(['Jalen Brunson', 'Nikola Jokić']);
    expect(el.querySelector('.empty')).toBeNull();

    const chips = [...cards[0].querySelectorAll('.chips li')];
    expect(chips.map((c) => c.classList[0])).toEqual(['miss', 'ok', 'ok', 'close', 'miss', 'close', 'miss']);
    expect(chips[0].textContent).toContain('Knicks'); // the team's name, not its abbreviation
    expect(chips[4].textContent).toContain('↑');
    expect(chips[4].querySelector('.visually-hidden')?.textContent).toBe(', wrong, the answer is higher');
    expect(chips[1].querySelector('.visually-hidden')?.textContent).toBe(', right');
  });

  it('describes the answer once the game is over', async () => {
    await open(
      gameState({
        status: 'won',
        guesses: [guess('Jayson Tatum', true, feedback())],
        answer: { playerId: 1, name: 'Jayson Tatum', teamName: 'Celtics', position: 'F-G', height: '6′8″', age: 28, number: '0' } as never,
      }),
    );

    expect(el.querySelector('.facts')?.textContent).toBe('Celtics · F-G · 6′8″ · 28 years old · #0');
    expect(el.querySelector('.card.right')).not.toBeNull();
  });

  it('loads a past puzzle when the address names one', async () => {
    await open(gameState({ puzzleId: 41, number: 3 }), '41');

    expect(el.querySelector('.meta .kb-label')?.textContent).toContain('No. 3');
  });

  it('builds the share grid: a row of seven squares per guess, oldest first', () => {
    const rows = shareRows([
      guess('A', false, feedback()) as never,
      guess('B', true, feedback({ team: { value: 'BOS', state: 'ok' }, position: { value: 'F', state: 'ok' } })) as never,
    ]);

    expect(rows).toEqual(['⬜🟩🟩🟨⬜🟨⬜', '🟩🟩🟩🟩⬜🟨⬜']);
  });
});
