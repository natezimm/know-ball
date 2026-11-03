import { Component, ElementRef, computed, effect, inject, signal, viewChild, ChangeDetectionStrategy } from '@angular/core';
import { shareText } from '../../core/share';
import { NextGame } from '../../shared/next-game/next-game';
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
  imports: [NextGame, SpordleGrid, SpordleKeyboard],
  providers: [SpordleGame],
  host: { '(window:keydown)': 'onKeydown($event)' },
  template: `
    <section class="spordle" aria-label="Spordle game">
      <header class="top kb-game-top">
        <h1 class="kb-wordmark">Spordle</h1>
        <div class="kb-icons">
          <button type="button" class="kb-icon" aria-label="How to play" title="How to play" (mousedown)="$event.preventDefault()" (click)="open(helpDialog)">?</button>
          <button type="button" class="kb-icon" aria-label="Stats" title="Stats" (mousedown)="$event.preventDefault()" (click)="openStats()">
            <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M2 14V8h3v6zm4.5 0V2h3v12zM11 14V5h3v9z" /></svg>
          </button>
          <button
            type="button"
            class="kb-icon"
            aria-label="New game"
            title="New word"
            [disabled]="game.busy()"
            (mousedown)="$event.preventDefault()"
            (click)="requestNewGame()"
          >
            <svg viewBox="0 0 16 16" aria-hidden="true">
              <path d="M8 2.5a5.5 5.5 0 1 0 5.4 6.5h-2.1A3.5 3.5 0 1 1 8 4.5c.9 0 1.8.4 2.4 1L8.5 7.4H14V1.9l-2.2 2.2A5.5 5.5 0 0 0 8 2.5z" />
            </svg>
          </button>
        </div>
      </header>

      <div class="toolbar kb-meta">
        <div class="lengths" role="group" aria-label="Word length">
          <span class="kb-label" aria-hidden="true">Letters</span>
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

      @if (game.status() === 'expired') {
        <div class="expired" role="alert">
          <p>This round is no longer available. It may have expired or the server restarted. Your stats are unchanged.</p>
          <button type="button" class="kb-btn primary" (click)="game.start()">Start a fresh game</button>
        </div>
      }

      @if (game.result(); as result) {
        <div class="outcome kb-result" [class.won]="result.won">
          <span class="tile-mark" aria-hidden="true">{{ result.answer[0] }}</span>
          <div class="kb-result-who">
            <span class="kb-label">
              @if (result.won) {
                Solved in {{ result.guesses }} {{ result.guesses === 1 ? 'guess' : 'guesses' }} · {{ formatTime(result.timeMs) }}
              } @else {
                The word was
              }
            </span>
            <strong>{{ result.answer }}</strong>
            @if (game.categoryHint(); as category) {
              <span class="kb-result-detail">{{ category }}</span>
            }
          </div>
          <div class="kb-result-next">
            <button type="button" class="kb-btn primary" (mousedown)="$event.preventDefault()" (click)="share()">{{ shareLabel() }}</button>
            <button type="button" class="kb-btn" (mousedown)="$event.preventDefault()" (click)="requestNewGame()">Play again</button>
            <app-next-game current="spordle" />
          </div>
        </div>
      } @else {
        <div class="hints" role="group" aria-labelledby="spordle-hints">
          <span id="spordle-hints" class="kb-label">Hints</span>
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
        [shake]="shaking()"
      />

      <app-spordle-keyboard [statuses]="game.letterStatuses()" (pressed)="game.press($event)" />

      @if (!game.result() && game.message(); as message) {
        <p class="kb-toast" role="status">{{ message }}</p>
      }
    </section>

    <dialog #helpDialog class="kb-dialog" aria-labelledby="spordle-help-title" (click)="closeOnBackdrop($event)">
      <div class="kb-dialog-head">
        <h2 id="spordle-help-title">How to play</h2>
        <button type="button" class="kb-close" aria-label="Close" (click)="helpDialog.close()">×</button>
      </div>
      <div class="help">
        <p>Find the sports word in six guesses. Pick 4, 5 or 6 letters; every guess must be a real word.</p>
        <p>After each guess the tiles show how close you are:</p>
        <ul class="legend">
          <li><i class="ok"></i><span><b>Teal</b>: the letter is in the word, in that spot.</span></li>
          <li><i class="close"></i><span><b>Mustard</b>: the letter is in the word, somewhere else.</span></li>
          <li><i class="miss"></i><span><b>Tan</b>: the letter is not in the word.</span></li>
        </ul>
        <p>Stuck? The Category and Letter hints are free and never use up a guess.</p>
        <p class="fine">A new word every game, as often as you like. Stats are kept in this browser for each word length.</p>
      </div>
    </dialog>

    <dialog #confirmDialog class="kb-dialog" aria-labelledby="spordle-confirm-title" (click)="closeOnBackdrop($event)" (close)="pendingLength.set(null)">
      <div class="kb-dialog-head">
        <h2 id="spordle-confirm-title">Start a new word?</h2>
        <button type="button" class="kb-close" aria-label="Close" (click)="confirmDialog.close()">×</button>
      </div>
      <p>This game counts as a loss in your stats if you leave it now.</p>
      <div class="choices">
        <button type="button" class="kb-btn primary" (click)="confirmNewGame()">Start new word</button>
        <button type="button" class="kb-btn" autofocus (click)="confirmDialog.close()">Keep playing</button>
      </div>
    </dialog>

    <dialog #hintDialog class="kb-dialog" aria-labelledby="spordle-hint-title" (click)="closeOnBackdrop($event)">
      <div class="kb-dialog-head">
        <h2 id="spordle-hint-title">{{ hintType() === 'category' ? 'Category clue' : 'Letter hint' }}</h2>
        <button type="button" class="kb-close" aria-label="Close hint" (click)="hintDialog.close()">×</button>
      </div>
      <p class="muted">
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
        class="kb-btn primary"
        autofocus
        [attr.aria-busy]="game.hintLoading()"
        (click)="hintShown() ? hintDialog.close() : game.revealHint(hintType())"
      >
        {{ hintButtonLabel() }}
      </button>
    </dialog>

    <dialog #statsDialog class="kb-dialog" aria-labelledby="spordle-stats-title" (click)="closeOnBackdrop($event)" (close)="resetting.set(false)">
      <div class="kb-dialog-head">
        <h2 id="spordle-stats-title">Statistics <small>{{ game.wordLength() }}-letter words</small></h2>
        <button type="button" class="kb-close" aria-label="Close statistics" (click)="statsDialog.close()">×</button>
      </div>
      <dl class="stats">
        <div><dd>{{ stats().totalGames }}</dd><dt>Played</dt></div>
        <div><dd>{{ winRate() }}%</dd><dt>Win %</dt></div>
        <div><dd>{{ stats().currentStreak }}</dd><dt>Streak</dt></div>
        <div><dd>{{ stats().longestStreak }}</dd><dt>Best streak</dt></div>
        <div><dd>{{ formatTime(stats().fastestSolveMs) }}</dd><dt>Fastest</dt></div>
        <div><dd>{{ stats().fewestGuesses ?? '--' }}</dd><dt>Fewest guesses</dt></div>
      </dl>
      <h3 class="kb-label">Guesses to solve</h3>
      <ol class="bars">
        @for (count of stats().distribution; track $index) {
          <li>
            <span>{{ $index + 1 }}</span>
            <i [style.width.%]="barWidth(count)" [class.zero]="count === 0" [class.latest]="game.result()?.won && game.result()?.guesses === $index + 1">{{ count }}</i>
          </li>
        }
      </ol>
      @if (resetting()) {
        <div class="resets" role="group" aria-label="Reset stats">
          <p>Reset clears these numbers for good.</p>
          <button type="button" class="kb-btn primary" (click)="reset(game.wordLength())">Reset {{ game.wordLength() }}-letter stats</button>
          <button type="button" class="kb-btn" (click)="reset('all')">Reset all word lengths</button>
          <button type="button" class="kb-link" (click)="resetting.set(false)">Cancel</button>
        </div>
      } @else {
        <button type="button" class="kb-link" (click)="resetting.set(true)">Reset stats…</button>
      }
    </dialog>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    :host { display: block; margin: 0 0 -44px; }
    .spordle { max-width: 480px; margin: 0 auto; display: grid; gap: 12px; --trim: var(--red); }

    .hints { display: flex; align-items: center; gap: 8px; min-height: 34px; }
    .toolbar { flex-wrap: wrap; }
    .lengths { display: flex; align-items: center; gap: 5px; }
    .lengths .kb-label { margin-right: 4px; }
    .lengths button {
      width: 32px; height: 32px; padding: 0; border: 2px solid var(--line); border-radius: 50%;
      background: var(--surface); color: var(--text); font: 700 16px/1 var(--font); cursor: pointer;
    }
    .lengths button.selected { background: var(--navy); color: var(--cream); }
    .lengths button:disabled { cursor: default; }
    .lengths button:disabled:not(.selected) { opacity: .5; }
    .status { font-size: 17px; font-style: italic; font-variant-numeric: tabular-nums; white-space: nowrap; }

    /* Hints look like ticket stubs: dashed until torn off, filled once revealed. */
    .btn.hint {
      border: 2px dashed var(--line); border-radius: 4px; background: none; color: var(--text); padding: 3px 11px;
      font: 700 15px/1.2 var(--font); cursor: pointer; white-space: nowrap;
    }
    .btn.hint:hover:not(:disabled) { background: var(--surface-2); }
    .btn.hint:disabled { opacity: .5; cursor: default; }
    .btn.hint.revealed { border-style: solid; background: var(--mustard); }
    .hint.revealed:disabled { opacity: 1; }

    /* The result: the answer's first letter as a tile, then the word in the card face. */
    .tile-mark {
      width: 58px; height: 58px; display: grid; place-items: center; border: 2px solid var(--navy); border-radius: 4px;
      background: var(--miss); color: var(--miss-ink); font: 400 30px/1 var(--display); text-transform: uppercase;
    }
    .outcome.won .tile-mark { background: var(--ok); color: var(--ok-ink); }
    .outcome strong { letter-spacing: .08em; text-transform: uppercase; }
    .expired { padding: 12px; border: 2px solid var(--accent); border-radius: var(--radius); background: var(--surface); }
    .expired p { margin: 0 0 10px; }

    /* Tiles shrink to fit the narrower of the screen's width and its spare height. */
    .board {
      --tile: clamp(
        34px,
        min((100vw - 40px - (var(--cols) - 1) * 6px) / var(--cols), (100dvh - 430px) / 6),
        58px
      );
      padding: 4px 0;
    }

    .kb-dialog h2 small { display: block; margin-top: 4px; font: 500 15px/1.3 var(--font); letter-spacing: 0; color: var(--muted); }
    .muted, .fine { color: var(--muted); }
    .fine { font-size: 15px; }
    .hint-result { font: 400 24px/1.2 var(--display); color: var(--accent); }
    .error { color: var(--bad); font-weight: 700; }
    .choices, .resets { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
    .resets p { flex-basis: 100%; margin: 4px 0; color: var(--muted); }

    .help { display: grid; gap: 4px; }
    .legend { list-style: none; margin: 0; padding: 0; display: grid; gap: 6px; }
    .legend li { display: flex; gap: 10px; align-items: baseline; }
    .legend i { flex: none; width: 16px; height: 16px; border: 2px solid var(--navy); border-radius: 3px; translate: 0 2px; }
    .legend .ok { background: var(--ok); }
    .legend .close { background: var(--close); }
    .legend .miss { background: var(--miss); }

    .stats { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; margin: 16px 0; }
    .stats div { padding: 12px 6px; border: 2px solid var(--navy); border-radius: 4px; background: var(--cream); text-align: center; }
    .stats dd { margin: 0; font: 400 26px/1.1 var(--display); }
    .stats dt { margin-top: 3px; font-size: 12px; font-weight: 700; letter-spacing: .06em; text-transform: uppercase; color: var(--muted); }
    .bars { list-style: none; margin: 8px 0 14px; padding: 0; display: grid; gap: 4px; }
    .bars li { display: grid; grid-template-columns: 14px 1fr; align-items: center; gap: 8px; font-weight: 700; }
    .bars i {
      min-width: 26px; padding: 1px 7px; border-radius: 3px; background: var(--navy); color: var(--cream);
      font-style: normal; font-size: 15px; text-align: right; box-sizing: border-box;
    }
    .bars .latest { background: var(--ok); }
    .bars .zero { background: var(--miss); color: var(--miss-ink); }

    @media (max-width: 420px) {
      .tile-mark { width: 48px; height: 48px; font-size: 25px; }
    }
  `,
})
export class Spordle {
  protected readonly game = inject(SpordleGame);
  protected readonly lengths = WORD_LENGTHS;
  protected readonly formatTime = formatTime;

