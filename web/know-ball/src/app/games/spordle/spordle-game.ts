import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { LetterStatus, mergeKeyboardStatuses } from './scoring';
import { GuessResult, HintType, SavedGame, SpordleApi } from './spordle-api';
import { DEFAULT_WORD_LENGTH, WordLength, isWordLength, recordGame } from './spordle-stats';

export const MAX_ATTEMPTS = 6;
/** Tiles flip one after another; these match the grid's CSS animation. */
export const FLIP_STAGGER_MS = 300;
export const FLIP_DURATION_MS = 500;

const LENGTH_KEY = 'knowball.spordle.wordLength';
const ROUND_KEY = 'knowball.spordle.round';

interface SavedRound {
  gameId: string;
  startedAt: number;
  finishedAt?: number;
  currentGuess: string;
  categoryHint: string | null;
  pendingGuess?: { word: string; id: string };
}

export type GameStatus = 'loading' | 'playing' | 'validating' | 'revealing' | 'complete' | 'expired' | 'error';

export interface Attempt {
  word: string;
  score: LetterStatus[];
}

export interface LetterHint {
  /** 1-based. */
  position: number;
  letter: string;
}

export interface GameResult {
  won: boolean;
  answer: string;
  guesses: number;
  timeMs: number | null;
}

/**
 * One Spordle game at a time. The server holds the answer and scores each guess; this keeps
 * what the player sees: guesses so far, the row being typed, keyboard colours and hints.
 */
@Injectable()
export class SpordleGame {
  private readonly api = inject(SpordleApi);
  private timers: ReturnType<typeof setTimeout>[] = [];
  private request?: Subscription;
  private hintRequest?: Subscription;
  private startedAt = 0;
  private finishedAt?: number;
  private pendingGuess?: { word: string; id: string };

  readonly wordLength = signal<WordLength>(readStoredLength());
  readonly gameId = signal('');
  readonly attempts = signal<Attempt[]>([]);
  readonly currentGuess = signal('');
  readonly letterStatuses = signal<Record<string, LetterStatus>>({});
  readonly status = signal<GameStatus>('loading');
  /** Short notice for the player; the page clears it after a few seconds. */
  readonly message = signal('');
  readonly result = signal<GameResult | null>(null);
  readonly categoryHint = signal<string | null>(null);
  readonly letterHint = signal<LetterHint | null>(null);
  readonly hintLoading = signal(false);
  readonly hintError = signal('');

  /** A request or the tile reveal is in flight, so input is ignored. */
  readonly busy = computed(() => ['loading', 'validating', 'revealing'].includes(this.status()));
  /** Starting a new round with scored guesses still on this board counts as a loss. */
  readonly inProgress = computed(() => this.status() === 'playing' && this.attempts().length > 0);

  constructor() {
    inject(DestroyRef).onDestroy(() => this.cancel());
  }

  /** Rebuild the board from the server; a temporary failure keeps the saved round. */
  resume(): void {
    let saved: SavedRound | null = null;
    try {
      const value = JSON.parse(localStorage.getItem(ROUND_KEY) ?? 'null');
      if (typeof value?.gameId === 'string' && Number.isFinite(value.startedAt)) saved = value;
    } catch { /* Missing or damaged storage starts a fresh round. */ }
    if (!saved) { this.start(); return; }
    const round = saved;
    this.cancel();
    this.gameId.set(round.gameId);
    this.startedAt = round.startedAt;
    this.finishedAt = round.finishedAt;
    this.status.set('loading');
    this.request = this.api.getGame(round.gameId).subscribe({
      next: (state) => this.applyState(state, round),
      error: (error) => {
        if (error instanceof HttpErrorResponse && error.status === 404) { this.expire(); return; }
        this.message.set('Could not resume your game. Reload to retry; your saved round is still available.');
        this.status.set('error');
      },
    });
  }

  /**
   * Shows the server's copy of the round. Returns whether the pending guess was among the saved ones,
   * so a retry is no longer needed.
   */
  private applyState(state: SavedGame, saved: Pick<SavedRound, 'currentGuess' | 'categoryHint' | 'pendingGuess'>): boolean {
    if (!isWordLength(state.wordLength)) { this.expire(); return false; }
    this.wordLength.set(state.wordLength);
    this.attempts.set(state.guesses.map(g => ({ word: g.word, score: g.score })));
    this.letterStatuses.set(state.guesses.reduce((keys, g) =>
      mergeKeyboardStatuses(keys, g.word, g.score), {} as Record<string, LetterStatus>));
    this.categoryHint.set(typeof saved.categoryHint === 'string' ? saved.categoryHint : null);
    this.letterHint.set(state.letterHint);
    const acknowledged = state.guesses.some(g => g.requestId === saved.pendingGuess?.id);
    this.pendingGuess = acknowledged ? undefined : saved.pendingGuess;
    this.currentGuess.set(state.complete || acknowledged ? '' :
      typeof saved.currentGuess === 'string' ? saved.currentGuess.slice(0, state.wordLength) : '');
    this.message.set('');
    this.result.set(null);
    if (state.complete) {
      this.finishedAt ??= Date.now();
      const timeMs = state.won ? Math.max(0, this.finishedAt - this.startedAt) : null;
      const result = { won: state.won, answer: state.answer!, guesses: state.guesses.length, timeMs };
      recordGame(state.wordLength, result, state.gameId);
      this.result.set(result);
      this.status.set('complete');
    } else {
      this.status.set('playing');
    }
    this.persist();
    return acknowledged;
  }

