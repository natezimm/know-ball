import { Component, computed, effect, inject, input, signal, untracked, ChangeDetectionStrategy } from '@angular/core';
import { GameState, League } from '../../core/game-api';
import { GameStore } from '../../core/game-store';
import { loadFromRoute } from '../../core/puzzle-route';
import { Avatar } from '../../shared/avatar/avatar';
import { GameShell } from '../../shared/game-shell/game-shell';

/** One printed line. `season` is the year it started: 2013 is 2013-14. */
export interface CardSeason {
  season: number;
  games: number;
  points: number;
  rebounds: number;
  assists: number;
}

interface CardCareer {
  seasons: number;
  games: number;
  points: number;
  rebounds: number;
  assists: number;
}

interface CardPayload {
  seasons: CardSeason[];
  career: CardCareer;
}

/** A football card's stat column after games, and the decimals it prints with. */
export interface CardColumn {
  label: string;
  decimals: number;
}

/** A football card: columns that suit the player's position, and season totals. */
export interface FootballCardPayload {
  columns: CardColumn[];
  seasons: { season: number; games: number; values: number[] }[];
  career: { seasons: number; games: number; values: number[] };
}

/** The card as printed: column headings after Year, Club and G, then each season's cells and the career line's. */
export interface CardTable {
  headings: string[];
  seasons: { season: number; label: string; games: number; cells: string[] }[];
  career: { seasons: number; games: number; cells: string[] };
}

/** Stamps for making a team, printed in teal; the rest are individual awards, in red. */
const TEAM_STAMPS = new Set(['All-Star', 'All-NBA', 'All-Pro', 'All-Pro 2nd', 'Pro Bowl']);

/** What the misses have uncovered so far. A part that is still covered is left out. */
export interface CardClues {
  steps: { label: string; open: boolean }[];
  /** The club for each printed season, in order: "OKC", or "BKN/PHX" for a season split by a trade. */
  teams?: string[];
  position?: string;
  height?: string;
  /** The award stamped on each season, or null. */
  stamps?: (string | null)[];
  drafted?: string;
  origin?: { label: string; value: string };
}

/** 2013 → "13–14", 1999 → "99–00". */
export function seasonLabel(season: number): string {
  const two = (n: number) => String(n % 100).padStart(2, '0');
  return `${two(season)}–${two(season + 1)}`;
}

