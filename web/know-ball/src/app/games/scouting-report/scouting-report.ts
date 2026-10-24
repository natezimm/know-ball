import { Component, computed, inject, input, ChangeDetectionStrategy } from '@angular/core';
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

/**
 * The seven clues in card order, with each one's share of a twelve-column row. The top row is
 * team, conference and division; the team gets the most room because "Timberwolves" needs it.
 */
export const CLUES: { key: ClueKey; label: string; span: number }[] = [
  { key: 'team', label: 'Team', span: 5 },
  { key: 'conference', label: 'Conf', span: 3 },
  { key: 'division', label: 'Division', span: 4 },
  { key: 'position', label: 'Pos', span: 3 },
  { key: 'height', label: 'Height', span: 3 },
  { key: 'age', label: 'Age', span: 3 },
  { key: 'number', label: 'No.', span: 3 },
];

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
        <p>Every guess gets a card with seven clues, coloured by how that player compares with the answer:</p>
        <ul class="key">
          <li><i class="ok"></i><span><b>Teal</b> is the same as the answer.</span></li>
          <li>
            <i class="close"></i>
            <span><b>Mustard</b> is close: shares a position, or within 2 inches, 2 years or 3 jersey numbers.</span>
          </li>
          <li><i class="miss"></i><span><b>Tan</b> is wrong.</span></li>
        </ul>
        <p>An arrow on height, age or number points toward the answer: ↑ means the answer is higher.</p>
      </div>

      @if (answerLine(); as line) {
        <span answer class="facts">{{ line }}</span>
      }

      @if (state(); as s) {
        @if (!s.guesses.length) {
          <div class="card empty">
            <app-avatar name="?" />
            <div>
              <b>Mystery player</b>
              <ul class="chips">
                @for (c of clues; track c.key) {
                  <li [style.grid-column]="'span ' + c.span"><small>{{ c.label }}</small>?</li>
                }
              </ul>
            </div>
          </div>
          <p class="tip">Guess any current player. His card shows how close each of these seven is.</p>
        }

        <ol class="cards" aria-label="Your guesses, newest first">
          @for (g of newestFirst(); track g.playerId) {
            <li class="card" [class.right]="g.correct">
              <app-avatar [src]="g.headshotUrl" [name]="g.name" />
              <div>
                <b>{{ g.name }}</b>
                @if (g.feedback; as f) {
                  <ul class="chips">
                    @for (c of clues; track c.key) {
                      @let clue = f[c.key];
                      <li [class]="clue.state" [style.grid-column]="'span ' + c.span">
                        <small>{{ c.label }}</small>
                        <span>
                          {{ c.key === 'team' ? (clue.label ?? clue.value) : clue.value }}
                          @if (clue.arrow) {
                            <i aria-hidden="true">{{ clue.arrow === 'up' ? '↑' : '↓' }}</i>
                          }
                        </span>
                        <span class="visually-hidden">{{ describe(clue) }}</span>
                      </li>
                    }
                  </ul>
                }
              </div>
            </li>
          }
        </ol>
      }
    </app-game-shell>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .cards { list-style: none; margin: 0; padding: 0; display: grid; gap: 10px; }
    /* Each guess is a card from the set: teal trim, cream gap, navy line. */
    .card {
      display: flex; gap: 10px; padding: 12px; background: var(--surface); border: 4px solid var(--teal); border-radius: 8px;
      box-shadow: inset 0 0 0 2px var(--cream), inset 0 0 0 3.5px var(--navy);
    }
    .card.right { border-color: var(--mustard); }
    .card > div { flex: 1; min-width: 0; }
    .card b { font: 400 18px/1.15 var(--display); }
    app-avatar { --size: 48px; }

    .chips { list-style: none; margin: 8px 0 0; padding: 0; display: grid; grid-template-columns: repeat(12, 1fr); gap: 5px; }
    .chips li {
      display: grid; align-content: start; gap: 1px; min-width: 0; padding: 4px 6px 5px;
      border: 2px solid var(--miss-line); border-radius: 4px; background: var(--miss); color: var(--miss-ink);
      font-size: 15px; font-weight: 700; line-height: 1.1;
    }
    .chips small { font-size: 10px; letter-spacing: .08em; text-transform: uppercase; opacity: .85; }
    .chips i { font-style: normal; }
    .chips .ok { background: var(--ok); color: var(--ok-ink); border-color: var(--navy); }
    .chips .close { background: var(--close); color: var(--close-ink); border-color: var(--navy); }

    .empty { border-style: dashed; box-shadow: none; }
    .empty .chips li { border-style: dashed; background: none; }
    .tip { margin: 0; color: var(--muted); font-size: 17px; }
    .facts { color: var(--muted); font-size: 16px; }

    .key { list-style: none; margin: 0; padding: 0; display: grid; gap: 6px; }
    .key li { display: flex; gap: 10px; align-items: baseline; }
    .key i { flex: none; width: 16px; height: 16px; border: 2px solid var(--navy); border-radius: 3px; translate: 0 2px; }
    .key .ok { background: var(--ok); }
    .key .close { background: var(--close); }
    .key .miss { background: var(--miss); }

    @media (max-width: 400px) {
      .card { padding: 10px; gap: 8px; }
      app-avatar { --size: 40px; }
      .chips li { padding: 4px 4px 5px; font-size: 14px; }
    }
  `,
})
export class ScoutingReport {
  /** From the address: a past or practice puzzle's id; absent for today's. */
  readonly puzzleId = input<string>();

  private readonly store = inject(GameStore);
  protected readonly clues = CLUES;

  protected readonly state = computed(() => this.store.state() as GameState<unknown, null, ScoutingFeedback, ScoutingAnswer> | null);
  protected readonly newestFirst = computed(() => [...(this.state()?.guesses ?? [])].reverse());
  protected readonly rows = computed(() => shareRows(this.state()?.guesses ?? []));
  protected readonly answerLine = computed(() => {
    const a = this.state()?.answer;
    if (!a) return '';
    return [a.teamName, a.position, a.height, a.age === null ? null : `${a.age} years old`, a.number === null ? null : `#${a.number}`]
      .filter(Boolean)
      .join(' · ');
  });

  constructor() {
    loadFromRoute(this.store, 'scouting-report', this.puzzleId);
  }

  /** The colour and arrow in words, for screen readers. */
  protected describe(clue: Clue): string {
    const direction = clue.arrow === 'up' ? ', the answer is higher' : clue.arrow === 'down' ? ', the answer is lower' : '';
    return `, ${STATE_WORDS[clue.state]}${direction}`;
  }
}
