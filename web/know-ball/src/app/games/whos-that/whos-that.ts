import { Component, computed, inject, input } from '@angular/core';
import { WhosThatStage, WhosThatClues, WhosThatAnswer } from './whos-that-stage';
import { GameState } from '../../core/game-api';
import { GameStore } from '../../core/game-store';
import { loadFromRoute } from '../../core/puzzle-route';
import { GameShell } from '../../shared/game-shell/game-shell';

interface Rung {
  label: string;
  /** What the rung shows once reached; null while locked. */
  value: string | null;
  locked: string;
}

/** The hint ladder: the outline is free, and each wrong guess releases the next rung. */
export function ladder(clues: WhosThatClues | null): Rung[] {
  return [
    { label: 'Silhouette', value: 'Shown', locked: '' },
    { label: 'Year', value: clues?.year ? String(clues.year) : null, locked: 'After 1 miss' },
    { label: 'Team', value: clues?.team ?? null, locked: 'After 2 misses' },
    { label: 'Blurred photo', value: clues?.blurredUrl ? 'Shown' : null, locked: 'After 3 misses' },
  ];
}

/**
 * Who's That Sportsman?: name the player from the outline of an iconic photo. Five guesses;
 * wrong ones release the year, the team and then the photo itself, blurred.
 */
@Component({
  selector: 'app-whos-that',
  imports: [GameShell, WhosThatStage],
  providers: [GameStore],
  template: `
    <app-game-shell name="Who's That Sportsman?" wordmark="Who's That?" trim="var(--navy)" includeInactive [shareRows]="rows()">
      <div help>
        <p>This is the outline of a player in a famous photo. Name him in five guesses.</p>
        <p>The answer can be anyone who has played in the league, past or present.</p>
        <p>Each wrong guess releases the next hint: the year of the photo, then the team, then the photo itself, blurred.</p>
        <p>The full photo and its story are shown at the end.</p>
      </div>

      @if (state()?.answer; as a) {
        <span answer class="facts">{{ a.team }}, {{ a.year }}</span>
      }

      @if (state(); as s) {
        <app-whos-that-stage [clues]="s.clues" [reveal]="s.answer" />

        <ol class="ladder" aria-label="Hints">
          @for (rung of rungs(); track rung.label) {
            <li [class.done]="rung.value">
              <b>{{ $index + 1 }}</b>
              {{ rung.label }}
              <em>{{ rung.value ?? rung.locked }}</em>
            </li>
          }
        </ol>

        @if (misses().length) {
          <ol class="misses" aria-label="Wrong guesses, newest first">
            @for (g of misses(); track g.playerId) {
              <li><s>{{ g.name }}</s></li>
            }
          </ol>
        }
      }
    </app-game-shell>
  `,
  styles: `
    .ladder { list-style: none; margin: 4px 0 0; padding: 0; display: grid; gap: 6px; }
    .ladder li { display: flex; align-items: center; gap: 10px; color: var(--muted); font-weight: 700; }
    .ladder b {
      flex: none; width: 26px; height: 26px; border: 2px solid var(--navy); border-radius: 50%; display: grid; place-items: center;
      font: 400 13px/1 var(--display); color: var(--navy);
    }
    .ladder em { margin-left: auto; font-style: normal; font-weight: 500; text-align: right; }
    .ladder .done { color: var(--text); }
    .ladder .done b { background: var(--navy); color: var(--cream); }
    .ladder .done em { font-weight: 700; color: var(--accent); }

    .misses { list-style: none; margin: 4px 0 0; padding: 0; display: flex; flex-wrap: wrap; gap: 6px; }
    .misses li { padding: 3px 10px; border: 2px solid var(--miss-line); border-radius: 4px; background: var(--miss); font-weight: 700; }
    .misses s { text-decoration-color: var(--bad); text-decoration-thickness: 2px; }
    .facts { color: var(--muted); font-size: 16px; }
  `,
})
export class WhosThat {
  /** From the address: a past puzzle's id; absent for today's. */
  readonly puzzleId = input<string>();

  private readonly store = inject(GameStore);

  protected readonly state = computed(() => this.store.state() as GameState<unknown, WhosThatClues, null, WhosThatAnswer> | null);
  protected readonly rungs = computed(() => ladder(this.state()?.clues ?? null));
  protected readonly misses = computed(() => (this.state()?.guesses ?? []).filter((g) => !g.correct).reverse());
  /** One square per guess: black for a miss, green for the one that got it. */
  protected readonly rows = computed(() => [(this.state()?.guesses ?? []).map((g) => (g.correct ? '🟩' : '⬛')).join('')]);

  constructor() {
    loadFromRoute(this.store, 'whos-that', this.puzzleId);
  }
}
