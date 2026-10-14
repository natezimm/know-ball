import { Component, computed, inject, input, signal } from '@angular/core';
import { GameState } from '../../core/game-api';
import { GameStore } from '../../core/game-store';
import { loadFromRoute } from '../../core/puzzle-route';
import { GameShell } from '../../shared/game-shell/game-shell';

interface WhosThatClues {
  silhouetteUrl: string;
  /** Each is null until enough wrong guesses have released it. */
  year: number | null;
  team: string | null;
  blurredUrl: string | null;
}

interface WhosThatAnswer {
  playerId: number;
  name: string;
  headshotUrl: string | null;
  photoUrl: string;
  caption: string | null;
  year: number;
  team: string;
}

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
  imports: [GameShell],
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
        <figure class="stage">
          <div class="frame" [class.photo]="s.answer || view() === 'blurred'">
            @if (s.answer; as a) {
              <img [src]="a.photoUrl" [alt]="'The photo: ' + a.name" />
            } @else if (view() === 'blurred') {
              <img [src]="s.clues!.blurredUrl" alt="The photo, heavily blurred" />
            } @else {
              <img [src]="s.clues?.silhouetteUrl" alt="Black outline of the mystery player" />
            }
          </div>
          @if (s.answer?.caption; as caption) {
            <figcaption>{{ caption }}</figcaption>
          }
        </figure>

        @if (!s.answer && s.clues?.blurredUrl) {
          <div class="views" role="group" aria-label="Which picture to show">
            <button type="button" class="kb-btn" [attr.aria-pressed]="view() === 'outline'" (click)="picked.set('outline')">Outline</button>
            <button type="button" class="kb-btn" [attr.aria-pressed]="view() === 'blurred'" (click)="picked.set('blurred')">Blurred photo</button>
          </div>
        }

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
    /* A spotlit stage in a card frame. The outline stands on it; photos fill it, with the caption underneath. */
    .stage {
      margin: 0; overflow: hidden; background: var(--navy);
      border: 2px solid var(--navy); border-radius: 6px; box-shadow: 0 0 0 2px var(--cream), 0 0 0 6px var(--navy);
    }
    .frame {
      aspect-ratio: 4 / 3; display: grid; place-items: end center;
      background: radial-gradient(circle at 50% 38%, #fbe9b4, var(--mustard) 52%, #9a6a12);
    }
    .frame img { max-width: 92%; max-height: 94%; display: block; }
    .frame.photo { place-items: center; background: var(--navy); }
    .frame.photo img { width: 100%; height: 100%; max-width: none; max-height: none; object-fit: contain; }
    figcaption { padding: 8px 12px 9px; color: var(--cream); font-size: 16px; font-style: italic; line-height: 1.25; }

    .views { display: flex; gap: 6px; margin-top: 4px; }
    .views [aria-pressed="true"] { background: var(--navy); color: var(--cream); }

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
  /** The picture the player asked for, once there is a choice. */
  protected readonly picked = signal<'outline' | 'blurred' | null>(null);
  /** The blurred photo takes the stage as soon as it is released; after that the player can flip between the two. */
  protected readonly view = computed(() => (this.state()?.clues?.blurredUrl ? (this.picked() ?? 'blurred') : 'outline'));
  protected readonly rungs = computed(() => ladder(this.state()?.clues ?? null));
  protected readonly misses = computed(() => (this.state()?.guesses ?? []).filter((g) => !g.correct).reverse());
  /** One square per guess: black for a miss, green for the one that got it. */
  protected readonly rows = computed(() => [(this.state()?.guesses ?? []).map((g) => (g.correct ? '🟩' : '⬛')).join('')]);

  constructor() {
    loadFromRoute(this.store, 'whos-that', this.puzzleId);
  }
}
