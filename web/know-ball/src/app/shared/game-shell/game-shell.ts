import { DatePipe } from '@angular/common';
import { Component, ElementRef, booleanAttribute, computed, effect, inject, input, signal, viewChild, ChangeDetectionStrategy } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ArchiveEntry, GameApi, GameState, GameStats } from '../../core/game-api';
import { GameStore } from '../../core/game-store';
import { shareHeadline, shareText } from '../../core/share';
import { Avatar } from '../avatar/avatar';
import { Countdown } from '../countdown/countdown';
import { PlayerSearch } from '../player-search/player-search';

const NOTICE_MS = 3500;

/**
 * The frame around every daily game: title bar with the puzzle number, help, stats and the
 * archive; the result once the game is over; and the guess box at the top, under the title, where a phone keyboard cannot cover it.
 * The game itself is projected in. Content marked `help` goes in the how-to-play dialog and
 * content marked `answer` sits under the answer's name.
 */
@Component({
  selector: 'app-game-shell',
  imports: [Avatar, Countdown, DatePipe, PlayerSearch, RouterLink],
  template: `
    <section class="game" [style.--trim]="trim()" [attr.aria-label]="name()">
      <header class="top">
        <h1 class="kb-wordmark" [attr.aria-label]="name()">{{ wordmark() || name() }}</h1>
        <div class="actions">
          <button type="button" class="kb-btn" aria-label="How to play" title="How to play" (click)="open(helpDialog)">?</button>
          <button type="button" class="kb-btn" aria-label="Statistics" title="Statistics" (click)="openStats()">
            <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M2 14V8h3v6zm4.5 0V2h3v12zM11 14V5h3v9z" /></svg>
          </button>
          <button type="button" class="kb-btn" aria-label="Past puzzles" title="Past puzzles" (click)="openArchive()">
            <svg viewBox="0 0 16 16" aria-hidden="true">
              <path d="M4 1h2v2h4V1h2v2h2v12H2V3h2zm8 6H4v6h8zM5.5 8.5h2v2h-2z" fill-rule="evenodd" />
            </svg>
          </button>
        </div>
      </header>

      @switch (store.phase()) {
        @case ('loading') {
          <p class="state" role="status">Loading the puzzle…</p>
        }
        @case ('error') {
          <div class="state" role="alert">
            <p>The puzzle could not be loaded.</p>
            <button type="button" class="kb-btn primary" (click)="store.retry()">Try again</button>
          </div>
        }
        @case ('unavailable') {
          <div class="state">
            <p>{{ store.reason() }}</p>
            <a class="kb-btn" routerLink="/">All games</a>
            @if (practice()) {
              <button type="button" class="kb-btn" [disabled]="store.busy()" (click)="startPractice()">Practice round</button>
            }
          </div>
        }
        @case ('ready') {
          @let s = store.state()!;
          <div class="meta">
            <span class="kb-label">
              @if (s.practice) {
                Practice round
              } @else {
                No. {{ s.number }} · {{ s.date | date: 'EEE, MMM d' }}
              }
            </span>
            <span class="dots" role="img" [attr.aria-label]="s.guesses.length + ' of ' + s.maxGuesses + ' guesses used'">
              @for (g of dots(); track $index) {
                <i [class]="g"></i>
              }
            </span>
          </div>

          @if (!store.over()) {
            <div class="guess">
              <app-player-search
                label="Guess a player"
                [placeholder]="'Guess ' + (s.guesses.length + 1) + ' of ' + s.maxGuesses + ': type a name'"
                [includeInactive]="includeInactive()" [puzzleId]="s.puzzleId"
                [disabled]="store.busy()"
                (selected)="store.guess($event.id)"
              />
            </div>
          }

          @if (practice() && !store.over()) {
            <nav class="modes" aria-label="Game mode">
              @if (s.practice) {
                <a class="kb-btn" [routerLink]="['/', s.game]" [attr.aria-disabled]="store.busy()" (click)="store.busy() && $event.preventDefault()">Today's puzzle</a>
              } @else {
                <button type="button" class="kb-btn" [disabled]="store.busy()" (click)="startPractice()">Practice round</button>
              }
              <p class="fine">Practice does not affect daily stats. Your daily progress stays saved.</p>
            </nav>
          }

          @if (store.over()) {
            <div class="result" [class.won]="s.status === 'won'" role="status">
              <app-avatar [src]="$safeNavigationMigration(s.answer?.headshotUrl)" [name]="s.answer?.name ?? ''" />
              <div class="who">
                <span class="kb-label">{{ verdict(s) }}</span>
                <strong>{{ s.answer?.name }}</strong>
                <ng-content select="[answer]" />
              </div>
              <div class="next">
                <button type="button" class="kb-btn primary" (click)="share(s)">{{ shareLabel() }}</button>
                @if (practice()) {
                  <button type="button" class="kb-btn" [disabled]="store.busy()" (click)="startPractice()">Practice round</button>
                }
                @if (s.nextPuzzleAt) {
                  <span class="count">Next puzzle in <app-countdown [until]="s.nextPuzzleAt" (done)="store.load(s.game)" /></span>
                } @else {
                  <a class="kb-btn" [routerLink]="['/', s.game]">Today's puzzle</a>
                }
              </div>
            </div>
          }

          <ng-content />

        }
      }

      @if (store.notice(); as notice) {
        <p class="kb-toast" role="status">{{ notice }}</p>
      }
    </section>

    <dialog #helpDialog class="kb-dialog" aria-labelledby="shell-help-title" (click)="closeOnBackdrop($event)">
      <div class="kb-dialog-head">
        <h2 id="shell-help-title">How to play</h2>
        <button type="button" class="kb-close" aria-label="Close" (click)="helpDialog.close()">×</button>
      </div>
      <div class="help"><ng-content select="[help]" /></div>
      <p class="fine">A new puzzle arrives at midnight Eastern. Your progress is saved in this browser.</p>
    </dialog>

    <dialog #statsDialog class="kb-dialog" aria-labelledby="shell-stats-title" (click)="closeOnBackdrop($event)">
      <div class="kb-dialog-head">
        <h2 id="shell-stats-title">Statistics</h2>
        <button type="button" class="kb-close" aria-label="Close" (click)="statsDialog.close()">×</button>
      </div>
      @if (stats(); as st) {
        <dl class="stats">
          <div><dd>{{ st.played }}</dd><dt>Played</dt></div>
          <div><dd>{{ winRate(st) }}%</dd><dt>Win %</dt></div>
          <div><dd>{{ st.currentStreak }}</dd><dt>Streak</dt></div>
          <div><dd>{{ st.bestStreak }}</dd><dt>Best</dt></div>
        </dl>
        <h3 class="kb-label">Guesses to solve</h3>
        <ol class="bars">
          @for (count of st.distribution; track $index) {
            <li>
              <span>{{ $index + 1 }}</span>
              <i [style.width.%]="barWidth(st, count)" [class.zero]="count === 0">{{ count }}</i>
            </li>
          }
        </ol>
        <p class="fine">Daily puzzles only. Practice rounds are not counted.</p>
      } @else {
        <p role="status">{{ dialogError() || 'Loading…' }}</p>
      }
    </dialog>

    <dialog #archiveDialog class="kb-dialog" aria-labelledby="shell-archive-title" (click)="closeOnBackdrop($event)">
      <div class="kb-dialog-head">
        <h2 id="shell-archive-title">Past puzzles</h2>
        <button type="button" class="kb-close" aria-label="Close" (click)="archiveDialog.close()">×</button>
      </div>
      @if (archive(); as entries) {
        <ol class="archive">
          @for (entry of entries; track entry.puzzleId) {
            <li>
              <a [routerLink]="['/', store.game(), 'puzzle', entry.puzzleId]" (click)="archiveDialog.close()">
                <b>No. {{ entry.number }}</b>
                <span>{{ entry.date | date: 'MMM d' }}</span>
                <em [class]="entry.status">{{ archiveLabel(entry) }}</em>
              </a>
            </li>
          } @empty {
            <li>No puzzles yet.</li>
          }
        </ol>
      } @else {
        <p role="status">{{ dialogError() || 'Loading…' }}</p>
      }
    </dialog>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    :host { display: block; }
    .game { max-width: 520px; min-height: calc(100dvh - 120px); margin: 0 auto; display: flex; flex-direction: column; gap: 12px; }

    .top, .meta { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
    .kb-wordmark { font-size: clamp(30px, 9.4vw, 46px); }
    .actions { display: flex; gap: 6px; }
    /* Round icon buttons, so the title keeps the row to itself on a phone. */
    .actions .kb-btn { width: 36px; height: 36px; padding: 0; display: grid; place-items: center; font-size: 17px; }
    .actions svg { width: 16px; height: 16px; fill: currentColor; }
    .meta { padding-bottom: 8px; border-bottom: 4px solid var(--trim); }
    .dots { display: flex; gap: 4px; }
    .dots i { width: 11px; height: 11px; border: 2px solid var(--navy); border-radius: 50%; background: var(--surface); }
    .dots .miss { background: var(--navy); }
    .dots .hit { background: var(--ok); }

    .state { margin: 40px 0; text-align: center; font-size: 19px; }
    .state p { margin: 0 0 14px; }

    /* The answer, framed like the front of a card in the game's own trim colour. */
    .result {
      display: grid; grid-template-columns: auto 1fr; gap: 10px 14px; align-items: center; padding: 14px;
      background: var(--surface); border: 5px solid var(--trim); border-radius: 8px;
      box-shadow: inset 0 0 0 2px var(--cream), inset 0 0 0 4px var(--navy);
    }
    .result app-avatar { --size: 64px; }
    .who { display: grid; gap: 2px; min-width: 0; }
    .who .kb-label { color: var(--bad); }
    .won .who .kb-label { color: var(--ok); }
    .who strong { font: 400 23px/1.1 var(--display); overflow-wrap: anywhere; }
    .next { grid-column: 1 / -1; display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
    .count { margin-left: auto; font-size: 16px; white-space: nowrap; }

    .help { display: grid; gap: 10px; }
    .fine { margin: 14px 0 0; color: var(--muted); font-size: 15px; }
    .stats { display: grid; grid-template-columns: repeat(4, 1fr); gap: 8px; margin: 0 0 16px; }
    .stats div { padding: 10px 4px; border: 2px solid var(--navy); border-radius: 4px; background: var(--cream); text-align: center; }
    .stats dd { margin: 0; font: 400 24px/1.1 var(--display); }
    .stats dt { margin-top: 3px; font-size: 12px; font-weight: 700; letter-spacing: .06em; text-transform: uppercase; color: var(--muted); }
    .bars { list-style: none; margin: 8px 0 0; padding: 0; display: grid; gap: 4px; }
    .bars li { display: grid; grid-template-columns: 14px 1fr; align-items: center; gap: 8px; font-weight: 700; }
    .bars i {
      min-width: 26px; padding: 1px 7px; border-radius: 3px; background: var(--trim, var(--navy)); color: var(--cream);
      font-style: normal; font-size: 15px; text-align: right; box-sizing: border-box;
    }
    .bars .zero { background: var(--miss); color: var(--miss-ink); }

    .archive { list-style: none; margin: 0; padding: 0; max-height: 55dvh; overflow-y: auto; }
    .archive a {
      display: grid; grid-template-columns: 76px 1fr auto; gap: 8px; align-items: baseline; padding: 8px 6px;
      border-bottom: 1px solid var(--tan); text-decoration: none;
    }
    .archive a:hover { background: var(--surface-2); }
    .archive span { color: var(--muted); }
    .archive em { font-style: normal; font-weight: 700; font-size: 15px; }
    .archive .won { color: var(--ok); }
    .archive .lost { color: var(--bad); }
    .archive .new { color: var(--muted); font-weight: 500; }

    @media (max-width: 420px) {
      .result .kb-btn { padding: 6px 10px; font-size: 12px; }
      .result app-avatar { --size: 52px; }
      .count { margin-left: 0; }
    }
  `,
})
export class GameShell {
  protected readonly store = inject(GameStore);
  private readonly api = inject(GameApi);
  private readonly router = inject(Router);

