import { Component, ElementRef, computed, effect, inject, signal, viewChild } from '@angular/core';
import { SpordleKeyboard } from './keyboard';
import { HintType } from './spordle-api';
import { MAX_ATTEMPTS, SpordleGame } from './spordle-game';
import { WORD_LENGTHS, WordLength, formatTime, getStats, resetStats } from './spordle-stats';
import { SpordleGrid } from './word-grid';

const MESSAGE_MS = 3000;

/**
 * Spordle: guess the sports word in six tries. Plays like Nerdle (random word per game,
 * 4, 5 or 6 letters, category and letter hints, stats per word length) in Know Ball's look.
 */
@Component({
  selector: 'app-spordle',
  imports: [SpordleGrid, SpordleKeyboard],
  providers: [SpordleGame],
  host: { '(window:keydown)': 'onKeydown($event)' },
  template: `
    <section class="spordle" aria-label="Spordle game">
      <header class="top">
        <h1>Spordle</h1>
        <div class="actions">
          <button
            type="button"
            class="btn primary"
            [disabled]="game.busy()"
            (mousedown)="$event.preventDefault()"
            (click)="requestNewGame()"
          >
            <span aria-hidden="true">↻</span> New game
          </button>
          <button type="button" class="btn" (mousedown)="$event.preventDefault()" (click)="openStats()">Stats</button>
        </div>
      </header>

      <div class="toolbar">
        <div class="lengths" role="group" aria-label="Word length">
          <span class="label" aria-hidden="true">Letters</span>
          @for (n of lengths; track n) {
            <button
              type="button"
              [class.selected]="n === game.wordLength()"
              [attr.aria-pressed]="n === game.wordLength()"
              [attr.aria-label]="n + ' letters'"
              [disabled]="game.busy()"
              (mousedown)="$event.preventDefault()"
              (click)="changeLength(n)"
            >
              {{ n }}
            </button>
          }
        </div>
        <span class="status" role="status">{{ statusText() }}</span>
      </div>

      @if (game.result(); as result) {
        <div class="outcome" [class.won]="result.won">
          <p>
            @if (result.won) {
              Solved in {{ result.guesses }} {{ result.guesses === 1 ? 'guess' : 'guesses' }} ·
              {{ formatTime(result.timeMs) }}
            } @else {
              The word was <strong>{{ result.answer }}</strong>
            }
          </p>
          <button type="button" class="btn primary" (mousedown)="$event.preventDefault()" (click)="requestNewGame()">
            Play again
          </button>
        </div>
      } @else {
        <div class="hints" role="group" aria-labelledby="spordle-hints">
          <span id="spordle-hints" class="label">Hints</span>
          <button
            type="button"
            class="btn hint"
            [class.revealed]="game.categoryHint()"
            [disabled]="game.status() !== 'playing'"
            (mousedown)="$event.preventDefault()"
            (click)="openHint('category')"
          >
            {{ game.categoryHint() || 'Category' }}
          </button>
          <button
            type="button"
            class="btn hint"
            [class.revealed]="game.letterHint()"
            [disabled]="game.status() !== 'playing'"
            (mousedown)="$event.preventDefault()"
            (click)="openHint('letter')"
          >
            @if (game.letterHint(); as hint) {
              Letter {{ hint.position }} is {{ hint.letter.toUpperCase() }}
            } @else {
              Letter
            }
          </button>
        </div>
      }

      <app-spordle-grid
        class="board"
        [style.--cols]="game.wordLength()"
        [attempts]="game.attempts()"
        [currentGuess]="game.currentGuess()"
        [wordLength]="game.wordLength()"
        [active]="game.status() === 'playing' || game.status() === 'validating' || game.status() === 'loading'"
        [hint]="game.letterHint()"
      />

      <app-spordle-keyboard [statuses]="game.letterStatuses()" (pressed)="game.press($event)" />

      @if (game.message(); as message) {
        <p class="toast" role="status">{{ message }}</p>
      }
    </section>

    <dialog #hintDialog class="dialog" aria-labelledby="spordle-hint-title" (click)="closeOnBackdrop($event)">
      <div class="dialog-head">
        <h2 id="spordle-hint-title">{{ hintType() === 'category' ? 'Category clue' : 'Letter hint' }}</h2>
        <button type="button" class="close" aria-label="Close hint" (click)="hintDialog.close()">×</button>
      </div>
      <p>
        {{
          hintType() === 'category'
            ? 'The part of the sports world this word belongs to.'
            : 'One letter in its place, once per game.'
        }}
        Hints never use up a guess.
      </p>
      <div role="status">
        @if (hintType() === 'category' && game.categoryHint(); as category) {
          <p class="hint-result">{{ category }}</p>
        } @else if (hintType() === 'letter' && game.letterHint(); as hint) {
          <p class="hint-result">Letter {{ hint.position }} is {{ hint.letter.toUpperCase() }}</p>
        }
        @if (game.hintError(); as error) {
          <p class="error">{{ error }}</p>
        }
      </div>
      <!--
        One button throughout, and never disabled: a focused button that is disabled or removed
        swallows the next key press, so Enter and Escape would stop working in the dialog.
      -->
      <button
        type="button"
        class="btn primary"
        autofocus
        [attr.aria-busy]="game.hintLoading()"
        (click)="hintShown() ? hintDialog.close() : game.revealHint(hintType())"
      >
        {{ hintButtonLabel() }}
      </button>
    </dialog>

    <dialog #statsDialog class="dialog" aria-labelledby="spordle-stats-title" (click)="closeOnBackdrop($event)">
      <div class="dialog-head">
        <h2 id="spordle-stats-title">Statistics <small>{{ game.wordLength() }}-letter words</small></h2>
        <button type="button" class="close" aria-label="Close statistics" (click)="statsDialog.close()">×</button>
      </div>
      <dl class="stats">
        <div><dd>{{ stats().totalGames }}</dd><dt>Played</dt></div>
        <div><dd>{{ winRate() }}%</dd><dt>Win %</dt></div>
        <div><dd>{{ stats().currentStreak }}</dd><dt>Streak</dt></div>
        <div><dd>{{ stats().longestStreak }}</dd><dt>Best streak</dt></div>
        <div><dd>{{ formatTime(stats().fastestSolveMs) }}</dd><dt>Fastest</dt></div>
        <div><dd>{{ stats().fewestGuesses ?? '--' }}</dd><dt>Fewest guesses</dt></div>
      </dl>
      <div class="resets">
        <button type="button" class="btn" (click)="requestReset(game.wordLength())">
          Reset {{ game.wordLength() }}-letter stats
        </button>
        <button type="button" class="btn" (click)="requestReset('all')">Reset all word lengths</button>
      </div>
    </dialog>
  `,
  styles: `

    :host { display: block; margin: -24px 0 -44px; }
    .spordle { max-width: 480px; margin: 0 auto; display: grid; gap: 12px; }

    .top, .toolbar, .hints, .outcome { display: flex; align-items: center; gap: 8px; }
    .top, .toolbar, .outcome { justify-content: space-between; }
    /* The wordmark is set like script lettering across a jersey. */
    h1 {
      margin: 0 0 0 2px; font: 400 50px/1 var(--script); color: var(--red);
      text-shadow: 1.5px 1.5px 0 var(--cream), 3px 3px 0 var(--navy);
      transform: rotate(-4deg); transform-origin: left center;
    }
    .actions { display: flex; gap: 8px; }

    .btn {
      border: 2px solid var(--line); border-radius: 999px; background: var(--surface); color: var(--text);
      padding: 6px 13px; font: 700 13px/1.2 var(--font); text-transform: uppercase; letter-spacing: .05em;
      cursor: pointer; white-space: nowrap;
    }
    .btn:hover:not(:disabled) { background: var(--surface-2); }
    .btn:disabled { opacity: .5; cursor: default; }
    .btn.primary { background: var(--accent); color: var(--accent-ink); }
    .btn.primary:hover:not(:disabled) { background: #a3271d; }

    .label { font-size: 13px; font-weight: 700; letter-spacing: .1em; text-transform: uppercase; }
    .lengths { display: flex; align-items: center; gap: 5px; }
    .lengths .label { margin-right: 4px; }
    .lengths button {
      width: 32px; height: 32px; padding: 0; border: 2px solid var(--line); border-radius: 50%;
      background: var(--surface); color: var(--text); font: 700 16px/1 var(--font); cursor: pointer;
    }
    .lengths button.selected { background: var(--navy); color: var(--cream); }
    .lengths button:disabled { cursor: default; }
    .lengths button:disabled:not(.selected) { opacity: .5; }
    .status { font-size: 17px; font-style: italic; font-variant-numeric: tabular-nums; white-space: nowrap; }

    .hints, .outcome { min-height: 34px; }
    /* Hints look like ticket stubs: dashed until torn off, filled once revealed. */
    .btn.hint {
      border-style: dashed; border-radius: 4px; background: none; padding: 3px 11px;
      font-size: 15px; text-transform: none; letter-spacing: 0;
    }
    .btn.hint.revealed { border-style: solid; background: var(--mustard); }
    .hint.revealed:disabled { opacity: 1; }
    .outcome p { margin: 0; font-size: 18px; }
    .outcome strong { font: 400 20px/1 var(--display); letter-spacing: .06em; text-transform: uppercase; color: var(--accent); }
    .outcome.won p { color: var(--ok); font-weight: 700; }

    /* Tiles shrink to fit the narrower of the screen's width and its spare height. */
    .board {
      --tile: clamp(
        34px,
        min((100vw - 40px - (var(--cols) - 1) * 6px) / var(--cols), (100dvh - 430px) / 6),
        58px
      );
      padding: 4px 0;
    }

    .toast {
      position: fixed; left: 50%; top: 220px; transform: translateX(-50%); z-index: 20; margin: 0;
      max-width: calc(100vw - 32px); padding: 9px 16px; border: 2px solid var(--cream); border-radius: 4px;
      background: var(--navy); color: var(--cream); font-size: 16px; font-weight: 700; text-align: center;
      box-shadow: 0 0 0 2px var(--navy), 0 8px 20px rgb(29 43 83 / .35);
    }

    /* Dialogs are cards: navy line, cream gap, red border. */
    .dialog {
      width: min(420px, calc(100vw - 40px)); padding: 22px; border: 2px solid var(--navy); border-radius: 6px;
      background: var(--surface); color: var(--text); box-shadow: 0 0 0 2px var(--cream), 0 0 0 8px var(--red);
    }
    .dialog::backdrop { background: rgb(29 43 83 / .6); }
    .dialog-head { display: flex; align-items: flex-start; justify-content: space-between; gap: 12px; }
    .dialog h2 { margin: 0; font: 400 22px/1.15 var(--display); letter-spacing: .02em; }
    .dialog h2 small { display: block; margin-top: 4px; font: 500 15px/1.3 var(--font); letter-spacing: 0; color: var(--muted); }
    .dialog p { margin: 12px 0; color: var(--muted); }
    .close {
      width: 32px; height: 32px; padding: 0; border: 0; border-radius: 50%; background: none; color: var(--navy);
      font-size: 26px; line-height: 1; cursor: pointer;
    }
    .close:hover { background: var(--surface-2); }
    .dialog .hint-result { font: 400 24px/1.2 var(--display); color: var(--accent); }
    .dialog .error { color: var(--bad); font-weight: 700; }

    .stats { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; margin: 16px 0; }
    .stats div { padding: 12px 6px; border: 2px solid var(--navy); border-radius: 4px; background: var(--cream); text-align: center; }
    .stats dd { margin: 0; font: 400 26px/1.1 var(--display); }
    .stats dt { margin-top: 3px; font-size: 12px; font-weight: 700; letter-spacing: .06em; text-transform: uppercase; color: var(--muted); }
    .resets { display: flex; flex-wrap: wrap; gap: 8px; }

    @media (max-width: 420px) {
      h1 { font-size: 42px; }
      .btn { padding: 6px 10px; font-size: 12px; }
    }
  `,
})
export class Spordle {
  protected readonly game = inject(SpordleGame);
  protected readonly lengths = WORD_LENGTHS;
  protected readonly formatTime = formatTime;

