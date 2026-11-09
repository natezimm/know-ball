import { Component, computed, inject, input, ChangeDetectionStrategy } from '@angular/core';
import { GameState, GuessRecord, isMiss } from '../../core/game-api';
import { GameStore } from '../../core/game-store';
import { loadFromRoute } from '../../core/puzzle-route';
import { Avatar } from '../../shared/avatar/avatar';
import { GameShell } from '../../shared/game-shell/game-shell';

/** One blank in the photo: where he stood and his line that season, per game. */
export interface PhotoSlot {
  position: string;
  row: 'back' | 'front';
  games: number;
  points: number;
  rebounds: number;
  assists: number;
}

export interface PhotoPayload {
  team: { city: string; name: string; abbreviation: string; logoUrl: string | null };
  /** The year the season started: 2015 is 2015-16. */
  season: number;
  wins: number | null;
  losses: number | null;
  /** "won" or "lost" for a Finals team; null for a practice team that did not get there. */
  finals: 'won' | 'lost' | null;
  slots: PhotoSlot[];
}

interface PhotoPerson {
  playerId: number;
  name: string;
  headshotUrl: string | null;
}

/** Sent once the game is over: who stood in each slot. */
export interface PhotoAnswer {
  slots: PhotoPerson[];
}

/** A starter's feedback names his slot; a bench player's says so and costs nothing. */
type PhotoFeedback = { slot: number } | { bench: true };

/** What one spot in the photo shows: a blank, a starter you named, or one the answer filled in after a loss. */
export interface PhotoSpot {
  index: number;
  slot: PhotoSlot;
  person: PhotoPerson | null;
  missed: boolean;
}

/** 2015 → "2015–16", 1999 → "1999–00". */
export function seasonName(season: number): string {
  return `${season}–${String((season + 1) % 100).padStart(2, '0')}`;
}

/** "30.1 pts" over "5.4 reb · 6.7 ast". */
export function statLine(slot: PhotoSlot): [string, string] {
  return [`${slot.points.toFixed(1)} pts`, `${slot.rebounds.toFixed(1)} reb · ${slot.assists.toFixed(1)} ast`];
}

/** Lays the slots out as the photo stands: the back row, then the front. A slot's index stays its place in the payload. */
export function photoRows(payload: PhotoPayload, guesses: GuessRecord[], answer: PhotoAnswer | null): { back: PhotoSpot[]; front: PhotoSpot[] } {
  const named = new Map<number, PhotoPerson>();
  for (const g of guesses) {
    const slot = (g.feedback as { slot?: number } | null)?.slot;
    if (g.correct && typeof slot === 'number') named.set(slot, { playerId: g.playerId, name: g.name, headshotUrl: g.headshotUrl });
  }
  const spots = payload.slots.map((slot, index): PhotoSpot => {
    const found = named.get(index);
    const revealed = answer?.slots[index] ?? null;
    return { index, slot, person: found ?? revealed, missed: !found && !!revealed };
  });
  return { back: spots.filter((s) => s.slot.row === 'back'), front: spots.filter((s) => s.slot.row !== 'back') };
}

