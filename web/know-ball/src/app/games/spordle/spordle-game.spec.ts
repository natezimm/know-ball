import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { useMemoryStorage } from './memory-storage';
import { GuessResult } from './spordle-api';
import { SpordleGame } from './spordle-game';
import { getStats } from './spordle-stats';

describe('SpordleGame', () => {
  let game: SpordleGame;
  let http: HttpTestingController;

  const GUESSES = '/api/spordle/games/g1/guesses';
  const HINT = '/api/spordle/games/g1/hint';

  beforeEach(() => {
    useMemoryStorage();
    vi.useFakeTimers();
    TestBed.configureTestingModule({ providers: [SpordleGame, provideHttpClient(withXhr()), provideHttpClientTesting()] });
    game = TestBed.inject(SpordleGame);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  function startGame(gameId = 'g1'): void {
    game.start();
    const request = http.expectOne('/api/spordle/games');
    request.flush({ gameId, wordLength: request.request.body.wordLength, maxAttempts: 6 });
  }

  function type(word: string): void {
    [...word].forEach((letter) => game.press(letter));
  }

  /** Types a word, submits it, answers the request and lets the tiles finish flipping. */
  function guess(word: string, result: GuessResult): void {
    type(word);
    game.press('Enter');
    http.expectOne(GUESSES).flush(result);
    vi.runAllTimers();
  }

  const miss: GuessResult = {
    valid: true,
    score: ['absent', 'present', 'absent', 'absent', 'correct'],
    won: false,
    complete: false,
    attemptsRemaining: 5,
  };

  function reopen(): void {
    game = TestBed.runInInjectionContext(() => new SpordleGame());
    game.resume();
  }

  const saved = {
    gameId: 'g1', wordLength: 5, maxAttempts: 6,
    guesses: [{ requestId: 'first', word: 'punts', score: miss.score }],
    complete: false, won: false, answer: null, letterHint: { position: 3, letter: 'i' },
  };

  it('restores the server board, keyboard, hints and typed row after reopening', () => {
    startGame();
    guess('punts', miss);
    game.revealHint('category');
    http.expectOne(HINT).flush({ category: 'Football' });
    type('bl');
    reopen();
    http.expectOne('/api/spordle/games/g1').flush(saved);
    http.expectNone('/api/spordle/games');
    expect(game.status()).toBe('playing');
    expect(game.attempts()).toEqual([{ word: 'punts', score: miss.score }]);
    expect(game.letterStatuses()['s']).toBe('correct');
    expect(game.categoryHint()).toBe('Football');
    expect(game.letterHint()).toEqual({ position: 3, letter: 'i' });
    expect(game.currentGuess()).toBe('bl');
    expect(getStats(5).totalGames).toBe(0);
  });

  it('reconciles a committed guess whose response was lost before reload', () => {
    startGame();
    type('punts');
    game.press('Enter');
    const request = http.expectOne(GUESSES);
    const requestId = request.request.body.requestId;
    request.error(new ProgressEvent('error'));
    http.expectOne('/api/spordle/games/g1').error(new ProgressEvent('error')); // still offline
    reopen();
    http.expectOne('/api/spordle/games/g1').flush({ ...saved,
      guesses: [{ ...saved.guesses[0], requestId }],
    });
    expect(game.attempts()).toHaveLength(1);
    expect(game.currentGuess()).toBe('');
    expect(game.status()).toBe('playing');
  });

  it('retains a pending request ID across reload when the server did not commit it', () => {
    startGame();
    type('punts');
    game.press('Enter');
    const request = http.expectOne(GUESSES);
    const requestId = request.request.body.requestId;
    request.error(new ProgressEvent('error'));
    http.expectOne('/api/spordle/games/g1').error(new ProgressEvent('error')); // still offline
    reopen();
    http.expectOne('/api/spordle/games/g1').flush({ ...saved, guesses: [], letterHint: null });
    game.press('Enter');
    const retry = http.expectOne(GUESSES);
    expect(retry.request.body.requestId).toBe(requestId);
    retry.flush(miss);
    vi.runAllTimers();
    expect(game.attempts()).toHaveLength(1);
  });

  it('restores a completed round once even when reload interrupts the tile reveal', () => {
    startGame();
    type('blitz');
    game.press('Enter');
    http.expectOne(GUESSES).flush({ valid: true, score: Array(5).fill('correct'),
      complete: true, won: true, answer: 'blitz', attemptsRemaining: 5 });
    expect(game.status()).toBe('revealing');
    const completed = { ...saved, complete: true, won: true, answer: 'blitz',
      guesses: [{ requestId: 'win', word: 'blitz', score: Array(5).fill('correct') }] };
    reopen();
    http.expectOne('/api/spordle/games/g1').flush(completed);
    expect(game.status()).toBe('complete');
    expect(game.result()).toMatchObject({ won: true, answer: 'blitz', guesses: 1 });
    const time = game.result()!.timeMs;
    vi.advanceTimersByTime(10_000);
    reopen();
    http.expectOne('/api/spordle/games/g1').flush(completed);
    expect(game.result()!.timeMs).toBe(time);
    expect(getStats(5)).toMatchObject({ totalGames: 1, wins: 1, currentStreak: 1 });
  });

  it('keeps a saved round on temporary resume failure and expires it without a loss on 404', () => {
    startGame();
    guess('punts', miss);
    reopen();
    http.expectOne('/api/spordle/games/g1').flush(null, { status: 500, statusText: 'Server Error' });
    expect(game.status()).toBe('error');
    expect(localStorage.getItem('knowball.spordle.round')).not.toBeNull();
    http.expectNone('/api/spordle/games');
    reopen();
    http.expectOne('/api/spordle/games/g1').flush(null, { status: 404, statusText: 'Not Found' });
    expect(game.status()).toBe('expired');
    expect(localStorage.getItem('knowball.spordle.round')).toBeNull();
    expect(getStats(5).totalGames).toBe(0);
  });

  it('asks the server for a five-letter game by default', () => {
    game.start();
    expect(game.status()).toBe('loading');

    const request = http.expectOne('/api/spordle/games');
    expect(request.request.body).toEqual({ wordLength: 5 });
    request.flush({ gameId: 'g1', wordLength: 5, maxAttempts: 6 });

    expect(game.status()).toBe('playing');
    expect(game.gameId()).toBe('g1');
  });

  it('remembers the chosen word length for next time', () => {
    game.start(6);
    http.expectOne('/api/spordle/games').flush({ gameId: 'g1', wordLength: 6, maxAttempts: 6 });

    expect(game.wordLength()).toBe(6);
    expect(localStorage.getItem('knowball.spordle.wordLength')).toBe('6');
  });

  for (const action of ['guess', 'hint']) {
    it(`offers a fresh game after an expired ${action}, without recording a loss`, () => {
      startGame();
      guess('punts', miss);
      if (action === 'guess') {
        type('about');
        game.press('Enter');
      } else {
        game.revealHint('category');
      }
      http.expectOne(action === 'guess' ? GUESSES : HINT).flush({ error: 'Game not found or expired' }, { status: 404, statusText: 'Not Found' });
      expect(game.status()).toBe('expired');
      expect(game.attempts()).toHaveLength(1);
      expect(game.hintLoading()).toBe(false);
      expect(getStats(game.wordLength()).totalGames).toBe(0);
      game.newGame();
      http.expectOne('/api/spordle/games').flush({ gameId: 'g2', wordLength: 5, maxAttempts: 6 });
      expect(game.status()).toBe('playing');
      expect(game.attempts()).toHaveLength(0);
      expect(getStats(game.wordLength()).totalGames).toBe(0);
    });
  }

  it('reports a game that cannot start', () => {
    game.start();
    http.expectOne('/api/spordle/games').flush(null, { status: 500, statusText: 'Server Error' });

    expect(game.status()).toBe('error');
    expect(game.message()).toContain('Failed to start');
  });

  it('types letters up to the word length and deletes them', () => {
    startGame();

    type('Blitzzz');
    expect(game.currentGuess()).toBe('blitz');

    game.press('Backspace');
    game.press('1');
    game.press('Shift');
    expect(game.currentGuess()).toBe('blit');
  });

  it('does not submit a short guess', () => {
    startGame();
    type('dun');
    game.press('Enter');

    http.expectNone(GUESSES);
    expect(game.message()).toBe('Guess must be 5 letters.');
  });

  it('keeps the row when the word is not in the dictionary', () => {
    startGame();
    type('zzzzz');
    game.press('Enter');
    expect(game.status()).toBe('validating');

    http.expectOne(GUESSES).flush({ valid: false });

    expect(game.attempts()).toEqual([]);
    expect(game.currentGuess()).toBe('zzzzz');
    expect(game.status()).toBe('playing');
    expect(game.message()).toContain('Not in the word list');
  });

  it('adds a scored guess, then colours the keyboard once the tiles have flipped', () => {
    startGame();
    type('punts');
    game.press('Enter');
    const request = http.expectOne(GUESSES);
    expect(request.request.body).toEqual({ word: 'punts', requestId: expect.any(String) });
    request.flush(miss);

    expect(game.attempts()).toEqual([{ word: 'punts', score: miss.score }]);
    expect(game.currentGuess()).toBe('');
    expect(game.status()).toBe('revealing');
    expect(game.letterStatuses()).toEqual({});

    game.press('a'); // ignored while the tiles are flipping
    expect(game.currentGuess()).toBe('');

    vi.runAllTimers();

    expect(game.status()).toBe('playing');
    expect(game.letterStatuses()).toEqual({ p: 'absent', u: 'present', n: 'absent', t: 'absent', s: 'correct' });
    expect(game.inProgress()).toBe(true);
  });

  it('records a win with its time and number of guesses', () => {
    startGame();
    guess('punts', miss);
    vi.advanceTimersByTime(20_000);

    guess('blitz', {
      valid: true,
      score: ['correct', 'correct', 'correct', 'correct', 'correct'],
      won: true,
      complete: true,
      attemptsRemaining: 4,
      answer: 'blitz',
    });

    expect(game.status()).toBe('complete');
    expect(game.result()).toMatchObject({ won: true, answer: 'blitz', guesses: 2 });
    expect(game.result()!.timeMs).toBeGreaterThanOrEqual(20_000);
    expect(game.message()).toContain('Congratulations');
    expect(getStats(5)).toMatchObject({ totalGames: 1, wins: 1, currentStreak: 1, fewestGuesses: 2 });

    game.press('a'); // the board is closed
    expect(game.currentGuess()).toBe('');
  });

  it('shows the answer after the last miss and records a loss', () => {
    startGame();
    for (let left = 5; left > 0; left--) guess('punts', { ...miss, attemptsRemaining: left });
    guess('punts', { ...miss, complete: true, attemptsRemaining: 0, answer: 'blitz' });

    expect(game.status()).toBe('complete');
    expect(game.result()).toEqual({ won: false, answer: 'blitz', guesses: 6, timeMs: null });
    expect(game.message()).toBe('Game over! The word was BLITZ.');
    expect(getStats(5)).toMatchObject({ totalGames: 1, wins: 0 });
  });

  it('surfaces the server message when a guess is rejected', () => {
    startGame();
    type('punts');
    game.press('Enter');
    http.expectOne(GUESSES).flush({ error: 'Guess must be 5 letters' }, { status: 400, statusText: 'Bad Request' });

    expect(game.message()).toBe('Guess must be 5 letters');
    expect(game.status()).toBe('playing');
  });

  it('reuses the request ID after a lost response, but not for the next scored row', () => {
    startGame();
    type('punts');
    game.press('Enter');
    const first = http.expectOne(GUESSES);
    const id = first.request.body.requestId;
    expect(id).toMatch(/^[0-9a-f-]{36}$/);
    first.error(new ProgressEvent('error'));
    // The guess may have been saved, so the board is reloaded; this one was not.
    http.expectOne('/api/spordle/games/g1').flush({ ...saved, guesses: [], letterHint: null });
    expect(game.status()).toBe('playing');
    expect(game.currentGuess()).toBe('punts');
    expect(game.message()).toBe('Error checking the word. Please try again.');
    game.press('Enter');
    const retry = http.expectOne(GUESSES);
    expect(retry.request.body.requestId).toBe(id);
    retry.flush(miss);
    vi.runAllTimers();
    expect(game.attempts()).toHaveLength(1);
    type('punts');
    game.press('Enter');
    const next = http.expectOne(GUESSES);
    expect(next.request.body.requestId).not.toBe(id);
    next.flush({ ...miss, attemptsRemaining: 4 });
    vi.runAllTimers();
    expect(game.attempts()).toHaveLength(2);
  });

  it('shows a guess that was saved even though its response was lost, before another word is typed', () => {
    startGame();
    type('punts');
    game.press('Enter');
    const lost = http.expectOne(GUESSES);
    const requestId = lost.request.body.requestId;
    lost.error(new ProgressEvent('error'));
    expect(game.busy()).toBe(true); // no new word until the board is known
    http.expectOne('/api/spordle/games/g1').flush({ ...saved, letterHint: null,
      guesses: [{ requestId, word: 'punts', score: miss.score }] });

    expect(game.attempts()).toEqual([{ word: 'punts', score: miss.score }]);
    expect(game.currentGuess()).toBe('');
    expect(game.message()).toBe('');
    expect(game.status()).toBe('playing');
    type('blitz');
    game.press('Enter');
    const next = http.expectOne(GUESSES);
    expect(next.request.body.requestId).not.toBe(requestId);
    next.flush({ ...miss, attemptsRemaining: 4 });
    vi.runAllTimers();
    expect(game.attempts().map(a => a.word)).toEqual(['punts', 'blitz']);
  });

  it('reloads the board when another tab guessed in between', () => {
    startGame();
    guess('punts', miss);
    type('blitz');
    game.press('Enter');
    // The server has three guesses: one from here, one from another tab, and this one.
    http.expectOne(GUESSES).flush({ ...miss, attemptsRemaining: 3 });
    const words = ['punts', 'goals', 'blitz'];
    http.expectOne('/api/spordle/games/g1').flush({ ...saved, letterHint: null,
      guesses: words.map((word, i) => ({ requestId: `r${i}`, word, score: miss.score })) });
    vi.runAllTimers();

    expect(game.attempts().map(a => a.word)).toEqual(words);
    expect(game.currentGuess()).toBe('');
    expect(game.status()).toBe('playing');
  });

  it('shows the finished round when a stale tab guesses after the game ended elsewhere', () => {
    startGame();
    guess('punts', miss);
    type('goals');
    game.press('Enter');
    http.expectOne(GUESSES).flush({ error: 'Game is already complete' }, { status: 409, statusText: 'Conflict' });
    const won = { requestId: 'other', word: 'blitz', score: Array(5).fill('correct') };
    http.expectOne('/api/spordle/games/g1').flush({ ...saved, complete: true, won: true, answer: 'blitz',
      guesses: [saved.guesses[0], won] });

    expect(game.status()).toBe('complete');
    expect(game.result()).toMatchObject({ won: true, answer: 'blitz', guesses: 2 });
    expect(game.attempts()).toHaveLength(2);
    expect(getStats(5)).toMatchObject({ totalGames: 1, wins: 1 });
  });

  it('counts an abandoned game as a loss, but not an untouched one', () => {
    startGame();
    game.newGame();
    http.expectOne('/api/spordle/games').flush({ gameId: 'g1', wordLength: 5, maxAttempts: 6 });
    expect(getStats(5).totalGames).toBe(0);

    guess('punts', miss);
    game.newGame(4);
    http.expectOne('/api/spordle/games').flush({ gameId: 'g2', wordLength: 4, maxAttempts: 6 });

    expect(getStats(5)).toMatchObject({ totalGames: 1, wins: 0 });
    expect(game.wordLength()).toBe(4);
    expect(game.attempts()).toEqual([]);
    expect(game.letterStatuses()).toEqual({});
  });

  it('fetches each hint once and keeps it', () => {
    startGame();

    game.revealHint('category');
    const category = http.expectOne(HINT);
    expect(category.request.body).toEqual({ type: 'category' });
    category.flush({ category: 'Football' });

    game.revealHint('letter');
    http.expectOne(HINT).flush({ position: 3, letter: 'i' });

    game.revealHint('category');
    game.revealHint('letter');
    http.expectNone(HINT);

    expect(game.categoryHint()).toBe('Football');
    expect(game.letterHint()).toEqual({ position: 3, letter: 'i' });
  });

  it('shows why a hint could not be given', () => {
    startGame();
    game.revealHint('letter');
    http.expectOne(HINT).flush({ error: 'Nothing left to reveal' }, { status: 409, statusText: 'Conflict' });

    expect(game.letterHint()).toBeNull();
    expect(game.hintError()).toBe('Nothing left to reveal');
    expect(game.hintLoading()).toBe(false);
  });

  it('clears hints when a new game starts', () => {
    startGame();
    game.revealHint('category');
    http.expectOne(HINT).flush({ category: 'Football' });

    game.newGame();
    http.expectOne('/api/spordle/games').flush({ gameId: 'g2', wordLength: 5, maxAttempts: 6 });

    expect(game.categoryHint()).toBeNull();
  });
});
