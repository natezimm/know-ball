import { Component, computed, effect, inject, input, signal, untracked, ChangeDetectionStrategy } from '@angular/core';
import { GameState, GuessRecord } from '../../core/game-api';
import { GameStore } from '../../core/game-store';
import { loadFromRoute } from '../../core/puzzle-route';
import { Avatar } from '../../shared/avatar/avatar';
import { GameShell } from '../../shared/game-shell/game-shell';

type ClueState = 'ok' | 'close' | 'miss';

interface Clue {
  value: string;
  state: ClueState;
  /** "up" when the answer is higher than the guess, "down" when lower. */
  arrow?: 'up' | 'down' | null;
  label?: string | null;
  /** The team's logo, on the team clue. */
  image?: string | null;
}

type ClueKey = 'team' | 'conference' | 'division' | 'position' | 'height' | 'age' | 'number';
export type ScoutingFeedback = Record<ClueKey, Clue>;

interface ScoutingAnswer {
  playerId: number;
  name: string;
  headshotUrl: string | null;
  teamName: string | null;
  position: string | null;
  height: string | null;
  age: number | null;
  number: string | null;
}

/** The seven clues in column order, with the short header shown above each column. */
export const CLUES: { key: ClueKey; label: string; header: string }[] = [
  { key: 'team', label: 'Team', header: 'Team' },
  { key: 'conference', label: 'Conference', header: 'Conf' },
  { key: 'division', label: 'Division', header: 'Div' },
  { key: 'position', label: 'Position', header: 'Pos' },
  { key: 'height', label: 'Height', header: 'Ht' },
  { key: 'age', label: 'Age', header: 'Age' },
  { key: 'number', label: 'Number', header: '#' },
];

/** Division names are too long for a seventh of a phone screen. */
const DIVISIONS: Record<string, string> = {
  Atlantic: 'ATL', Central: 'CEN', Southeast: 'SE', Northwest: 'NW', Pacific: 'PAC', Southwest: 'SW',
};

const SQUARES: Record<ClueState, string> = { ok: '🟩', close: '🟨', miss: '⬜' };
const STATE_WORDS: Record<ClueState, string> = { ok: 'right', close: 'close', miss: 'wrong' };

/** One row of seven squares per guess, oldest first: the share grid. */
export function shareRows(guesses: GuessRecord<ScoutingFeedback>[]): string[] {
  return guesses
    .filter((g) => g.feedback)
    .map((g) => CLUES.map((c) => SQUARES[g.feedback![c.key].state]).join(''));
}