/** Team Photo: a blank photo of a Finals team's starting five. Name all five before your fourth miss; naming a bench player is free. */
@Component({
  selector: 'app-team-photo',
  imports: [Avatar, GameShell],
  providers: [GameStore],
  template: `
    <app-game-shell
      name="Team Photo" trim="var(--red)" limitsMisses includeInactive practice
      [shareRows]="rows()" [resultName]="teamName()" [resultImage]="state()?.payload?.team?.logoUrl ?? null"
    >
      <div help>
        <p>This is the team photo of one NBA team that reached the Finals, with the faces left blank. Name its starting five.</p>
        <p>
          The five are the players who started the most games that season. Under each blank is his position and his
          points, rebounds and assists per game.
        </p>
        <p>
          Naming a starter puts him in the photo. Naming someone who came off the bench for that team is free. Anyone
          else is a miss, and four misses ends the game.
        </p>
        <p>Practice rounds use other playoff teams since 1981–82.</p>
      </div>

      @if (state()?.answer) {
        <span answer class="kb-result-detail">{{ record() }}</span>
      }

      @if (state(); as s) {

        <article class="photo" [attr.aria-label]="'Team photo, ' + found() + ' of ' + s.payload.slots.length + ' named'">
          <header class="plaque">
            <app-avatar class="logo" [src]="s.payload.team.logoUrl" [name]="s.payload.team.city + ' ' + s.payload.team.name" />
            <div>
              <b>{{ teamName() }}</b>
              <small>{{ record() }}</small>
            </div>
          </header>

          @for (row of [layout().back, layout().front]; track $index) {
            <ol class="row" [class.front]="$index === 1" [attr.aria-label]="$index === 0 ? 'Back row' : 'Front row'">
              @for (spot of row; track spot.index) {
                <li class="spot" [class.found]="spot.person && !spot.missed" [class.missed]="spot.missed">
                  <span class="face">
                    @if (spot.person; as p) {
                      <app-avatar [src]="p.headshotUrl" [name]="p.name" />
                    } @else {
                      <span class="figure" aria-hidden="true"><i></i><u></u></span>
                    }
                    <span class="pos">{{ spot.slot.position }}</span>
                  </span>
                  <span class="plate">
                    @if (spot.person; as p) {
                      <b>{{ lastName(p.name) }}</b>
                      <span class="visually-hidden">{{ p.name }}{{ spot.missed ? ', not named' : '' }}</span>
                    } @else {
                      <small>{{ line(spot.slot)[0] }}</small>
                      <small>{{ line(spot.slot)[1] }}</small>
                    }
                  </span>
                </li>
              }
            </ol>
          }
        </article>

        <p class="count">{{ found() }} of {{ s.payload.slots.length }} in the photo</p>

        @if (latest(); as g) {
          @if (!g.correct) {
            <p class="note" [class.free]="!isMiss(g)" role="status">
              @if (isMiss(g)) {
                <b>{{ g.name }}</b> isn't in this photo · miss
              } @else {
                <b>{{ g.name }}</b> came off the bench · no miss
              }
            </p>
          }
        }

        @if (bench().length) {
          <div class="bench">
            <span class="kb-label">Off the bench</span>
            <ul>
              @for (g of bench(); track g.playerId) {
                <li><app-avatar [src]="g.headshotUrl" [name]="g.name" /><span>{{ g.name }}</span></li>
              }
            </ul>
          </div>
        }

        @if (misses().length) {
          <ol class="misses" aria-label="Misses, newest first">
            @for (g of misses(); track g.playerId) {
              <li><app-avatar [src]="g.headshotUrl" [name]="g.name" /><s>{{ g.name }}</s></li>
            }
          </ol>
        }
      }
    </app-game-shell>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    /* A photo in a cream mat: a navy backdrop over a wooden floor, with a plaque at the top. */
    .photo {
      display: grid; gap: 10px; padding: 12px 8px 14px; border-radius: 6px;
      border: 6px solid var(--cream); outline: 3px solid var(--navy);
      background:
        repeating-linear-gradient(180deg, transparent 0 22px, rgb(255 255 255 / .03) 22px 24px),
        linear-gradient(180deg, #2b4a7d 0%, var(--navy) 60%, #7a5530 60%, #5e4024 100%);
      box-shadow: var(--card-lift);
    }
    .plaque {
      display: flex; align-items: center; gap: 8px; justify-self: center; max-width: 100%;
      padding: 4px 12px 4px 4px; border-radius: 999px; background: var(--stock); color: var(--navy);
      border: 2px solid var(--gold);
    }
    .plaque div { display: grid; line-height: 1.15; min-width: 0; }
    .plaque b { font: 400 15px/1.15 var(--display); }
    .plaque small { font-size: 13px; color: var(--muted); font-weight: 700; }
    .logo { --size: 34px; border-color: var(--gold); }

    .row { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 4px; }
    .row.front { grid-template-columns: repeat(2, minmax(0, 104px)); justify-content: center; gap: 24px; }
    .spot { display: grid; justify-items: center; gap: 4px; min-width: 0; }
    .face {
      --face: 76px; position: relative; width: var(--face); height: var(--face); border-radius: 50%; overflow: hidden; display: grid; place-items: end center;
      background: rgb(255 255 255 / .07); border: 2px dashed rgb(246 236 210 / .45);
    }
    .face app-avatar { --size: calc(var(--face) - 6px); border: none; }
    .found .face { border: 3px solid var(--mustard); place-items: center; animation: pose .45s ease-out both; }
    .missed .face { border: 3px solid var(--red); place-items: center; }
    .missed app-avatar { filter: grayscale(1); opacity: .75; }
    @keyframes pose { from { transform: scale(.6); opacity: 0; } }
    @media (prefers-reduced-motion: reduce) { .found .face { animation: none; } }
    .figure { position: relative; width: 70%; height: 78%; opacity: .35; }
    .figure i { position: absolute; left: 32%; top: 6%; width: 36%; aspect-ratio: 1; border-radius: 50%; background: var(--cream); }
    .figure u { position: absolute; left: 6%; right: 6%; bottom: -4%; height: 46%; border-radius: 50% 50% 0 0; background: var(--cream); }
    .pos {
      position: absolute; top: 5px; left: 50%; transform: translateX(-50%); padding: 0 4px; border-radius: 3px;
      font: 400 12px/1.3 var(--display); letter-spacing: .05em; color: var(--cream); background: rgb(29 43 83 / .55);
    }
    .found .pos, .missed .pos { top: auto; bottom: 3px; }
    .plate {
      display: grid; max-width: 104px; padding: 2px 6px; border-radius: 4px; text-align: center; line-height: 1.15;
      background: rgb(0 0 0 / .35); color: var(--cream);
    }
    .plate small { font-size: 12px; white-space: nowrap; }
    .plate small:last-of-type { font-size: 11px; opacity: .85; }
    .plate b { font-size: 14px; overflow-wrap: anywhere; }
    .found .plate { background: var(--mustard); color: var(--navy); }
    .missed .plate { background: var(--red); color: var(--cream); }

    .count { margin: 0; text-align: center; font-weight: 700; color: var(--muted); }
    .note { margin: 0; padding: 7px 10px; border-radius: 6px; background: var(--surface); border-left: 4px solid var(--red); }
    .note.free { border-left-color: var(--teal); }

    .bench { display: grid; gap: 4px; }
    .bench ul { list-style: none; margin: 0; padding: 0; display: flex; flex-wrap: wrap; gap: 6px; }
    .bench li {
      display: flex; align-items: center; gap: 6px; padding: 2px 10px 2px 2px; border-radius: 999px;
      background: var(--surface); border: 2px solid var(--teal); font-weight: 700; font-size: 15px;
    }
    .bench app-avatar, .misses app-avatar { --size: 26px; }

    .misses { list-style: none; margin: 0; padding: 0; display: grid; }
    .misses li { display: flex; align-items: center; gap: 8px; padding: 5px 2px; border-bottom: 1px dashed var(--miss-line); }
    .misses s { flex: 1; min-width: 0; font-weight: 700; text-decoration-color: var(--bad); text-decoration-thickness: 2px; overflow-wrap: anywhere; }

    @media (max-width: 360px) {
      .face { --face: 64px; }
      .row.front { gap: 12px; }
    }
  `,
})
export class TeamPhoto {
  /** From the address: a past or practice puzzle's id; absent for today's. */
  readonly puzzleId = input<string>();

