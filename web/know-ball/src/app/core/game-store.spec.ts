import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { GameStore } from './game-store';
import { gameState, guess } from './testing';

describe('GameStore', () => {
  let store: GameStore;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withXhr()), provideHttpClientTesting(), GameStore] });
    store = TestBed.inject(GameStore);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it("loads today's puzzle", () => {
    store.load('career-path');
    expect(store.phase()).toBe('loading');

    http.expectOne('/api/career-path/daily').flush(gameState({ game: 'career-path' }));

    expect(store.phase()).toBe('ready');
    expect(store.state()?.puzzleId).toBe(7);
    expect(store.over()).toBe(false);
  });

  it('loads a past puzzle by id', () => {
    store.load('scouting-report', 41);

    http.expectOne('/api/scouting-report/puzzles/41').flush(gameState({ puzzleId: 41 }));

    expect(store.state()?.puzzleId).toBe(41);
  });

  it('says why when there is no puzzle, and tells that apart from a failure', () => {
    store.load('career-path');
    http.expectOne('/api/career-path/daily').flush({ error: "There is no Career Path puzzle today." }, { status: 404, statusText: 'Not Found' });

    expect(store.phase()).toBe('unavailable');
    expect(store.reason()).toBe("There is no Career Path puzzle today.");

    store.load('career-path');
    http.expectOne('/api/career-path/daily').flush('boom', { status: 500, statusText: 'Server Error' });
    expect(store.phase()).toBe('error');

    store.retry();
    http.expectOne('/api/career-path/daily').flush(gameState({ game: 'career-path' }));
    expect(store.phase()).toBe('ready');
  });

  it('sends a guess and takes the judged puzzle back', () => {
    store.load('scouting-report');
    http.expectOne('/api/scouting-report/daily').flush(gameState());

    store.guess(30);
    expect(store.busy()).toBe(true);
    store.guess(31); // ignored while the first is in flight

    const request = http.expectOne('/api/scouting-report/7/guess');
    expect(request.request.body).toEqual({ playerId: 30 });
    request.flush(gameState({ guesses: [guess('Jalen Brunson'), guess('Jayson Tatum', true)], status: 'won', answer: { playerId: 1, name: 'Jayson Tatum' } }));

    expect(store.busy()).toBe(false);
    expect(store.over()).toBe(true);
    expect(store.misses()).toBe(1);

    store.guess(32); // the game is over
    http.expectNone('/api/scouting-report/7/guess');
  });

  it("shows the server's reason when a guess is refused, and keeps the game", () => {
    store.load('scouting-report');
    http.expectOne('/api/scouting-report/daily').flush(gameState());

    store.guess(30);
    http.expectOne('/api/scouting-report/7/guess').flush({ error: 'You already guessed Jalen Brunson.' }, { status: 400, statusText: 'Bad Request' });

    expect(store.notice()).toBe('You already guessed Jalen Brunson.');
    expect(store.phase()).toBe('ready');
    expect(store.busy()).toBe(false);
  });

  it('starts a practice round and does not fetch it again when the page moves to its address', () => {
    store.load('career-path');
    http.expectOne('/api/career-path/daily').flush(gameState({ game: 'career-path', status: 'won' }));

    let movedTo = 0;
    store.practice((id) => (movedTo = id));
    http.expectOne('/api/career-path/practice').flush(gameState({ game: 'career-path', puzzleId: 90, practice: true, number: null, date: null }));

    expect(movedTo).toBe(90);
    expect(store.state()?.practice).toBe(true);

    store.load('career-path', 90);
    http.expectNone('/api/career-path/puzzles/90');
  });
});