  /**
   * After a failed guess, or one that does not follow the board (another tab guessed too), the server's
   * saved round is the truth. A lost response may still have used up a guess, so reload it before the
   * player can type something else.
   */
  private reconcile(failure?: string): void {
    this.status.set('validating');
    this.request = this.api.getGame(this.gameId()).subscribe({
      next: (state) => {
        const acknowledged = this.applyState(state, {
          currentGuess: this.currentGuess(), categoryHint: this.categoryHint(), pendingGuess: this.pendingGuess,
        });
        if (failure && !acknowledged && this.status() === 'playing') this.message.set(failure);
      },
      error: (error) => {
        if (error instanceof HttpErrorResponse && error.status === 404) { this.expire(); return; }
        if (failure) this.message.set(failure);
        this.status.set('playing');
      },
    });
  }

  private persist(): void {
    if (!this.gameId()) return;
    try {
      localStorage.setItem(ROUND_KEY, JSON.stringify({
        gameId: this.gameId(), startedAt: this.startedAt, finishedAt: this.finishedAt,
        currentGuess: this.currentGuess(), categoryHint: this.categoryHint(), pendingGuess: this.pendingGuess,
      }));
    } catch { /* Blocked storage: the round lasts for this visit only. */ }
  }

  /** Begins a fresh game, dropping whatever was on the board. */
  start(length: WordLength = this.wordLength()): void {
    this.cancel();
    this.pendingGuess = undefined;
    this.finishedAt = undefined;
    try { localStorage.removeItem(ROUND_KEY); } catch { /* Storage may be blocked. */ }
    this.wordLength.set(length);
    try {
      localStorage.setItem(LENGTH_KEY, String(length));
    } catch {
      // Storage blocked: the choice lasts for this visit only.
    }

    this.gameId.set('');
    this.attempts.set([]);
    this.currentGuess.set('');
    this.letterStatuses.set({});
    this.message.set('');
    this.result.set(null);
    this.categoryHint.set(null);
    this.letterHint.set(null);
    this.hintLoading.set(false);
    this.hintError.set('');
    this.status.set('loading');

    this.request = this.api.startGame(length).subscribe({
      next: (game) => {
        this.gameId.set(game.gameId);
        this.startedAt = Date.now();
        this.status.set('playing');
        this.persist();
      },
      error: () => {
        this.message.set('Failed to start a game. Please try again later.');
        this.status.set('error');
      },
    });
  }

  /** Like {@link start}, but an unfinished game with guesses on the board is recorded as a loss first. */
  newGame(length: WordLength = this.wordLength()): void {
    if (this.status() === 'validating' || this.status() === 'revealing') return;
    if (this.inProgress()) {
      recordGame(this.wordLength(), { won: false, guesses: this.attempts().length, timeMs: null }, this.gameId());
    }
    this.start(length);
  }

  /** A key from the on-screen or physical keyboard: a letter, "Enter" or "Backspace". */
  press(key: string): void {
    if (this.status() !== 'playing' && this.status() !== 'loading') return;
    // Resume must finish before typing or submitting against the existing server round.
    if (this.status() === 'loading' && this.gameId()) return;

    if (key === 'Enter') {
      this.submit();
    } else if (key === 'Backspace') {
      this.currentGuess.update((guess) => guess.slice(0, -1));
    } else if (/^[a-zA-Z]$/.test(key) && this.currentGuess().length < this.wordLength()) {
      this.currentGuess.update((guess) => guess + key.toLowerCase());
    }
    this.persist();
  }

  /** Asks the server for a hint. Each kind is fetched once per game and then kept. */
  revealHint(type: HintType): void {
    if (this.status() !== 'playing' || this.hintLoading()) return;
    if (type === 'category' ? this.categoryHint() : this.letterHint()) return;

    this.hintLoading.set(true);
    this.hintError.set('');
    this.hintRequest = this.api.requestHint(this.gameId(), type).subscribe({
      next: (hint) => {
        if (hint.category) this.categoryHint.set(hint.category);
        if (hint.position && hint.letter) this.letterHint.set({ position: hint.position, letter: hint.letter });
        this.hintLoading.set(false);
        this.persist();
      },
      error: (error) => {
        if (error instanceof HttpErrorResponse && error.status === 404) {
          this.expire();
          return;
        }
        this.hintError.set(serverMessage(error) ?? 'Could not load your hint. Please try again.');
        this.hintLoading.set(false);
      },
    });
  }