/** Scouting Report: guess the current player in eight tries from seven clues per guess. */
@Component({
  selector: 'app-scouting-report',
  imports: [Avatar, GameShell],
  providers: [GameStore],
  template: `
    <app-game-shell name="Scouting Report" trim="var(--teal)" practice [shareRows]="rows()">
      <div help>
        <p>Guess the current NBA player in eight tries. Start with anyone.</p>
        <p>Every guess adds a row of seven clues, coloured by how that player compares with the answer:</p>
        <ul class="key">
          <li><i class="ok"></i><span><b>Teal</b> is the same as the answer.</span></li>
          <li>
            <i class="close"></i>
            <span>
              <b>Mustard</b> is close: a team the answer used to play for, a shared position, or within 2 inches,
              2 years or 2 jersey numbers.
            </span>
          </li>
          <li><i class="miss"></i><span><b>Tan</b> is wrong.</span></li>
        </ul>
        <p>An arrow on height, age or number points toward the answer: ↑ means the answer is higher.</p>
      </div>

      @if (answerLine(); as line) {
        <span answer class="facts">{{ line }}</span>
      }

      @if (state(); as s) {
        <p class="tip">{{ s.clues?.rosterSnapshot ? 'Clues and search use the roster saved when this puzzle was created.' : 'Older puzzle: the answer is frozen; other players use current roster data.' }}</p>
        <div class="board" role="table" aria-label="Your guesses, oldest first">
          <div class="cols" role="row">
            @for (c of clues; track c.key) {
              <span role="columnheader" [attr.aria-label]="c.label">{{ c.header }}</span>
            }
          </div>

          @for (g of s.guesses; track g.playerId; let i = $index) {
            <div class="row" role="row" [class.right]="g.correct" [class.fresh]="i >= settled()">
              <div class="who" role="rowheader">
                <app-avatar [src]="g.headshotUrl" [name]="g.name" />
                <b>{{ g.name }}</b>
              </div>
              @if (g.feedback; as f) {
                @for (c of clues; track c.key; let col = $index) {
                  @let clue = f[c.key];
                  <div role="cell" class="cell" [class]="clue.state" [style.--col]="col">
                    @if (c.key === 'team' && showLogo(clue)) {
                      <img [src]="clue.image" alt="" [title]="clue.label ?? clue.value" (error)="logoFailed(clue)" />
                      <small>{{ clue.value }}</small>
                    } @else {
                      <span class="value" [title]="c.key === 'division' ? clue.value : null">{{ short(c.key, clue) }}</span>
                    }
                    @if (clue.arrow) {
                      <i class="arrow" [class]="clue.arrow" aria-hidden="true">{{ clue.arrow === 'up' ? '▲' : '▼' }}</i>
                    }
                    <span class="visually-hidden">{{ describe(c.label, clue) }}</span>
                  </div>
                }
              }
            </div>
          }

          @if (!over()) {
            @for (n of blanks(); track n; let first = $first) {
              <div class="row blank" [class.next]="first" aria-hidden="true">
                <div class="who">
                  <app-avatar name="?" />
                  <span>{{ first && !s.guesses.length ? 'Mystery player' : 'Guess ' + n }}</span>
                </div>
                @for (c of clues; track c.key) {
                  <div class="cell"></div>
                }
              </div>
            }
          }
        </div>

        @if (!s.guesses.length) {
          <p class="tip">Guess any current player. Each row shows how close that player is on all seven clues.</p>
        }
      }
    </app-game-shell>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    /* The board: one header, then one row per guess, oldest at the top. */
    .board { display: grid; gap: 8px; }
    .cols, .row { display: grid; grid-template-columns: repeat(7, minmax(0, 1fr)); gap: 4px; }
    .cols {
      position: sticky; top: 0; z-index: 1; padding: 4px 0; background: var(--bg);
      font: 400 13px/1 var(--display); letter-spacing: .06em; text-transform: uppercase; text-align: center; color: var(--navy);
    }
    .who { grid-column: 1 / -1; display: flex; align-items: center; gap: 8px; min-width: 0; }
    .who b { font: 400 17px/1.15 var(--display); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .who span { color: var(--muted); font-size: 15px; }
    app-avatar { --size: 28px; }

    .cell {
      position: relative; display: grid; place-items: center; align-content: center; gap: 1px; min-width: 0; height: 54px;
      padding: 2px; border: 2px solid var(--miss-line); border-radius: 4px; background: var(--miss); color: var(--miss-ink);
      font-size: 15px; font-weight: 700; line-height: 1.1; text-align: center;
    }
    .cell.ok { background: var(--ok); color: var(--ok-ink); border-color: var(--navy); }
    .cell.close { background: var(--close); color: var(--close-ink); border-color: var(--navy); }
    .row.right .cell { box-shadow: 0 0 0 2px var(--mustard); }
    .value { max-width: 100%; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .cell img { width: 26px; height: 26px; object-fit: contain; }
    .cell small { font-size: 10px; letter-spacing: .04em; }

    /* The arrow keeps nudging toward the answer: up for higher, down for lower. */
    .arrow { font-style: normal; font-size: 11px; line-height: 1; animation: nudge-up 1.1s ease-in-out infinite; }
    .arrow.down { animation-name: nudge-down; }
    @keyframes nudge-up { 0%, 100% { translate: 0 1px; } 50% { translate: 0 -3px; } }
    @keyframes nudge-down { 0%, 100% { translate: 0 -1px; } 50% { translate: 0 3px; } }

    /* A new guess turns its cells over one at a time, left to right. */
    .row.fresh .cell { animation: flip .45s ease-out both; animation-delay: calc(var(--col) * 120ms); }
    .row.fresh .arrow { animation-delay: calc(var(--col) * 120ms + .45s); }
    @keyframes flip { from { transform: rotateX(90deg); } to { transform: none; } }

    .blank .cell { border-style: dashed; background: none; opacity: .45; }
    .blank.next .cell { opacity: 1; border-color: var(--navy); }
    /* Rows still to come are thin strips, so the board stays short on a phone. */
    .blank:not(.next) .who { display: none; }
    .blank:not(.next) .cell { height: 14px; }

    @media (prefers-reduced-motion: reduce) {
      .arrow, .row.fresh .cell { animation: none; }
    }

    .tip { margin: 0; color: var(--muted); font-size: 17px; }
    .facts { color: var(--muted); font-size: 16px; }

    .key { list-style: none; margin: 0; padding: 0; display: grid; gap: 6px; }
    .key li { display: flex; gap: 10px; align-items: baseline; }
    .key i { flex: none; width: 16px; height: 16px; border: 2px solid var(--navy); border-radius: 3px; translate: 0 2px; }
    .key .ok { background: var(--ok); }
    .key .close { background: var(--close); }
    .key .miss { background: var(--miss); }

    @media (max-width: 400px) {
      .cell { height: 50px; font-size: 14px; }
      .cols { font-size: 12px; }
    }
  `,
})
export class ScoutingReport {
  /** From the address: a past or practice puzzle's id; absent for today's. */
  readonly puzzleId = input<string>();