  private readonly store = inject(GameStore);

  protected readonly isMiss = isMiss;
  protected readonly line = statLine;
  protected readonly state = computed(() => this.store.state() as GameState<PhotoPayload, null, PhotoFeedback, PhotoAnswer> | null);
  protected readonly layout = computed(() => {
    const s = this.state();
    return s ? photoRows(s.payload, s.guesses, s.answer) : { back: [], front: [] };
  });
  protected readonly found = computed(() => (this.state()?.guesses ?? []).filter((g) => g.correct).length);
  protected readonly latest = computed(() => this.state()?.guesses.at(-1) ?? null);
  protected readonly bench = computed(() => (this.state()?.guesses ?? []).filter((g) => !g.correct && !isMiss(g)));
  protected readonly misses = computed(() => (this.state()?.guesses ?? []).filter(isMiss).reverse());
  /** "2015–16 Golden State Warriors". */
  protected readonly teamName = computed(() => {
    const p = this.state()?.payload;
    return p ? `${seasonName(p.season)} ${p.team.city} ${p.team.name}` : '';
  });
  /** "73–9 · Lost in the Finals". */
  protected readonly record = computed(() => {
    const p = this.state()?.payload;
    if (!p) return '';
    const finals = p.finals === 'won' ? 'Won the title' : p.finals === 'lost' ? 'Lost in the Finals' : 'Made the playoffs';
    return [p.wins !== null && p.losses !== null ? `${p.wins}–${p.losses}` : null, finals].filter(Boolean).join(' · ');
  });
  /** One square per guess: green for a starter, yellow for a bench player, red for a miss. */
  protected readonly rows = computed(() => [
    (this.state()?.guesses ?? []).map((g) => (g.correct ? '🟩' : isMiss(g) ? '🟥' : '🟨')).join(''),
  ]);

  constructor() {
    loadFromRoute(this.store, 'team-photo', this.puzzleId);
  }

  /** "Curry"; "Jr." and the like stay with the name before them. */
  protected lastName(name: string): string {
    const parts = name.trim().split(/\s+/);
    const last = parts.at(-1) ?? name;
    return parts.length > 2 && /^(jr\.?|sr\.?|ii|iii|iv)$/i.test(last) ? parts.slice(-2).join(' ') : last;
  }
}
