import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { LetterStatus, mergeKeyboardStatuses } from './scoring';
import { GuessResult, HintType, SpordleApi } from './spordle-api';
import { DEFAULT_WORD_LENGTH, WordLength, isWordLength, recordGame } from './spordle-stats';

export const MAX_ATTEMPTS = 6;
/** Tiles flip one after another; these match the grid's CSS animation. */
export const FLIP_STAGGER_MS = 300;
export const FLIP_DURATION_MS = 500;

const LENGTH_KEY = 'knowball.spordle.wordLength';

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
  /** At least one scored guess and not over yet: walking away now counts as a loss. */
  readonly inProgress = computed(() => this.status() === 'playing' && this.attempts().length > 0);

  constructor() {
    inject(DestroyRef).onDestroy(() => this.cancel());
  }

  /** Begins a fresh game, dropping whatever was on the board. */
  start(length: WordLength = this.wordLength()): void {
    this.cancel();
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
      recordGame(this.wordLength(), { won: false, guesses: this.attempts().length, timeMs: null });
    }
    this.start(length);
  }

  /** A key from the on-screen or physical keyboard: a letter, "Enter" or "Backspace". */
  press(key: string): void {
    if (this.status() !== 'playing' && this.status() !== 'loading') return;

    if (key === 'Enter') {
      this.submit();
    } else if (key === 'Backspace') {
      this.currentGuess.update((guess) => guess.slice(0, -1));
    } else if (/^[a-zA-Z]$/.test(key) && this.currentGuess().length < this.wordLength()) {
      this.currentGuess.update((guess) => guess + key.toLowerCase());
    }
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
    this.request = this.api.submitGuess(this.gameId(), guess).subscribe({
      next: (result) => this.applyResult(guess, result),
      error: (error) => {
        if (error instanceof HttpErrorResponse && error.status === 404) {
          this.expire();
          return;
        }
        this.message.set(serverMessage(error) ?? 'Error checking the word. Please try again.');
        this.status.set('playing');
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
  }

  private applyResult(guess: string, result: GuessResult): void {
    const score = result.score;
    if (!result.valid || !score) {
      this.message.set('Not in the word list. Try again.');
      this.status.set('playing');
      return;
    }

    this.attempts.update((attempts) => [...attempts, { word: guess, score }]);
    this.currentGuess.set('');
    this.status.set('revealing');

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
      const timeMs = won ? Date.now() - this.startedAt : null;
      recordGame(length, { won, guesses, timeMs });
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