  protected readonly hintType = signal<HintType>('category');
  /** The word length a confirmed new game will use, while the confirm dialog is open. */
  protected readonly pendingLength = signal<WordLength | null>(null);
  protected readonly resetting = signal(false);
  protected readonly shaking = signal(false);
  protected readonly shareLabel = signal('Share');
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
      case 'expired':
        return 'Round unavailable';
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
  private readonly confirmDialog = viewChild.required<ElementRef<HTMLDialogElement>>('confirmDialog');

  constructor() {
    this.game.resume();

    effect(() => {
      if (this.game.status() !== 'expired') return;
      const dialog = this.hintDialog().nativeElement;
      if (dialog.open && typeof dialog.close === 'function') dialog.close();
      else dialog.removeAttribute('open');
    });

    // A turned-away word shakes the row once.
    effect((onCleanup) => {
      if (!this.game.rejected()) return;
      this.shaking.set(true);
      const timer = setTimeout(() => this.shaking.set(false), 420);
      onCleanup(() => { clearTimeout(timer); this.shaking.set(false); });
    });

    effect((onCleanup) => {
      if (!this.game.message()) return;
      const timer = setTimeout(() => this.game.message.set(''), MESSAGE_MS);
      onCleanup(() => clearTimeout(timer));
    });
  }