  protected readonly hintType = signal<HintType>('category');
  protected readonly stats = signal(getStats(this.game.wordLength()));
  protected readonly winRate = computed(() => {
    const { wins, totalGames } = this.stats();
    return totalGames ? Math.round((wins / totalGames) * 100) : 0;
  });
  protected readonly hintShown = computed(() =>
    this.hintType() === 'category' ? !!this.game.categoryHint() : !!this.game.letterHint(),
  );
  protected readonly hintButtonLabel = computed(() => {
    if (this.hintShown()) return 'Got it';
    if (this.game.hintLoading()) return 'Loading…';
    return this.hintType() === 'category' ? 'Reveal category' : 'Reveal letter';
  });
  protected readonly statusText = computed(() => {
    switch (this.game.status()) {
      case 'loading':
        return 'Starting…';
      case 'error':
        return 'Unavailable';
      case 'validating':
        return 'Checking…';
      case 'revealing':
        return 'Revealing…';
      case 'complete':
        return this.game.result()?.won ? 'Solved' : 'Finished';
      default:
        return `Guess ${Math.min(this.game.attempts().length + 1, MAX_ATTEMPTS)} of ${MAX_ATTEMPTS}`;
    }
  });

  private readonly hintDialog = viewChild.required<ElementRef<HTMLDialogElement>>('hintDialog');
  private readonly statsDialog = viewChild.required<ElementRef<HTMLDialogElement>>('statsDialog');

