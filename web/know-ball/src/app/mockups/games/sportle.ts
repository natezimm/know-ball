import { Component } from '@angular/core';
import { MockOption } from '../option';

// Kevin Durant's points per game by season, used for the chart sketch.
const PPG = [20.3, 25.3, 30.1, 27.7, 28, 28.1, 32, 25.4, 28.2, 25.1, 26.4, 26, 26.9, 29.9, 29.1, 27.1, 26.6];
const SELECTED = 6;

const chartX = (i: number): number => 30 + i * (274 / (PPG.length - 1));
const chartY = (ppg: number): number => 132 - (ppg - 18) * 7.5;

@Component({
  selector: 'app-sportle-mock',
  imports: [MockOption],
  template: `
    <section class="mk-game">
      <h2>Sportle <span class="mk-chip">Phase 3</span></h2>
      <p class="mk-sub">
        Guess the player from season-by-season numbers with the giveaways hidden. Each miss reveals more: teams first,
        then awards, then position.
      </p>

      <div class="mk-row">
        <app-mock-option
          game="sportle"
          letter="A"
          name="Stat table"
          bar="Sportle #12"
          plus="Reads like a Basketball-Reference page, which stat fans already know how to scan."
          minus="A wide table on a narrow screen: long careers scroll, and it is the driest of the three."
        >
          <div class="hints">
            <span class="mk-chip on">Teams shown</span>
            <span class="mk-chip">Awards next</span>
            <span class="mk-chip">Position last</span>
          </div>
          <table>
            <thead>
              <tr><th>Season</th><th>Tm</th><th>GP</th><th>PTS</th><th>REB</th><th>AST</th><th>Awards</th></tr>
            </thead>
            <tbody>
              @for (r of rows; track r.season) {
                <tr>
                  <td>{{ r.season }}</td><td>{{ r.team }}</td><td>{{ r.gp }}</td><td><b>{{ r.pts }}</b></td>
                  <td>{{ r.reb }}</td><td>{{ r.ast }}</td><td><span class="hidden">{{ r.award }}</span></td>
                </tr>
              }
            </tbody>
          </table>
          <div class="mk-guess">Carmelo Anthony</div>
          <div class="mk-guess">Dirk Nowitzki</div>
          <div class="mk-spacer"></div>
          <div class="mk-input"><span>Guess 3 of 8</span><b>↵</b></div>
        </app-mock-option>

        <app-mock-option
          game="sportle"
          letter="B"
          name="Trading card back"
          bar="Sportle #12"
          plus="A strong visual identity of its own, and written hints add personality beyond the numbers."
          minus="Each puzzle needs a few hint lines written or generated, on top of the stats."
        >
          <div class="tcard">
            <div class="thead"><b>████ ██████</b><span>Forward · 6′11″</span></div>
            <table>
              <thead>
                <tr><th>Year</th><th>Team</th><th>G</th><th>PTS</th><th>REB</th><th>AST</th></tr>
              </thead>
              <tbody>
                @for (r of rows; track r.season) {
                  <tr>
                    <td>{{ r.season }}</td><td>▒▒▒</td><td>{{ r.gp }}</td><td>{{ r.pts }}</td>
                    <td>{{ r.reb }}</td><td>{{ r.ast }}</td>
                  </tr>
                }
              </tbody>
            </table>
            <p><b>Hint 1 of 3.</b> Rookie of the Year in his first season.</p>
          </div>
          <span class="mk-btn ghost">Flip for another hint (2 left)</span>
          <div class="mk-guess">Carmelo Anthony</div>
          <div class="mk-spacer"></div>
          <div class="mk-input"><span>Guess 2 of 8</span><b>↵</b></div>
        </app-mock-option>

        <app-mock-option
          game="sportle"
          letter="C"
          name="Career arc"
          bar="Sportle #12"
          plus="Shows the shape of a career at a glance; the most original look, and it reuses the charts planned for GOAT."
          minus="One stat at a time, so there is less to go on per screen; needs a charting library."
        >
          <div class="hints">
            <span class="mk-chip on">Points</span>
            <span class="mk-chip">Rebounds</span>
            <span class="mk-chip">Assists</span>
          </div>
          <svg class="chart" viewBox="0 0 312 150">
            @for (g of gridlines; track g.label) {
              <line [attr.x1]="26" [attr.x2]="308" [attr.y1]="g.y" [attr.y2]="g.y" />
              <text [attr.x]="20" [attr.y]="g.y + 4" text-anchor="end">{{ g.label }}</text>
            }
            <polyline [attr.points]="line" />
            @for (p of points; track p.i) {
              <circle [attr.cx]="p.x" [attr.cy]="p.y" [attr.r]="p.selected ? 5 : 2.5" [class.selected]="p.selected" />
            }
          </svg>
          <div class="stints">
            <span class="shown" style="flex-grow: 9">Team 1</span>
            <span style="flex-grow: 3">?</span>
            <span style="flex-grow: 3">?</span>
            <span style="flex-grow: 2">?</span>
          </div>
          <div class="mk-card">
            <span class="mk-label">Season 7</span>
            <div class="season"><b>32.0 pts</b><b>7.4 reb</b><b>5.5 ast</b><span>Award ?</span></div>
          </div>
          <div class="mk-guess">Carmelo Anthony</div>
          <div class="mk-spacer"></div>
          <div class="mk-input"><span>Guess 2 of 8</span><b>↵</b></div>
        </app-mock-option>
      </div>
    </section>
  `,
  styles: `
    .hints { display: flex; flex-wrap: wrap; gap: 6px; }
    table { width: 100%; border-collapse: collapse; font-size: 12px; font-variant-numeric: tabular-nums; }
    th { font-size: 9px; text-transform: uppercase; letter-spacing: .05em; color: var(--muted); font-weight: 500; }
    th, td { padding: 6px 3px; text-align: right; border-bottom: 1px solid var(--line); }
    th:first-child, td:first-child, th:nth-child(2), td:nth-child(2) { text-align: left; }
    .hidden { filter: blur(5px); user-select: none; }

    .tcard { border-radius: 12px; overflow: hidden; background: #efe6d2; color: #2a2218; }
    .thead {
      display: flex; justify-content: space-between; align-items: center; padding: 8px 12px;
      background: var(--accent); color: var(--accent-ink); font-size: 12px;
    }
    .thead b { font-family: var(--display); font-size: 16px; letter-spacing: .04em; }
    .tcard table { margin: 4px 0; }
    .tcard th { color: #6b5d48; }
    .tcard th, .tcard td { border-color: #d6c9ad; }
    .tcard th:first-child, .tcard td:first-child { padding-left: 12px; }
    .tcard th:last-child, .tcard td:last-child { padding-right: 12px; }
    .tcard p { margin: 0; padding: 6px 12px 10px; font-size: 12px; }

    .chart { display: block; width: 100%; flex: none; }
    .chart line { stroke: var(--line); }
    .chart text { fill: var(--muted); font-size: 10px; }
    .chart polyline { fill: none; stroke: var(--muted); stroke-width: 2; stroke-linejoin: round; }
    .chart circle { fill: var(--muted); }
    .chart .selected { fill: var(--accent); stroke: var(--bg); stroke-width: 2; }
    .stints { display: flex; gap: 2px; margin: -6px 0 0 26px; }
    .stints span {
      flex: 1 1 0; text-align: center; font-size: 10px; padding: 3px 0; border-radius: 4px;
      background: var(--surface-2); color: var(--muted);
    }
    .stints .shown { background: var(--accent); color: var(--accent-ink); font-weight: 700; }
    .season { display: flex; gap: 12px; align-items: baseline; margin-top: 2px; font-size: 14px; }
    .season span { margin-left: auto; color: var(--muted); font-size: 12px; }
  `,
})
export class SportleMock {
  protected readonly rows = [
    { season: '07–08', team: 'SEA', gp: 80, pts: '20.3', reb: '4.4', ast: '2.4', award: 'ROY' },
    { season: '09–10', team: 'OKC', gp: 82, pts: '30.1', reb: '7.6', ast: '2.8', award: 'All-NBA' },
    { season: '13–14', team: 'OKC', gp: 81, pts: '32.0', reb: '7.4', ast: '5.5', award: 'MVP' },
    { season: '16–17', team: 'GSW', gp: 62, pts: '25.1', reb: '8.3', ast: '4.8', award: 'Champ' },
    { season: '20–21', team: 'BKN', gp: 35, pts: '26.9', reb: '7.1', ast: '5.6', award: 'All-Star' },
    { season: '23–24', team: 'PHX', gp: 75, pts: '27.1', reb: '6.6', ast: '5.0', award: 'All-NBA' },
  ];

  protected readonly gridlines = [20, 25, 30].map((v) => ({ label: v, y: chartY(v) }));
  protected readonly points = PPG.map((ppg, i) => ({ i, x: chartX(i), y: chartY(ppg), selected: i === SELECTED }));
  protected readonly line = this.points.map((p) => `${p.x},${p.y}`).join(' ');
}