  readonly name = input.required<string>();
  /** A shorter form of the name for the title, when the full one will not fit a phone. */
  readonly wordmark = input('');
  /** The game's colour from the card set: the rule under the title and the border of the result. */
  readonly trim = input('var(--navy)');
  /** Whether the guess box offers retired players. */
  readonly includeInactive = input(false, { transform: booleanAttribute });
  /** Whether a practice round can follow the daily. */
  readonly practice = input(false, { transform: booleanAttribute });
  /** The emoji rows under the headline when the result is shared. */
  readonly shareRows = input<string[]>([]);

  protected readonly stats = signal<GameStats | null>(null);
  protected readonly archive = signal<ArchiveEntry[] | null>(null);
  protected readonly dialogError = signal('');
  protected readonly shareLabel = signal('Share');

  /** One dot per allowed guess: a miss, the winning guess, or still to come. */
  protected readonly dots = computed(() => {
    const state = this.store.state();
    if (!state) return [];
    return Array.from({ length: state.maxGuesses }, (_, i) => {
      const guess = state.guesses[i];
      return guess ? (guess.correct ? 'hit' : 'miss') : '';
    });
  });

  private readonly statsDialog = viewChild.required<ElementRef<HTMLDialogElement>>('statsDialog');
  private readonly archiveDialog = viewChild.required<ElementRef<HTMLDialogElement>>('archiveDialog');

