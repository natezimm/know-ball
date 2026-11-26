import { Component, computed, inject, input, ChangeDetectionStrategy } from '@angular/core';
import { GameState, GuessRecord, League, isMiss } from '../../core/game-api';
import { GameStore } from '../../core/game-store';
import { loadFromRoute } from '../../core/puzzle-route';
import { Avatar } from '../../shared/avatar/avatar';
import { GameShell } from '../../shared/game-shell/game-shell';

/** One blank in an NBA photo: where he stood and his line that season, per game. */
export interface BasketballSlot {
  position: string;
  row: 'back' | 'front';
  games: number;
  points: number;
  rebounds: number;
  assists: number;
}

/**
 * One blank in an NFL or MLB photo: the position and the season's totals it is known by, by card key (pyd, ptd, int
 * for a quarterback; car, ryd, rtd for a running back; rec, wyd, wtd for a receiver; avg, hr, rbi for a baseball
 * fielder; w, l, era, k for the starting pitcher). A baseball photo puts the infield in a middle row.
 */
export interface FootballSlot {
  position: string;
  row: 'back' | 'middle' | 'front';
  games: number;
  stats: Record<string, number>;
}

export type PhotoSlot = BasketballSlot | FootballSlot;

export interface PhotoPayload {
  team: { city: string; name: string; abbreviation: string; logoUrl: string | null };
  /** NBA: the year the season started, 2015 is 2015-16. NFL and MLB: the season's year, 2019 ended in Super Bowl LIV. */
  season: number;
  wins: number | null;
  losses: number | null;
  /** NFL only. */
  ties?: number | null;
  /** "won" or "lost" for a Finals (or Super Bowl, or World Series) team; null for a practice team that did not get there. */
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

/** 2019 → "LIV": Super Bowl I closed the 1966 season. */
export function superBowl(season: number): string {
  let n = season - 1965;
  let out = '';
  for (const [value, numeral] of [[50, 'L'], [40, 'XL'], [10, 'X'], [9, 'IX'], [5, 'V'], [4, 'IV'], [1, 'I']] as const) {
    while (n >= value) {
      out += numeral;
      n -= value;
    }
  }
  return out;
}

const count = (n: number | undefined) => Math.round(n ?? 0).toLocaleString('en-US');
/** 0.292 → ".292". */
const average = (n: number | undefined) => (n ?? 0).toFixed(3).replace(/^0/, '');

/**
 * NBA: "30.1 pts" over "5.4 reb · 6.7 ast". NFL: "4,031 pass yds" over "26 TD · 5 INT", "1,229 rec yds" over
 * "97 rec · 5 TD", or "498 rush yds" over "111 car · 5 TD". MLB: ".292 avg" over "39 HR · 102 RBI", or "2.53 ERA"
 * over "19–5 · 197 K".
 */
export function statLine(slot: PhotoSlot): [string, string] {
  if ('stats' in slot) {
    const s = slot.stats;
    if ('era' in s) return [`${(s['era'] ?? 0).toFixed(2)} ERA`, `${count(s['w'])}–${count(s['l'])} · ${count(s['k'])} K`];
    if ('avg' in s) return [`${average(s['avg'])} avg`, `${count(s['hr'])} HR · ${count(s['rbi'])} RBI`];
    if (slot.position === 'QB') return [`${count(s['pyd'])} pass yds`, `${count(s['ptd'])} TD · ${count(s['int'])} INT`];
    if (slot.position === 'RB') return [`${count(s['ryd'])} rush yds`, `${count(s['car'])} car · ${count(s['rtd'])} TD`];
    return [`${count(s['wyd'])} rec yds`, `${count(s['rec'])} rec · ${count(s['wtd'])} TD`];
  }
  return [`${slot.points.toFixed(1)} pts`, `${slot.rebounds.toFixed(1)} reb · ${slot.assists.toFixed(1)} ast`];
}

/**
 * Lays the slots out as the photo stands: the back row, a middle row in a baseball photo (empty otherwise), then the
 * front. A slot's index stays its place in the payload.
 */
export function photoRows(
  payload: PhotoPayload,
  guesses: GuessRecord[],
  answer: PhotoAnswer | null,
): { back: PhotoSpot[]; middle: PhotoSpot[]; front: PhotoSpot[] } {
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
  return {
    back: spots.filter((s) => s.slot.row === 'back'),
    middle: spots.filter((s) => s.slot.row === 'middle'),
    front: spots.filter((s) => s.slot.row !== 'back' && s.slot.row !== 'middle'),
  };
}

/**
 * Team Photo: a blank photo of a Finals team's starting five, or in the NFL edition a Super Bowl team's quarterback,
 * running back, two receivers and tight end. Name all five before your fourth miss; naming a bench player is free.
 */
@Component({
  selector: 'app-team-photo',
  imports: [Avatar, GameShell],
  providers: [GameStore],
  template: `
    <app-game-shell
      name="Team Photo" trim="var(--red)" limitsMisses includeInactive practice [leagues]="['nba', 'nfl', 'mlb']"
      [shareRows]="rows()" [resultName]="teamName()" [resultImage]="state()?.payload?.team?.logoUrl ?? null"
    >
      <div help>
        @if (football()) {
          <p>
            This is the team photo of one NFL team that played in the Super Bowl, with the faces left blank. Name its
            quarterback, running back, two wide receivers and tight end.
          </p>
          <p>
            Each is the player with the most of what his position does for that team that season: pass attempts,
            carries or receiving yards. Under each blank is his position and his season totals.
          </p>
          <p>
            Naming one of the five puts him in the photo. Naming anyone else on that team's roster is free, linemen and
            kickers too. Anyone else is a miss, and four misses ends the game.
          </p>
          <p>Practice rounds use other playoff teams since 1999.</p>
        } @else if (baseball()) {
          <p>
            This is the team photo of one MLB team that played in the World Series, with the faces left blank. Name its
            eight fielders and its top starting pitcher.
          </p>
          <p>
            Each fielder is the player with the most games at his position for that team that season, and the pitcher is
            the one with the most starts. The outfield stands at the back, the infield in the middle, and the pitcher and
            catcher in front. Under each blank is his position and his season line.
          </p>
          <p>
            Naming one of the nine puts him in the photo. Naming anyone else who played for that team that season is free.
            Anyone else is a miss, and four misses ends the game.
          </p>
          <p>Practice rounds use other playoff teams since 1960.</p>
        } @else {
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
        }
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

          @for (row of rowsOf(layout()); track row.label) {
            <ol class="row" [class.middle]="row.label === 'Middle row'" [class.front]="row.label === 'Front row'" [attr.aria-label]="row.label">
              @for (spot of row.spots; track spot.index) {
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
                      <small>{{ line(spot.slot)[0] }}</small>
                      <span class="visually-hidden">{{ p.name }}{{ spot.missed ? ', not named' : '' }}</span>
                    } @else {
                      <small>{{ line(spot.slot)[0] }}</small>
                      <small>@for (part of line(spot.slot)[1].split(' · '); track $index) {@if ($index) {{{ ' ' }}}<span class="part">{{ $index ? '· ' + part : part }}</span>}</small>
                    }
                  </span>
                </li>
              }
            </ol>
          }
        </article>

        <p class="count">{{ found() }} of {{ s.payload.slots.length }} in the photo</p>

        @if (latest(); as g) {
          @if (!g.correct && s.status === 'playing') {
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
    /* A baseball infield: four across, a little smaller. */
    .row.middle { grid-template-columns: repeat(4, minmax(0, 1fr)); }
    .row.middle .face { --face: 64px; }
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
      font: 400 13px/1.3 var(--display); letter-spacing: .03em; color: var(--cream); background: rgb(29 43 83 / .55);
    }
    .plate {
      display: grid; max-width: 104px; padding: 2px 6px; border-radius: 4px; text-align: center; line-height: 1.15;
      background: rgb(0 0 0 / .35); color: var(--cream);
    }
    .plate small { font-size: 13px; white-space: nowrap; }
    .plate small:last-of-type { opacity: .85; }
    /* "6.8 reb · 5.4 ast" breaks between its parts, never inside one. */
    .plate .part { display: inline-block; white-space: nowrap; }
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
      /* Three lines side by side outgrow a third of a 320px photo; let each wrap inside its plate. */
      .plate { max-width: 100%; padding: 2px 4px; }
      .plate small { white-space: normal; }
    }
  `,
})
export class TeamPhoto {
  /** From the address: a past or practice puzzle's id; absent for today's. */
  readonly puzzleId = input<string>();
  /** From the route's data: "nfl" or "mlb" on those editions' addresses. */
  readonly league = input<League>();

