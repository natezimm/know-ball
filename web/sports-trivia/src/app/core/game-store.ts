import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { Observable, Subscription } from 'rxjs';
import { GameApi, GameSlug, GameState } from './game-api';

export type LoadPhase = 'loading' | 'ready' | 'unavailable' | 'error';

/**
 * One puzzle on screen. The server keeps the answer and the progress; this holds the latest
 * copy it sent, so a reload or another tab picks the game up where it was.
 */
@Injectable()
export class GameStore {
  private readonly api = inject(GameApi);
  private request?: Subscription;
  private requestedId?: number;

  readonly game = signal<GameSlug>('scouting-report');
  readonly state = signal<GameState | null>(null);
  readonly phase = signal<LoadPhase>('loading');
  /** A guess or a new practice round is on its way to the server. */
  readonly busy = signal(false);
  /** Short notice for the player; the shell clears it after a few seconds. */
  readonly notice = signal('');
  /** Why there is nothing to play, when the phase is "unavailable". */
  readonly reason = signal('');

  readonly over = computed(() => {
    const status = this.state()?.status;
    return status === 'won' || status === 'lost';
  });
  readonly misses = computed(() => this.state()?.guesses.filter((g) => !g.correct).length ?? 0);

  constructor() {
    inject(DestroyRef).onDestroy(() => this.request?.unsubscribe());
  }

  /** Loads today's puzzle, or a past or practice one when an id is given. */
  load(game: GameSlug, puzzleId?: number): void {
    // A practice round arrives with its puzzle already in hand; moving to its address needs no second fetch.
    const current = this.state();
    if (puzzleId !== undefined && this.phase() === 'ready' && current?.game === game && current.puzzleId === puzzleId) {
      this.requestedId = puzzleId;
      return;
    }

    this.game.set(game);
    this.requestedId = puzzleId;
    this.fetch(puzzleId === undefined ? this.api.daily(game) : this.api.puzzle(game, puzzleId));
  }

  /** Asks again for the puzzle that failed to load. */
  retry(): void {
    const id = this.requestedId;
    this.fetch(id === undefined ? this.api.daily(this.game()) : this.api.puzzle(this.game(), id));
  }

  /** Starts a practice round. Emits its puzzle id so the page can move to its address. */
  practice(done: (puzzleId: number) => void): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.request?.unsubscribe();
    this.request = this.api.practice(this.game()).subscribe({
      next: (state) => {
        this.busy.set(false);
        this.accept(state);
        done(state.puzzleId);
      },
      error: (error) => {
        this.busy.set(false);
        this.notice.set(serverMessage(error) ?? 'Could not start a practice round. Please try again.');
      },
    });
  }

  guess(playerId: number): void {
    const state = this.state();
    if (!state || this.over() || this.busy()) return;

    this.busy.set(true);
    this.notice.set('');
    this.request?.unsubscribe();
    this.request = this.api.guess(state.game, state.puzzleId, playerId).subscribe({
      next: (next) => {
        this.busy.set(false);
        this.state.set(next);
      },
      error: (error) => {
        this.busy.set(false);
        this.notice.set(serverMessage(error) ?? 'That guess did not go through. Please try again.');
      },
    });
  }

  private fetch(source: Observable<GameState>): void {
    this.request?.unsubscribe();
    this.state.set(null);
    this.notice.set('');
    this.busy.set(false);
    this.phase.set('loading');

    this.request = source.subscribe({
      next: (state) => this.accept(state),
      error: (error) => {
        const missing = error instanceof HttpErrorResponse && error.status === 404;
        this.reason.set(missing ? (serverMessage(error) ?? 'There is no puzzle here.') : '');
        this.phase.set(missing ? 'unavailable' : 'error');
      },
    });
  }

  private accept(state: GameState): void {
    this.state.set(state);
    this.phase.set('ready');
  }
}

/** The API reports problems as `{ error: "..." }`. */
export function serverMessage(error: unknown): string | null {
  const body = error instanceof HttpErrorResponse ? error.error : null;
  return body && typeof body.error === 'string' ? body.error : null;
}