  constructor() {
    effect((onCleanup) => {
      if (!this.store.notice()) return;
      const timer = setTimeout(() => this.store.notice.set(''), NOTICE_MS);
      onCleanup(() => clearTimeout(timer));
    });
  }

  protected verdict(state: GameState): string {
    if (state.status !== 'won') return 'The answer was';
    return state.guesses.length === 1 ? 'Solved on the first guess' : `Solved in ${state.guesses.length} guesses`;
  }

  protected startPractice(): void {
    this.store.practice((puzzleId) => void this.router.navigate(['/', this.store.game(), 'puzzle', puzzleId]));
  }

  protected async share(state: GameState): Promise<void> {
    const won = state.status === 'won';
    const text = [shareHeadline(this.name(), state.practice ? null : state.number, state.guesses.length, state.maxGuesses, won), ...this.shareRows()].join('\n');
    const outcome = await shareText(text);
    this.shareLabel.set(outcome === 'copied' ? 'Copied' : outcome === 'failed' ? 'Could not copy' : 'Share');
    if (outcome !== 'shared') setTimeout(() => this.shareLabel.set('Share'), 2500);
  }

  protected openStats(): void {
    this.stats.set(null);
    this.dialogError.set('');
    this.open(this.statsDialog().nativeElement);
    this.api.stats().subscribe({
      next: (all) => this.stats.set(all.find((s) => s.game === this.store.game()) ?? null),
      error: () => this.dialogError.set('Your statistics could not be loaded.'),
    });
  }

