import { Component, ChangeDetectionStrategy, signal } from '@angular/core';
import { MockOption } from '../option';
import { headshot } from '../mock-data';

interface Season {
  year: string;
  team: string;
  g: number;
  pts: string;
  reb: string;
  ast: string;
  /** Award stamp shown once awards are unlocked. */
  stamp?: string;
}

// Kevin Durant's per-game lines, rounded; sample data for the sketch only.
const SEASONS: Season[] = [
  { year: '07-08', team: 'SEA', g: 80, pts: '20.3', reb: '4.4', ast: '2.4', stamp: 'ROY' },
  { year: '08-09', team: 'OKC', g: 74, pts: '25.3', reb: '6.5', ast: '2.8' },
  { year: '09-10', team: 'OKC', g: 82, pts: '30.1', reb: '7.6', ast: '2.8', stamp: 'All-Star' },
  { year: '10-11', team: 'OKC', g: 78, pts: '27.7', reb: '6.8', ast: '2.7', stamp: 'All-Star' },
  { year: '11-12', team: 'OKC', g: 66, pts: '28.0', reb: '8.0', ast: '3.5', stamp: 'All-Star' },
  { year: '12-13', team: 'OKC', g: 81, pts: '28.1', reb: '7.9', ast: '4.6', stamp: 'All-Star' },
  { year: '13-14', team: 'OKC', g: 81, pts: '32.0', reb: '7.4', ast: '5.5', stamp: 'MVP' },
  { year: '14-15', team: 'OKC', g: 27, pts: '25.4', reb: '6.6', ast: '4.1' },
  { year: '15-16', team: 'OKC', g: 72, pts: '28.2', reb: '8.2', ast: '5.0', stamp: 'All-Star' },
  { year: '16-17', team: 'GSW', g: 62, pts: '25.1', reb: '8.3', ast: '4.8', stamp: 'Champ' },
  { year: '17-18', team: 'GSW', g: 68, pts: '26.4', reb: '6.8', ast: '5.4', stamp: 'Champ' },
  { year: '18-19', team: 'GSW', g: 78, pts: '26.0', reb: '6.4', ast: '5.9', stamp: 'All-Star' },
  { year: '20-21', team: 'BKN', g: 35, pts: '26.9', reb: '7.1', ast: '5.6', stamp: 'All-Star' },
  { year: '21-22', team: 'BKN', g: 55, pts: '29.9', reb: '7.4', ast: '6.4', stamp: 'All-Star' },
  { year: '22-23', team: 'TOT', g: 47, pts: '29.1', reb: '6.7', ast: '5.0', stamp: 'All-Star' },
  { year: '23-24', team: 'PHX', g: 75, pts: '27.1', reb: '6.6', ast: '5.0', stamp: 'All-Star' },
  { year: '24-25', team: 'PHX', g: 62, pts: '26.6', reb: '6.0', ast: '4.2', stamp: 'All-Star' },
];

/** The seasons option B keeps: rookie year, the peak, each move, and the latest. */
const KEY_YEARS = ['07-08', '09-10', '13-14', '16-17', '20-21', '23-24', '24-25'];

