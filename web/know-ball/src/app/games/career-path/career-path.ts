import { Component, computed, inject, input, signal, ChangeDetectionStrategy } from '@angular/core';
import { GameState } from '../../core/game-api';
import { GameStore } from '../../core/game-store';
import { loadFromRoute } from '../../core/puzzle-route';
import { Avatar } from '../../shared/avatar/avatar';
import { GameShell } from '../../shared/game-shell/game-shell';

export interface CareerStop {
  team: string;
  abbreviation: string;
  /** Null for teams that no longer exist; they get a lettered badge. */
  logoUrl: string | null;
  /** Calendar year the spell began. */
  from: number;
  /** Calendar year the last season ended; null or left out (the API drops nulls) while the player is still there. */
  to?: number | null;
  seasons: number;
}

interface CareerHint {
  label: string;
  /** Null until enough wrong guesses have opened it. */
  value: string | null;
}

interface CareerFeedback {
  /** Positions in the path of the stops the guessed player also made. */
  shared: number[];
}

interface CareerAnswer {
  playerId: number;
  name: string;
  headshotUrl: string | null;
  alsoAccepted: number;
}

/** "2011–17", "1998–2004" across a century, "2025–" when it has not ended. */
export function stopYears(stop: Pick<CareerStop, 'from' | 'to'>): string {
  if (stop.to == null) return `${stop.from}–`;
  const sameCentury = Math.floor(stop.to / 100) === Math.floor(stop.from / 100);
  return `${stop.from}–${sameCentury ? String(stop.to % 100).padStart(2, '0') : stop.to}`;
}

/** Career Path: every team a player has been on, in order. Six guesses; each miss opens a hint. */
@Component({
  selector: 'app-career-path',
  imports: [Avatar, GameShell],
  providers: [GameStore],
  template: `
    <app-game-shell name="Career Path" trim="var(--mustard)" includeInactive practice [shareRows]="rows()">
      <div help>
        <p>These are all the teams one player has been on, in order. Name him in six guesses.</p>
        <p>The answer can be a current player or a retired one, and any player whose career ran through the same teams counts.</p>
        <p>
          Each wrong guess opens the next hint (position, draft, college, initials) and tells you which of these stops
          your guess also made.
        </p>
        <p>A team that moved or changed its name appears as it was at the time, with a lettered badge in place of a logo.</p>
      </div>

      @if (state()?.answer?.alsoAccepted; as others) {
        <span answer class="also">{{ others }} other {{ others === 1 ? 'player shares' : 'players share' }} this exact path.</span>
      }

      @if (state(); as s) {
        <h2 class="ask">Whose career is this?</h2>
        <ol class="rail">
          @for (stop of s.payload.stops; track $index) {
            <li>
              <span class="logo" [class.badge]="!showLogo(stop)">
                @if (showLogo(stop)) {
                  <img [src]="stop.logoUrl" alt="" (error)="logoFailed(stop)" />
                } @else {
                  {{ stop.abbreviation }}
                }
              </span>
              <div>
                <b>{{ stop.team }}</b>
                <small>{{ years(stop) }} · {{ stop.seasons }} {{ stop.seasons === 1 ? 'season' : 'seasons' }}</small>
                <span class="bar"><i [style.width.%]="(stop.seasons / longest()) * 100"></i></span>
              </div>
            </li>
          }
        </ol>

        <ul class="hints" aria-label="Hints">
          @for (hint of s.clues?.hints ?? []; track hint.label) {
            <li [class.open]="hint.value">
              @if (hint.value) {
                <small>{{ hint.label }}</small>{{ hint.value }}
              } @else {
                <small>Hint {{ $index + 1 }}</small>{{ hint.label }}
              }
            </li>
          }
        </ul>

        @if (misses().length) {
          <ol class="misses" aria-label="Wrong guesses, newest first">
            @for (g of misses(); track g.playerId) {
              <li>
                <app-avatar [src]="g.headshotUrl" [name]="g.name" />
                <span>
                  <s>{{ g.name }}</s>
                  <small>{{ sharedWith(s.payload.stops, g.feedback) }}</small>
                </span>
              </li>
            }
          </ol>
        }
      }
    </app-game-shell>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .ask { margin: 0; font: 400 20px/1.2 var(--display); }

    .rail { list-style: none; margin: 0; padding: 0; display: grid; gap: 10px; position: relative; }
    /* The dashed line that strings the stops together. */
    .rail::before { content: ""; position: absolute; left: 23px; top: 24px; bottom: 24px; border-left: 3px dashed var(--navy); opacity: .45; }
    .rail li { display: flex; align-items: center; gap: 12px; position: relative; }
    .logo {
      flex: none; width: 48px; height: 48px; border: 2px solid var(--navy); border-radius: 50%; background: #fff;
      display: grid; place-items: center; overflow: hidden;
    }
    .logo img { width: 38px; height: 38px; }
    .logo.badge { background: var(--mustard); font: 400 13px/1 var(--display); letter-spacing: .02em; }
    .rail div { flex: 1; min-width: 0; display: grid; gap: 1px; }
    .rail b { font-size: 19px; line-height: 1.15; }
    .rail small { color: var(--muted); font-size: 16px; }
    .bar { height: 6px; margin-top: 3px; border: 1.5px solid var(--navy); border-radius: 4px; background: var(--surface); }
    .bar i { display: block; height: 100%; min-width: 6px; background: var(--mustard); }

    /* Hints are ticket stubs: dashed until earned, filled once open. */
    .hints { list-style: none; margin: 4px 0 0; padding: 0; display: grid; grid-template-columns: repeat(2, 1fr); gap: 6px; }
    .hints li {
      display: grid; padding: 5px 9px 6px; border: 2px dashed var(--navy); border-radius: 4px;
      color: var(--muted); font-weight: 700; line-height: 1.15; min-width: 0; overflow-wrap: anywhere;
    }
    .hints small { font-size: 11px; letter-spacing: .08em; text-transform: uppercase; }
    .hints .open { border-style: solid; background: var(--mustard); color: var(--navy); }

    .misses { list-style: none; margin: 4px 0 0; padding: 0; display: grid; gap: 6px; }
    .misses li {
      display: flex; align-items: center; gap: 10px; padding: 6px 10px; border: 2px solid var(--miss-line); border-radius: 4px;
      background: var(--miss);
    }
    .misses app-avatar { --size: 36px; }
    .misses span { display: grid; min-width: 0; }
    .misses s { font-weight: 700; text-decoration-color: var(--bad); text-decoration-thickness: 2px; }
    .misses small { color: var(--miss-ink); font-size: 15px; }
    .also { color: var(--muted); font-size: 16px; }
  `,
})
export class CareerPath {
  /** From the address: a past or practice puzzle's id; absent for today's. */
  readonly puzzleId = input<string>();