  protected openArchive(): void {
    this.archive.set(null);
    this.dialogError.set('');
    this.open(this.archiveDialog().nativeElement);
    this.api.archive(this.store.game()).subscribe({
      next: (entries) => this.archive.set(entries),
      error: () => this.dialogError.set('The archive could not be loaded.'),
    });
  }

  protected winRate(stats: GameStats): number {
    return stats.played ? Math.round((stats.wins / stats.played) * 100) : 0;
  }

  protected barWidth(stats: GameStats, count: number): number {
    const most = Math.max(...stats.distribution, 1);
    return Math.round((count / most) * 100);
  }

  protected archiveLabel(entry: ArchiveEntry): string {
    switch (entry.status) {
      case 'won':
        return `Solved in ${entry.guesses}`;
      case 'lost':
        return 'Missed';
      case 'playing':
        return `${entry.guesses} guessed`;
      default:
        return 'Not played';
    }
  }

  protected open(dialog: HTMLDialogElement): void {
    if (dialog.open) return;
    // Older browsers and test DOMs may lack showModal.
    if (typeof dialog.showModal === 'function') dialog.showModal();
    else dialog.setAttribute('open', '');
  }

  /** A click on the dialog element itself, rather than its content, landed on the backdrop. */
  protected closeOnBackdrop(event: MouseEvent): void {
    const dialog = event.currentTarget as HTMLDialogElement;
    if (event.target === dialog) dialog.close();
  }
}