  /** A new word right away, or after asking when it would throw away a game with guesses on the board. */
  protected requestNewGame(length: WordLength = this.game.wordLength()): void {
    if (this.game.busy()) return;
    if (!this.game.inProgress()) {
      this.game.newGame(length);
      return;
    }
    this.pendingLength.set(length);
    openDialog(this.confirmDialog().nativeElement);
  }

  protected confirmNewGame(): void {
    const length = this.pendingLength() ?? this.game.wordLength();
    this.confirmDialog().nativeElement.close();
    this.game.newGame(length);
  }

  protected open(dialog: HTMLDialogElement): void {
    openDialog(dialog);
  }

  protected async share(): Promise<void> {
    const result = this.game.result();
    if (!result) return;
    const rows = this.game.attempts().map((a) => a.score.map((s) => (s === 'correct' ? '🟩' : s === 'present' ? '🟨' : '⬜')).join(''));
    const text = [`Know Ball · Spordle ${result.won ? result.guesses : 'X'}/6 (${this.game.wordLength()} letters)`, ...rows,
      'https://knowball.nathanzimmerman.com/spordle'].join('\n');
    const outcome = await shareText(text);
    this.shareLabel.set(outcome === 'copied' ? 'Copied' : outcome === 'failed' ? 'Could not copy' : 'Share');
    if (outcome !== 'shared') setTimeout(() => this.shareLabel.set('Share'), 2500);
  }

  protected barWidth(count: number): number {
    const most = Math.max(...this.stats().distribution, 1);
    return Math.round((count / most) * 100);
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

  protected reset(scope: WordLength | 'all'): void {
    resetStats(scope);
    this.resetting.set(false);
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
