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

  it('starts with column headers and eight empty rows', async () => {
    await open();

    expect([...el.querySelectorAll('.cols span')].map((s) => s.textContent)).toEqual(['Team', 'Conf', 'Div', 'Pos', 'Ht', 'Age', '#']);
    expect(el.querySelectorAll('.row.blank').length).toBe(8);
    expect(el.querySelector('.row.blank.next')?.textContent).toContain('Mystery player');
    expect(el.querySelectorAll('.row:not(.blank)').length).toBe(0);
  });

  it('shows each guess as a row of coloured clues, oldest first', async () => {
    await open(
      gameState({
        guesses: [
          guess('Nikola Jokić', false, feedback({ conference: { value: 'West', state: 'miss' } })),
          guess('Jalen Brunson', false, feedback({ team: { value: 'NYK', state: 'close', label: 'Knicks', image: 'https://logos/nyk.svg' } })),
        ],
      }),
    );

    const rows = [...el.querySelectorAll('.row:not(.blank)')];
    expect(rows.map((r) => r.querySelector('b')?.textContent)).toEqual(['Nikola Jokić', 'Jalen Brunson']);
    expect(el.querySelectorAll('.row.blank').length).toBe(6);
    expect(el.querySelector('.row.blank.next')?.textContent).toContain('Guess 3');

    const cells = [...rows[1].querySelectorAll('.cell')];
    expect(cells.map((c) => ['ok', 'close', 'miss'].find((s) => c.classList.contains(s)))).toEqual(['close', 'ok', 'ok', 'close', 'miss', 'close', 'miss']);
    expect(cells[0].querySelector('img')?.getAttribute('src')).toBe('https://logos/nyk.svg');
    expect(cells[0].querySelector('.visually-hidden')?.textContent).toBe('Team: Knicks, close');
    expect(cells[2].textContent).toContain('ATL'); // Atlantic, shortened to fit
    expect(cells[4].querySelector('.arrow.up')?.textContent).toBe('▲');
    expect(cells[4].querySelector('.visually-hidden')?.textContent).toBe('Height: 6′2″, wrong, the answer is higher');
    expect(cells[5].querySelector('.arrow.down')).not.toBeNull();
  });

  it('shows the team abbreviation when there is no logo or it fails to load', async () => {
    await open(gameState({ guesses: [guess('Jalen Brunson', false, feedback({ team: { value: 'NYK', state: 'miss', image: 'https://logos/nyk.svg' } }))] }));

    const team = () => el.querySelector('.row:not(.blank) .cell')!;
    team().querySelector('img')!.dispatchEvent(new Event('error'));
    await fixture.whenStable();
    expect(team().querySelector('img')).toBeNull();
    expect(team().querySelector('.value')?.textContent).toBe('NYK');
  });

  it('turns over only the guesses made after the puzzle loaded', async () => {
    await open(gameState({ guesses: [guess('Nikola Jokić', false, feedback())] }));

    expect(el.querySelector('.row.fresh')).toBeNull();
  });

  it('describes the answer once the game is over and drops the empty rows', async () => {
    await open(
      gameState({
        status: 'won',
        guesses: [guess('Jayson Tatum', true, feedback())],
        answer: { playerId: 1, name: 'Jayson Tatum', teamName: 'Celtics', position: 'F-G', height: '6′8″', age: 28, number: '0' } as never,
      }),
    );

    expect(el.querySelector('.facts')?.textContent).toBe('Celtics · F-G · 6′8″ · 28 years old · #0');
    expect(el.querySelector('.row.right')).not.toBeNull();
    expect(el.querySelectorAll('.row.blank').length).toBe(0);
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