  private readonly store = inject(GameStore);
  private readonly brokenLogos = signal<ReadonlySet<string>>(new Set());

  protected readonly years = stopYears;
  protected readonly state = computed(
    () => this.store.state() as GameState<{ stops: CareerStop[] }, { hints: CareerHint[] }, CareerFeedback, CareerAnswer> | null,
  );
  protected readonly longest = computed(() => Math.max(1, ...(this.state()?.payload.stops ?? []).map((s) => s.seasons)));
  protected readonly misses = computed(() => (this.state()?.guesses ?? []).filter((g) => !g.correct).reverse());
  /** One square per guess: red for a miss, green for the one that got it. */
  protected readonly rows = computed(() => [(this.state()?.guesses ?? []).map((g) => (g.correct ? '🟩' : '🟥')).join('')]);

  constructor() {
    loadFromRoute(this.store, 'career-path', this.puzzleId);
  }

  protected showLogo(stop: CareerStop): boolean {
    return !!stop.logoUrl && !this.brokenLogos().has(stop.logoUrl);
  }

  protected logoFailed(stop: CareerStop): void {
    if (stop.logoUrl) this.brokenLogos.update((set) => new Set(set).add(stop.logoUrl!));
  }

  /** "Also played for CHI and MIA" for a wrong guess. */
  protected sharedWith(stops: CareerStop[], feedback: CareerFeedback | null): string {
    const teams = [...new Set((feedback?.shared ?? []).map((i) => stops[i]?.abbreviation).filter(Boolean))];
    if (!teams.length) return 'None of these stops';
    const list = teams.length > 1 ? `${teams.slice(0, -1).join(', ')} and ${teams[teams.length - 1]}` : teams[0];
    return `Also played for ${list}`;
  }
}