  private submit(): void {
    const guess = this.currentGuess();
    const length = this.wordLength();

    if (!this.gameId()) {
      this.message.set('Still starting the game. Please wait.');
      return;
    }
    if (guess.length !== length) {
      this.message.set(`Guess must be ${length} letters.`);
      return;
    }

    this.message.set('');
    this.status.set('validating');
    // A failed response may still have committed on the server. Retry with the same ID.
    if (this.pendingGuess?.word !== guess) this.pendingGuess = { word: guess, id: crypto.randomUUID() };
    this.persist();
    this.request = this.api.submitGuess(this.gameId(), guess, this.pendingGuess.id).subscribe({
      next: (result) => {
        this.pendingGuess = undefined;
        this.applyResult(guess, result);
        this.persist();
      },
      error: (error) => {
        if (error instanceof HttpErrorResponse && error.status === 404) {
          this.expire();
          return;
        }
        const failure = serverMessage(error) ?? 'Error checking the word. Please try again.';
        // A rejected word (400) or a rate limit (429) saved nothing; anything else may have.
        if (error instanceof HttpErrorResponse && (error.status === 400 || error.status === 429)) {
          this.message.set(failure);
          this.status.set('playing');
          return;
        }
        this.reconcile(failure);
      },
    });
  }

  private expire(): void {
    this.cancel();
    this.hintLoading.set(false);
    this.hintError.set('');
    this.message.set('');
    // A missing server session is not a player's loss. Keep their board visible until
    // they choose a fresh game, and do not write a result to local statistics.
    this.status.set('expired');
    try { localStorage.removeItem(ROUND_KEY); } catch { /* Storage may be blocked. */ }
  }

  private applyResult(guess: string, result: GuessResult): void {
    const score = result.score;
    if (!result.valid || !score) {
      this.message.set('Not in the word list. Try again.');
      this.status.set('playing');
      return;
    }
    // The server counts every guess on this round. If that is not one more than the board shows,
    // another tab or a lost response got in between: show the saved round instead.
    if (result.attemptsRemaining !== undefined && MAX_ATTEMPTS - result.attemptsRemaining !== this.attempts().length + 1) {
      this.currentGuess.set(''); // this guess was saved too
      this.reconcile();
      return;
    }

    this.attempts.update((attempts) => [...attempts, { word: guess, score }]);
    this.currentGuess.set('');
    this.status.set('revealing');
    if (result.complete) this.finishedAt = Date.now();

    const length = this.wordLength();
    const guesses = this.attempts().length;
    const keyboard = mergeKeyboardStatuses(this.letterStatuses(), guess, score);

    // The keyboard and the outcome wait for the last tile to finish flipping.
    this.after(revealTime(length), () => {
      this.letterStatuses.set(keyboard);

      if (!result.complete) {
        this.message.set('');
        this.status.set('playing');
        return;
      }

      const won = result.won === true;
      const answer = result.answer ?? guess;
      const timeMs = won ? (this.finishedAt ?? Date.now()) - this.startedAt : null;
      recordGame(length, { won, guesses, timeMs }, this.gameId());
      this.result.set({ won, answer, guesses, timeMs });
      this.message.set(won ? "Congratulations! You've guessed the word." : `Game over! The word was ${answer.toUpperCase()}.`);
      this.status.set('complete');
    });
  }

  private after(delay: number, callback: () => void): void {
    const timer = setTimeout(() => {
      this.timers = this.timers.filter((t) => t !== timer);
      callback();
    }, delay);
    this.timers.push(timer);
  }

  private cancel(): void {
    this.request?.unsubscribe();
    this.hintRequest?.unsubscribe();
    this.timers.forEach((timer) => clearTimeout(timer));
    this.timers = [];
  }
}

function revealTime(wordLength: number): number {
  const reducedMotion = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
  return reducedMotion ? 0 : (wordLength - 1) * FLIP_STAGGER_MS + FLIP_DURATION_MS;
}

function readStoredLength(): WordLength {
  try {
    const stored = Number(localStorage.getItem(LENGTH_KEY));
    return isWordLength(stored) ? stored : DEFAULT_WORD_LENGTH;
  } catch {
    return DEFAULT_WORD_LENGTH;
  }
}

/** The API reports problems as `{ error: "..." }`. */
function serverMessage(error: unknown): string | null {
  const body = error instanceof HttpErrorResponse ? error.error : null;
  return body && typeof body.error === 'string' ? body.error : null;
}