/** 5097 with no decimals → "5,097"; 66 with one → "66.0". */
function figure(value: number, decimals: number): string {
  return value.toLocaleString('en-US', { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
}

/** Either kind of card as rows of printed cells. A football season goes by its own year: 2023, not 23–24. */
export function cardTable(payload: CardPayload | FootballCardPayload): CardTable {
  if ('columns' in payload) {
    const cells = (values: number[]) => values.map((v, i) => figure(v, payload.columns[i]?.decimals ?? 0));
    return {
      headings: payload.columns.map((c) => c.label),
      seasons: payload.seasons.map((s) => ({ season: s.season, label: String(s.season), games: s.games, cells: cells(s.values) })),
      career: { seasons: payload.career.seasons, games: payload.career.games, cells: cells(payload.career.values) },
    };
  }
  const cells = (s: { points: number; rebounds: number; assists: number }) => [s.points, s.rebounds, s.assists].map((v) => v.toFixed(1));
  return {
    headings: ['Pts', 'Reb', 'Ast'],
    seasons: payload.seasons.map((s) => ({ season: s.season, label: seasonLabel(s.season), games: s.games, cells: cells(s) })),
    career: { seasons: payload.career.seasons, games: payload.career.games, cells: cells(payload.career) },
  };
}

/** Back of the Card: a player's season lines with the name blacked out. Five guesses; each of the first four misses uncovers more of the card. */
@Component({
  selector: 'app-back-of-the-card',
  imports: [Avatar, GameShell],
  providers: [GameStore],
  template: `
    <app-game-shell name="Back of the Card" trim="var(--gold)" includeInactive practice [leagues]="['nba', 'nfl']" [shareRows]="rows()">
      <div help>
        <p>This is the back of one {{ leagueName() }} player's card: every season he played, with his name blacked out. Name him in five guesses.</p>
        <p>The answer can be a current player or a retired one.</p>
        <p>
          Each wrong guess uncovers the next part of the card: first his team each season, then his position and height,
          then the awards stamped on his best seasons, then his draft and college. Your last guess sees the whole card.
        </p>
        @if (football()) {
          <p>
            Stats are season totals: games played, then the numbers his position is known by. Passing for quarterbacks, rushing
            for running backs, receiving for receivers and tight ends, and tackles, sacks, interceptions and forced fumbles for
            defenders. Every answer's career began in 1999 or later.
          </p>
        } @else {
          <p>Stats are per game: games played, points, rebounds and assists.</p>
        }
      </div>

      @if (state()?.answer && answerLine()) {
        <span answer class="kb-result-detail">{{ answerLine() }}</span>
      }

      @if (state(); as s) {
        <h2 class="ask">{{ s.answer ? possessive(s.answer.name) + ' card' : 'Whose card is this?' }}</h2>

        <article class="back" aria-label="The back of the card">
          <header class="strip">
            @if (s.answer) {
              <span class="name">{{ s.answer.name }}</span>
            } @else {
              <span class="redact name" aria-label="Name hidden">████ ███████</span>
            }
            @if (s.number) {
              <span class="no">No. {{ s.number }}</span>
            }
          </header>

          <dl class="bio">
            <div><dt>Pos</dt><dd [class.fresh]="fresh(1)">@if (clues()?.position; as v) { {{ v }} } @else { <i class="redact" aria-label="Hidden">▒▒</i> }</dd></div>
            <div><dt>Ht</dt><dd [class.fresh]="fresh(1)">@if (clues()?.height; as v) { {{ v }} } @else if (clues()?.position) { – } @else { <i class="redact" aria-label="Hidden">▒▒▒</i> }</dd></div>
            <div><dt>Drafted</dt><dd [class.fresh]="fresh(3)">@if (clues()?.drafted; as v) { {{ v }} } @else { <i class="redact" aria-label="Hidden">▒▒▒▒</i> }</dd></div>
          </dl>

          <table>
            <thead>
              <tr>
                <th scope="col">Year</th><th scope="col">Club</th><th scope="col">G</th>
                @for (heading of table()?.headings ?? []; track $index) { <th scope="col">{{ heading }}</th> }
              </tr>
            </thead>
            <tbody>
              @for (line of table()?.seasons ?? []; track line.season; let i = $index) {
                <tr>
                  <td>{{ line.label }}</td>
                  <td class="club">
                    @if (clues()?.teams?.[i]; as team) {
                      <b [class.fresh]="fresh(0)">{{ team }}</b>
                    } @else {
                      <i class="redact" aria-label="Hidden">▒▒▒</i>
                    }
                    @if (clues()?.stamps?.[i]; as stamp) {
                      <em class="stamp" [class.top]="!teamStamps.has(stamp)" [class.fresh]="fresh(2)">{{ stamp }}</em>
                    }
                  </td>
                  <td>{{ line.games }}</td>
                  @for (cell of line.cells; track $index) { <td>{{ cell }}</td> }
                </tr>
              }
            </tbody>
            @if (table(); as t) {
              <tfoot>
                <tr>
                  <th scope="row">Career</th>
                  <td>{{ t.career.seasons }} {{ t.career.seasons === 1 ? 'yr' : 'yrs' }}</td>
                  <td>{{ t.career.games.toLocaleString('en-US') }}</td>
                  @for (cell of t.career.cells; track $index) { <td>{{ cell }}</td> }
                </tr>
              </tfoot>
            }
          </table>

          @if (clues()?.origin; as origin) {
            <p class="fact" [class.fresh]="fresh(3)"><b>{{ origin.label }}:</b> {{ origin.value }}</p>
          } @else {
            <p class="fact locked">Draft and college open after 4 misses</p>
          }
        </article>

        <ol class="ladder" aria-label="What each miss uncovers">
          @for (step of clues()?.steps ?? []; track step.label; let i = $index) {
            <li [class.open]="step.open" [class.next]="!step.open && i === openCount()">
              <span class="visually-hidden">{{ step.open ? 'Uncovered: ' : 'Covered: ' }}</span>{{ step.label }}
            </li>
          }
        </ol>

        @if (misses().length) {
          <ol class="misses" aria-label="Wrong guesses, newest first">
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
    .ask { margin: 0; font: 400 20px/1.2 var(--display); }

    /* The card back: navy border, a red name strip, cream stock and a stat table. */
    .back {
      display: grid; gap: 6px; padding: 8px 10px 10px; background: var(--surface);
      border: 5px solid var(--navy); border-radius: 6px;
      box-shadow: inset 0 0 0 2px var(--cream), inset 0 0 0 3px var(--red), var(--card-lift);
    }
    .strip {
      display: flex; justify-content: space-between; align-items: center; gap: 10px;
      padding: 5px 10px; background: var(--red); color: var(--cream); border-radius: 3px;
    }
    .strip .name { font: 400 20px/1.1 var(--script); overflow-wrap: anywhere; }
    .strip .redact { color: var(--navy); font: 400 16px/1 var(--font); letter-spacing: -1px; }
    .strip .no { flex: none; font: 400 12px/1 var(--display); letter-spacing: .06em; }
    .redact { font-style: normal; color: var(--navy); opacity: .8; user-select: none; }

    .bio { display: flex; flex-wrap: wrap; gap: 4px 16px; margin: 0; font-size: 14px; }
    .bio div { display: flex; gap: 5px; align-items: baseline; }
    .bio dt { font-size: 11px; letter-spacing: .08em; text-transform: uppercase; color: var(--muted); font-weight: 700; }
    .bio dd { margin: 0; font-weight: 700; }

    table { width: 100%; border-collapse: collapse; font-size: 15px; line-height: 1.2; font-variant-numeric: tabular-nums; }
    thead th {
      font: 400 11px/1 var(--display); color: var(--red); text-transform: uppercase; letter-spacing: .05em;
      padding: 4px 3px 5px; border-bottom: 2px solid var(--navy); text-align: right;
    }
    td, tfoot th { padding: 3px; text-align: right; white-space: nowrap; }
    thead th:nth-child(-n+2), td:nth-child(-n+2), tfoot th { text-align: left; }
    tbody tr:nth-child(even) td { background: rgb(217 203 166 / .35); }
    tfoot th, tfoot td { border-top: 2px solid var(--navy); font-weight: 700; padding-top: 5px; }
    /* A traded season with an award is the widest cell: on a narrow phone its stamp drops under the clubs. */
    td.club { white-space: normal; }
    .club b { font-weight: 700; white-space: nowrap; }
    .stamp {
      display: inline-block; margin-left: 6px; transform: rotate(-6deg); padding: 1px 4px 0;
      font: 400 10px/1.3 var(--display); font-style: normal; text-transform: uppercase; letter-spacing: .03em;
      color: var(--teal); border: 1.5px solid var(--teal); border-radius: 3px; background: var(--surface);
    }
    .stamp.top { color: var(--red); border-color: var(--red); }

    .fact { margin: 2px 0 0; padding: 6px 9px; font-size: 15px; border: 2px dashed var(--navy); border-radius: 4px; }
    .fact b { color: var(--red); }
    .fact.locked { text-align: center; color: var(--muted); font-size: 12px; font-weight: 700; letter-spacing: .08em; text-transform: uppercase; }

    /* What each miss uncovers, as ticket stubs: dashed until earned. */
    .ladder { list-style: none; margin: 2px 0 0; padding: 0; display: grid; grid-template-columns: repeat(4, 1fr); gap: 6px; }
    .ladder li {
      text-align: center; padding: 4px 2px; border: 2px solid var(--navy); border-radius: 999px;
      font-size: 12px; font-weight: 700; letter-spacing: .06em; text-transform: uppercase; color: var(--muted);
    }
    .ladder .open { background: var(--navy); color: var(--cream); }
    .ladder .next { border-style: dashed; color: var(--navy); }

    /* Parts uncovered by the guess just made turn over like a card. */
    .fresh { animation: uncover .5s ease-out both; }
    @keyframes uncover { from { transform: rotateX(90deg); opacity: 0; } }
    .stamp.fresh { animation-name: stamp; }
    @keyframes stamp { from { transform: scale(1.8) rotate(-6deg); opacity: 0; } }
    @media (prefers-reduced-motion: reduce) { .fresh, .stamp.fresh { animation: none; } }

    .misses { list-style: none; margin: 4px 0 0; padding: 0; display: grid; }
    .misses li { display: flex; align-items: center; gap: 8px; padding: 5px 2px; border-bottom: 1px dashed var(--miss-line); }
    .misses app-avatar { --size: 26px; }
    .misses s { flex: 1; min-width: 0; font-weight: 700; text-decoration-color: var(--bad); text-decoration-thickness: 2px; overflow-wrap: anywhere; }

    @media (max-width: 380px) {
      table { font-size: 13.5px; }
      td, tfoot th { padding: 3px 2px; }
    }
  `,
})
export class BackOfTheCard {
  /** From the address: a past or practice puzzle's id; absent for today's. */
  readonly puzzleId = input<string>();
  /** From the route's data: "nfl" on the NFL edition's address. */
  readonly league = input<League>();

  private readonly store = inject(GameStore);

  protected readonly teamStamps = TEAM_STAMPS;
  protected readonly football = computed(() => this.store.league() === 'nfl');
  protected readonly leagueName = computed(() => (this.football() ? 'NFL' : 'NBA'));
  protected readonly state = computed(() => this.store.state() as GameState<CardPayload | FootballCardPayload, CardClues> | null);
  protected readonly table = computed(() => {
    const payload = this.state()?.payload;
    return payload ? cardTable(payload) : null;
  });
  protected readonly clues = computed(() => this.state()?.clues ?? null);
  protected readonly openCount = computed(() => (this.clues()?.steps ?? []).filter((step) => step.open).length);
  protected readonly misses = computed(() => (this.state()?.guesses ?? []).filter((g) => !g.correct).reverse());
  /** Position, height, draft and college, which every finished game has uncovered. */
  protected readonly answerLine = computed(() => {
    const c = this.clues();
    if (!c) return '';
    return [c.position, c.height, c.drafted && `Drafted ${c.drafted}`, c.origin?.value].filter(Boolean).join(' · ');
  });
  /** Steps already open when the puzzle loaded stay still; only those opened since turn over. */
  private readonly loaded = signal<{ puzzleId: number; open: number } | null>(null);
  /** One square per guess: red for a miss, green for the one that got it. */
  protected readonly rows = computed(() => [(this.state()?.guesses ?? []).map((g) => (g.correct ? '🟩' : '🟥')).join('')]);

  constructor() {
    loadFromRoute(this.store, 'back-of-the-card', this.puzzleId, this.league);
    effect(() => {
      const s = this.state();
      if (s && untracked(this.loaded)?.puzzleId !== s.puzzleId) {
        this.loaded.set({ puzzleId: s.puzzleId, open: (s.clues?.steps ?? []).filter((step) => step.open).length });
      }
    });
  }

  /** Whether step `index` (0 = teams) opened after the page loaded, so it should animate in. */
  protected fresh(index: number): boolean {
    const loaded = this.loaded();
    return !!loaded && index >= loaded.open && index < this.openCount();
  }

  /** "Kevin Durant's"; a name ending in s takes a bare apostrophe. */
  protected possessive(name: string): string {
    return /s$/i.test(name) ? `${name}'` : `${name}'s`;
  }
}
