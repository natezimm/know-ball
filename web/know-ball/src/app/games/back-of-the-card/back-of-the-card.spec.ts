import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { gameState, guess } from '../../core/testing';
import { BackOfTheCard, CardClues, CardSeason, cardTable, seasonLabel } from './back-of-the-card';

const seasons: CardSeason[] = [
  { season: 2007, games: 80, points: 20.3, rebounds: 4.4, assists: 2.4 },
  { season: 2013, games: 81, points: 32, rebounds: 7.4, assists: 5.5 },
  { season: 2019, games: 60, points: 26, rebounds: 7, assists: 5 },
];
const payload = { seasons, career: { seasons: 3, games: 1221, points: 26.1, rebounds: 6.3, assists: 4.3 } };
const labels = ['Teams', 'Position', 'Awards', 'Draft'];

/** The clues the API sends after this many misses. */
function clues(open: number): CardClues {
  return {
    steps: labels.map((label, i) => ({ label, open: i < open })),
    ...(open > 0 && { teams: ['SEA', 'OKC', 'GSW/MIA'] }),
    ...(open > 1 && { position: 'F-G', height: '6′11″' }),
    ...(open > 2 && { stamps: ['ROY', 'MVP', null] }),
    ...(open > 3 && { drafted: '2007, pick 2', origin: { label: 'College', value: 'Texas' } }),
  };
}

describe('Back of the Card', () => {
  let fixture: ComponentFixture<BackOfTheCard>;
  let http: HttpTestingController;
  let el: HTMLElement;

  async function open(overrides = {}) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withXhr()), provideHttpClientTesting(), provideRouter([])] });
    fixture = TestBed.createComponent(BackOfTheCard);
    http = TestBed.inject(HttpTestingController);
    el = fixture.nativeElement;
    await fixture.whenStable();
    http.expectOne('/api/back-of-the-card/daily').flush(gameState({ game: 'back-of-the-card', payload, clues: clues(0), ...overrides }));
    await fixture.whenStable();
  }

  const cells = (column: number) => [...el.querySelectorAll('tbody tr')].map((r) => r.children[column].textContent?.trim());

  afterEach(() => http.verify());

  it('prints every season with the name, clubs and bio covered', async () => {
    await open();

    expect(cells(0)).toEqual(['07–08', '13–14', '19–20']);
    expect(cells(3)).toEqual(['20.3', '32.0', '26.0']);
    expect(el.querySelectorAll('tbody .club .redact').length).toBe(3);
    expect(el.querySelector('.strip .redact')).not.toBeNull();
    expect(el.querySelector('tfoot')?.textContent).toContain('1,221');
    expect(el.querySelector('.fact.locked')).not.toBeNull();
    expect([...el.querySelectorAll('.ladder li')].map((li) => li.classList.contains('open'))).toEqual([false, false, false, false]);
  });

  it('shows each part of the card the misses have uncovered', async () => {
    await open({ clues: clues(3), guesses: [guess('Nikola Jokić', false, null, 1), guess('Jayson Tatum', false, null, 2), guess('Jalen Brunson', false, null, 3)] });

    expect([...el.querySelectorAll('tbody .club b')].map((b) => b.textContent)).toEqual(['SEA', 'OKC', 'GSW/MIA']);
    expect([...el.querySelectorAll('tbody .club')].map((c) => c.querySelector('.stamp')?.textContent ?? null)).toEqual(['ROY', 'MVP', null]);
    expect(el.querySelector('.bio')?.textContent).toContain('F-G');
    expect(el.querySelector('.bio')?.textContent).toContain('6′11″');
    expect(el.querySelector('.fact.locked')).not.toBeNull();
    expect([...el.querySelectorAll('.ladder li')].map((li) => li.classList.contains('open'))).toEqual([true, true, true, false]);
    expect(el.querySelector('.ladder .next')?.textContent).toContain('Draft');
    expect([...el.querySelectorAll('.misses s')].map((s) => s.textContent)).toEqual(['Jalen Brunson', 'Jayson Tatum', 'Nikola Jokić']);
  });

  it('names the player on the card once the game is over', async () => {
    await open({
      status: 'won', clues: clues(4), guesses: [guess('Kevin Durant', true)],
      answer: { playerId: 1, name: 'Kevin Durant', headshotUrl: null },
    });

    expect(el.querySelector('.ask')?.textContent).toBe("Kevin Durant's card");
    expect(el.querySelector('.strip .name')?.textContent).toBe('Kevin Durant');
    expect(el.querySelector('.fact')?.textContent).toContain('Texas');
    expect(el.querySelector('.kb-result-detail')?.textContent).toBe('F-G · 6′11″ · Drafted 2007, pick 2 · Texas');
  });

  it('labels a season across the century', () => {
    expect(seasonLabel(1999)).toBe('99–00');
    expect(seasonLabel(2005)).toBe('05–06');
  });
});

describe('cardTable', () => {
  it('prints a football card by year, with its own columns and decimals', () => {
    const table = cardTable({
      columns: [{ label: 'Cmp%', decimals: 1 }, { label: 'Yds', decimals: 0 }, { label: 'TD', decimals: 0 }, { label: 'Int', decimals: 0 }],
      seasons: [{ season: 2018, games: 16, values: [66, 5097, 50, 12] }],
      career: { seasons: 1, games: 16, values: [66, 5097, 50, 12] },
    });

    expect(table.headings).toEqual(['Cmp%', 'Yds', 'TD', 'Int']);
    expect(table.seasons[0]).toEqual({ season: 2018, label: '2018', games: 16, cells: ['66.0', '5,097', '50', '12'] });
    expect(table.career.cells).toEqual(['66.0', '5,097', '50', '12']);
  });

  it('prints a basketball card by season, per game', () => {
    const table = cardTable(payload);
    expect(table.headings).toEqual(['Pts', 'Reb', 'Ast']);
    expect(table.seasons.map((s) => s.label)).toEqual(['07–08', '13–14', '19–20']);
    expect(table.career.cells).toEqual(['26.1', '6.3', '4.3']);
  });
});