@Component({
  selector: 'app-back-of-the-card-mock',
  imports: [MockOption],
  template: `
    <section class="mk-game">
      <h2>Back of the Card <span class="mk-chip">Next up</span></h2>
      <p class="mk-sub">
        Guess today's player from the back of their card: every season's numbers, with the name blacked out. Each miss
        uncovers more of the card, in this order: teams, then position and size, then award stamps, then the fact panel.
        A win flips the card to its front. Drawn in the site's own colours; the data is Kevin Durant's.
      </p>

      <div class="mk-row">
        <app-mock-option
          game="back-of-the-card"
          letter="A"
          name="Every season, opening"
          bare
          plus="Feels like reading a real card back; long careers are a clue in themselves."
          minus="A 20-season career needs small type on a phone, and short careers leave the card half empty."
        >
          <div class="screen">
            <div class="bar"><span>‹</span><b>Back of the Card</b><span>No. 12</span></div>
            <div class="card">
              <div class="head">
                <span class="redact">████ ███████</span>
                <span class="no">No. 12</span>
              </div>
              <div class="bio">
                <span>Pos <i class="redact">▒▒</i></span>
                <span>Ht <i class="redact">▒▒▒</i></span>
                <span>Drafted <i class="redact">▒▒▒▒</i></span>
              </div>
              <table>
                <thead><tr><th>Year</th><th>Club</th><th>G</th><th>Pts</th><th>Reb</th><th>Ast</th></tr></thead>
                <tbody>
                  @for (s of seasons; track s.year) {
                    <tr><td>{{ s.year }}</td><td><i class="redact">▒▒▒</i></td><td>{{ s.g }}</td><td>{{ s.pts }}</td><td>{{ s.reb }}</td><td>{{ s.ast }}</td></tr>
                  }
                </tbody>
                <tfoot><tr><td>Career</td><td></td><td>1,123</td><td>27.2</td><td>7.0</td><td>4.4</td></tr></tfoot>
              </table>
              <div class="fact locked">Fact panel unlocks after 4 misses</div>
            </div>
            <ol class="ladder">
              <li class="next">Teams</li><li>Position</li><li>Awards</li><li>Fact</li>
            </ol>
            <div class="guess miss">Carmelo Anthony <span>✕</span></div>
            <div class="input">Guess 2 of 8 <b>Search players</b></div>
          </div>
        </app-mock-option>

        <app-mock-option
          game="back-of-the-card"
          letter="B"
          name="Key seasons, three misses"
          bare
          plus="Seven rows fit any phone, and award stamps sit on the season they were won, like a real card."
          minus="Choosing the key seasons is a rule we have to get right, and it hides some of the career's shape."
        >
          <div class="screen">
            <div class="bar"><span>‹</span><b>Back of the Card</b><span>No. 12</span></div>
            <div class="card">
              <div class="head">
                <span class="redact">████ ███████</span>
                <span class="no">No. 12</span>
              </div>
              <div class="bio open">
                <span>Pos <b>F</b></span>
                <span>Ht <b>6′11″</b></span>
                <span>Drafted <i class="redact">▒▒▒▒</i></span>
              </div>
              <table class="roomy">
                <thead><tr><th>Year</th><th>Club</th><th>G</th><th>Pts</th><th>Reb</th><th>Ast</th></tr></thead>
                <tbody>
                  @for (s of keySeasons; track s.year) {
                    <tr>
                      <td>{{ s.year }}</td>
                      <td><b>{{ s.team }}</b>@if (stampFor(s); as st) {<em class="stamp" [class.big]="st !== 'ROY'">{{ st }}</em>}</td>
                      <td>{{ s.g }}</td><td>{{ s.pts }}</td>
                      <td>{{ s.reb }}</td><td>{{ s.ast }}</td>
                    </tr>
                  }
                </tbody>
                <tfoot><tr><td>Career</td><td>17 yrs</td><td>1,123</td><td>27.2</td><td>7.0</td><td>4.4</td></tr></tfoot>
              </table>
              <div class="fact locked">Fact panel unlocks after 4 misses</div>
            </div>
            <ol class="ladder">
              <li class="done">Teams</li><li class="done">Position</li><li class="done">Awards</li><li class="next">Fact</li>
            </ol>
            <div class="guess miss">Carmelo Anthony <span>✕</span></div>
            <div class="guess miss">Paul George <span>✕</span></div>
            <div class="guess miss">Kevin Garnett <span>✕</span></div>
            <div class="input">Guess 4 of 8 <b>Search players</b></div>
          </div>
        </app-mock-option>

        <app-mock-option
          game="back-of-the-card"
          letter="C"
          name="Win: the card flips"
          bare
          plus="The flip is the payoff, and it reuses the foil tiers and flip reveal every game already has."
          minus="Needs a headshot; when the photo fails to load the front falls back to a plain name plate."
        >
          <div class="screen">
            <div class="bar"><span>‹</span><b>Back of the Card</b><span>No. 12</span></div>
            <div class="front" [class.flipped]="flipped()" (click)="flipped.set(!flipped())">
              <div class="face">
                <i class="kb-shine" aria-hidden="true"></i>
                <div class="photo">
                  @if (!photoFailed()) {
                    <img [src]="photo" alt="" (error)="photoFailed.set(true)" />
                  } @else {
                    <span>KD</span>
                  }
                </div>
                <div class="plate">
                  <span class="name">Kevin Durant</span>
                  <span class="meta">Forward · OKC, GSW, BKN, PHX</span>
                </div>
                <span class="tier">Got it in 4</span>
              </div>
            </div>
            <div class="fact">
              <b>Did you know?</b> He led the league in scoring four times before he turned 26.
            </div>
            <div class="actions">
              <span class="btn primary">Share</span>
              <span class="btn">Next game</span>
            </div>
            <p class="tap">Tap the card to flip it back</p>
          </div>
        </app-mock-option>
      </div>
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    /* The site's trading card colours, set here because the gallery itself keeps the old dark palette. */
    .screen {
      --ink: #1d2b53; --paper: #f6ecd2; --stock: #fbf5e2; --red: #b92d22; --mustard: #e0a526; --teal: #1f6f6b;
      --tan: #d9cba6; --mute: #4f587a;
      height: 100%; display: flex; flex-direction: column; gap: 9px; padding: 0 12px 12px; overflow: hidden;
      background: var(--paper) radial-gradient(rgb(29 43 83 / .07) 1px, transparent 1.4px) 0 0 / 5px 5px;
      color: var(--ink); font-family: "Rokkitt", Georgia, serif; font-weight: 500;
    }
    .bar {
      display: flex; align-items: center; justify-content: space-between; margin: 0 -12px; padding: 10px 14px;
      border-bottom: 2px solid var(--ink); font-size: 13px;
    }
    .bar b { font: 400 22px/1 "Yellowtail", cursive; color: var(--red); transform: rotate(-3deg); }

    .card {
      background: var(--stock); border: 5px solid var(--ink); border-radius: 6px;
      box-shadow: inset 0 0 0 2px var(--paper), inset 0 0 0 3px var(--red), 3px 4px 0 rgb(29 43 83 / .16);
      padding: 6px 8px 8px; display: flex; flex-direction: column; gap: 4px;
    }
    .head {
      display: flex; justify-content: space-between; align-items: center; margin: -1px -3px 0;
      padding: 4px 8px; background: var(--red); color: var(--paper); border-radius: 3px;
    }
    .head .redact { font-size: 15px; letter-spacing: -1px; color: var(--ink); }
    .head .no { font: 400 11px/1 "Alfa Slab One", Georgia, serif; letter-spacing: .06em; }
    .bio { display: flex; gap: 10px; font-size: 11px; color: var(--mute); text-transform: uppercase; letter-spacing: .04em; }
    .bio b { color: var(--ink); }
    .redact { font-style: normal; color: var(--ink); opacity: .85; }

    table { width: 100%; border-collapse: collapse; font-size: 10.5px; line-height: 1.15; font-variant-numeric: tabular-nums; }
    th { font: 400 9px/1 "Alfa Slab One", Georgia, serif; color: var(--red); text-transform: uppercase; letter-spacing: .04em; padding: 3px 2px; border-bottom: 1.5px solid var(--ink); }
    td { padding: 1.5px 2px; text-align: right; }
    th { text-align: right; }
    th:nth-child(-n+2), td:nth-child(-n+2) { text-align: left; }
    tbody tr:nth-child(even) td { background: rgb(217 203 166 / .35); }
    tfoot td { border-top: 1.5px solid var(--ink); font-weight: 700; padding-top: 3px; }
    table.roomy { font-size: 13px; }
    table.roomy td { padding: 4px 2px; }
    .stamp {
      display: inline-block; margin-left: 6px; transform: rotate(-8deg);
      font: 400 8px/1 "Alfa Slab One", Georgia, serif; text-transform: uppercase; font-style: normal;
      color: var(--teal); border: 1.5px solid var(--teal); border-radius: 3px; padding: 2px 3px; background: var(--stock);
    }
    .stamp.big { color: var(--red); border-color: var(--red); }

    .fact { font-size: 13px; line-height: 1.3; padding: 7px 9px; border: 1.5px dashed var(--ink); border-radius: 4px; background: var(--stock); }
    .fact.locked { text-align: center; color: var(--mute); font-size: 11px; text-transform: uppercase; letter-spacing: .06em; padding: 5px; }
    .fact b { color: var(--red); }

    .ladder { list-style: none; margin: 0; padding: 0; display: flex; gap: 4px; }
    .ladder li {
      flex: 1; text-align: center; font-size: 10px; text-transform: uppercase; letter-spacing: .05em; padding: 3px 0;
      border: 1.5px solid var(--ink); border-radius: 999px; color: var(--mute);
    }
    .ladder .done { background: var(--ink); color: var(--paper); }
    .ladder .next { border-style: dashed; color: var(--ink); font-weight: 700; }

    .guess {
      display: flex; justify-content: space-between; padding: 5px 10px; border-radius: 4px; font-size: 14px;
      background: var(--tan); color: #54482b; border: 1.5px solid #9a8c66;
    }
    .input {
      margin-top: auto; display: flex; justify-content: space-between; align-items: center; padding: 9px 12px;
      border: 2px solid var(--ink); border-radius: 999px; background: var(--stock); font-size: 14px; color: var(--mute);
    }
    .input b { color: var(--ink); font-weight: 500; }

    .front { perspective: 900px; margin-top: 6px; cursor: pointer; }
    .face {
      position: relative; isolation: isolate; overflow: hidden; height: 330px; display: flex; flex-direction: column;
      background: var(--stock); border: 6px solid var(--teal); border-radius: 8px;
      box-shadow: inset 0 0 0 2px var(--paper), inset 0 0 0 4px var(--ink), 3px 5px 0 rgb(29 43 83 / .16);
      animation: flip .9s ease-out both; transition: transform .5s;
    }
    .front.flipped .face { transform: rotateY(180deg); }
    @keyframes flip { from { transform: rotateY(180deg); } to { transform: rotateY(0); } }
    .face .kb-shine { z-index: -1; }
    .photo {
      flex: 1; margin: 12px 12px 0; border: 2px solid var(--ink); border-radius: 3px; overflow: hidden;
      background: linear-gradient(160deg, #2c6b8f, #1d2b53); display: grid; place-items: center;
    }
    .photo img { width: 100%; height: 100%; object-fit: cover; object-position: top; }
    .photo span { font: 400 64px/1 "Alfa Slab One", Georgia, serif; color: rgb(246 236 210 / .5); }
    .plate { margin: 0 12px 12px; padding: 6px 10px 8px; background: var(--red); color: var(--paper); border: 2px solid var(--ink); border-top: 0; }
    .plate .name { display: block; font: 400 30px/1 "Yellowtail", cursive; }
    .plate .meta { font-size: 12px; text-transform: uppercase; letter-spacing: .06em; }
    .tier {
      position: absolute; top: 22px; right: -46px; transform: rotate(35deg); padding: 3px 48px; white-space: nowrap;
      background: var(--mustard); color: var(--ink); font: 400 10px/1.2 "Alfa Slab One", Georgia, serif; text-transform: uppercase;
    }
    .actions { display: flex; gap: 8px; }
    .btn {
      flex: 1; text-align: center; padding: 9px; border-radius: 999px; border: 2px solid var(--ink); font-size: 15px; font-weight: 700;
    }
    .btn.primary { background: var(--red); color: var(--paper); }
    .tap { margin: 0; text-align: center; font-size: 12px; color: var(--mute); }
  `,
})
export class BackOfTheCardMock {
  protected readonly seasons = SEASONS;
  protected readonly keySeasons = SEASONS.filter((s) => KEY_YEARS.includes(s.year));
  protected readonly photo = headshot(201142, '1040x760');
  protected readonly photoFailed = signal(false);
  protected readonly flipped = signal(false);

  /** Only the stamps a fan would point at: rookie of the year, MVP, titles. */
  protected stampFor(s: Season): string | undefined {
    return s.stamp && s.stamp !== 'All-Star' ? s.stamp : undefined;
  }
}