  private readonly store = inject(GameStore);

  protected readonly football = computed(() => this.store.league() === 'nfl');
  protected readonly baseball = computed(() => this.store.league() === 'mlb');

  protected readonly isMiss = isMiss;
  protected readonly line = statLine;
  protected readonly state = computed(() => this.store.state() as GameState<PhotoPayload, null, PhotoFeedback, PhotoAnswer> | null);
  protected readonly layout = computed(() => {
    const s = this.state();
    return s ? photoRows(s.payload, s.guesses, s.answer) : { back: [], middle: [], front: [] };
  });
  protected readonly found = computed(() => (this.state()?.guesses ?? []).filter((g) => g.correct).length);
  protected readonly latest = computed(() => this.state()?.guesses.at(-1) ?? null);
  protected readonly bench = computed(() => (this.state()?.guesses ?? []).filter((g) => !g.correct && !isMiss(g)));
  protected readonly misses = computed(() => (this.state()?.guesses ?? []).filter(isMiss).reverse());
  /** "2015–16 Golden State Warriors"; an NFL or MLB season is one year, "2019 Kansas City Chiefs". */
  protected readonly teamName = computed(() => {
    const p = this.state()?.payload;
    return p ? `${this.store.league() === 'nba' ? seasonName(p.season) : p.season} ${p.team.city} ${p.team.name}` : '';
  });
  /** "73–9 · Lost in the Finals", "11–4–1 · Lost Super Bowl XLVII", or "103–58 · Won the World Series". */
  protected readonly record = computed(() => {
    const p = this.state()?.payload;
    if (!p) return '';
    const football = this.football();
    const finals = this.baseball()
      ? p.finals === 'won' ? 'Won the World Series' : p.finals === 'lost' ? 'Lost the World Series' : 'Made the playoffs'
      : football
      ? p.finals === 'won' ? `Won Super Bowl ${superBowl(p.season)}` : p.finals === 'lost' ? `Lost Super Bowl ${superBowl(p.season)}` : 'Made the playoffs'
      : p.finals === 'won' ? 'Won the title' : p.finals === 'lost' ? 'Lost in the Finals' : 'Made the playoffs';
    const ties = p.ties ? `–${p.ties}` : '';
    return [p.wins != null && p.losses != null ? `${p.wins}–${p.losses}${ties}` : null, finals].filter(Boolean).join(' · ');
  });
  /** One square per guess: green for a starter, yellow for a bench player, red for a miss. */
  protected readonly rows = computed(() => [
    (this.state()?.guesses ?? []).map((g) => (g.correct ? '🟩' : isMiss(g) ? '🟥' : '🟨')).join(''),
  ]);

  constructor() {
    loadFromRoute(this.store, 'team-photo', this.puzzleId, this.league);
  }

  /** The photo's rows from back to front, leaving out the middle one a basketball or football photo does not have. */
  protected rowsOf(layout: { back: PhotoSpot[]; middle: PhotoSpot[]; front: PhotoSpot[] }): { label: string; spots: PhotoSpot[] }[] {
    return [
      { label: 'Back row', spots: layout.back },
      { label: 'Middle row', spots: layout.middle },
      { label: 'Front row', spots: layout.front },
    ].filter((row) => row.label !== 'Middle row' || row.spots.length > 0);
  }

  /** "Curry"; "Jr." and the like stay with the name before them. */
  protected lastName(name: string): string {
    const parts = name.trim().split(/\s+/);
    const last = parts.at(-1) ?? name;
    return parts.length > 2 && /^(jr\.?|sr\.?|ii|iii|iv)$/i.test(last) ? parts.slice(-2).join(' ') : last;
  }
}