  constructor() {
    this.game.start();

    effect((onCleanup) => {
      if (!this.game.message()) return;
      const timer = setTimeout(() => this.game.message.set(''), MESSAGE_MS);
      onCleanup(() => clearTimeout(timer));
    });
  }

  protected requestNewGame(length: WordLength = this.game.wordLength()): void {
    if (this.game.busy()) return;
    if (
      this.game.inProgress() &&
      !window.confirm('Starting a new game will count this one as a loss. Start a new game?')
    ) {
      return;
    }
    this.game.newGame(length);
  }

  protected changeLength(length: WordLength): void {
    if (length !== this.game.wordLength()) this.requestNewGame(length);
  }

  protected openHint(type: HintType): void {
    this.hintType.set(type);
    this.game.hintError.set('');
    openDialog(this.hintDialog().nativeElement);
  }

  protected openStats(): void {
    this.stats.set(getStats(this.game.wordLength()));
    openDialog(this.statsDialog().nativeElement);
  }

  protected requestReset(scope: WordLength | 'all'): void {
    const what = scope === 'all' ? 'all word lengths' : `${scope}-letter games`;
    if (!window.confirm(`Reset stats for ${what}? This cannot be undone.`)) return;
    resetStats(scope);
    this.stats.set(getStats(this.game.wordLength()));
  }

  /** A click on the dialog element itself, rather than its content, landed on the backdrop. */
  protected closeOnBackdrop(event: MouseEvent): void {
    const dialog = event.currentTarget as HTMLDialogElement;
    if (event.target === dialog) dialog.close();
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.ctrlKey || event.metaKey || event.altKey || event.defaultPrevented) return;
    if (document.querySelector('dialog[open]')) return;

    // Enter or Space on a focused control belongs to that control, with one exception:
    // once a full word is typed, Enter submits it even if a button still has focus.
    const onControl = event.target instanceof Element && !!event.target.closest('button, a, input, select, textarea');
    if (onControl && (event.key === 'Enter' || event.key === ' ')) {
      const wordReady =
        event.key === 'Enter' &&
        this.game.status() === 'playing' &&
        this.game.currentGuess().length === this.game.wordLength();
      if (!wordReady) return;
      event.preventDefault();
    }

    this.game.press(event.key);
  }
}

function openDialog(dialog: HTMLDialogElement): void {
  if (dialog.open) return;
  // Older browsers and test DOMs may lack showModal.
  if (typeof dialog.showModal === 'function') dialog.showModal();
  else dialog.setAttribute('open', '');
}