  private readonly store = inject(GameStore);
  protected readonly clues = CLUES;

  protected readonly state = computed(() => this.store.state() as GameState<unknown, { rosterSnapshot: boolean }, ScoutingFeedback, ScoutingAnswer> | null);
  protected readonly over = computed(() => this.store.over());
  /** Numbers of the guesses still to come, so the board always shows all eight rows. */
  protected readonly blanks = computed(() => {
    const s = this.state();
    if (!s) return [];
    return Array.from({ length: Math.max(0, s.maxGuesses - s.guesses.length) }, (_, i) => s.guesses.length + i + 1);
  });
  protected readonly rows = computed(() => shareRows(this.state()?.guesses ?? []));
  protected readonly answerLine = computed(() => {
    const a = this.state()?.answer;
    if (!a) return '';
    return [a.teamName, a.position, a.height, a.age === null ? null : `${a.age} years old`, a.number === null ? null : `#${a.number}`]
      .filter(Boolean)
      .join(' · ');
  });

  /** Rows already on the board when a puzzle loads stay still; only guesses made since turn over. */
  private readonly loaded = signal<{ puzzleId: number; guesses: number } | null>(null);
  protected readonly settled = computed(() => this.loaded()?.guesses ?? Infinity);
  private readonly brokenLogos = signal<ReadonlySet<string>>(new Set());

  constructor() {
    loadFromRoute(this.store, 'scouting-report', this.puzzleId);
    effect(() => {
      const s = this.state();
      if (s && untracked(this.loaded)?.puzzleId !== s.puzzleId) this.loaded.set({ puzzleId: s.puzzleId, guesses: s.guesses.length });
    });
  }

  protected showLogo(clue: Clue): boolean {
    return !!clue.image && !this.brokenLogos().has(clue.image);
  }

  protected logoFailed(clue: Clue) {
    if (clue.image) this.brokenLogos.update((set) => new Set(set).add(clue.image!));
  }

  /** What fits in the cell. */
  protected short(key: ClueKey, clue: Clue): string {
    return key === 'division' ? (DIVISIONS[clue.value] ?? clue.value) : clue.value;
  }

  /** The clue, its colour and its arrow in words, for screen readers. */
  protected describe(label: string, clue: Clue): string {
    const direction = clue.arrow === 'up' ? ', the answer is higher' : clue.arrow === 'down' ? ', the answer is lower' : '';
    return `${label}: ${clue.label ?? clue.value}, ${STATE_WORDS[clue.state]}${direction}`;
  }
}
