import { Component, input } from '@angular/core';
import { headshot, logo } from '../mock-data';
import { MockOption } from '../option';

type LonLat = [number, number];

// Rough outline of the contiguous US, clockwise from the Pacific Northwest. A stand-in for real map tiles.
const OUTLINE: LonLat[] = [
  [-124.7, 48.4], [-123, 49], [-95.2, 49], [-89.6, 48], [-84.8, 46.5], [-82.5, 45.3], [-82.4, 43], [-83.1, 42],
  [-79, 42.8], [-76.3, 43.6], [-75, 45], [-71.5, 45], [-69.2, 47.4], [-67.8, 47.1], [-67, 44.8], [-70, 43.7],
  [-70.7, 42.5], [-70, 41.7], [-71.5, 41.3], [-74, 40.6], [-75.5, 38.5], [-76, 37], [-75.5, 35.3], [-77.9, 33.9],
  [-80, 32.5], [-81.3, 30.7], [-80.1, 27], [-80.3, 25.3], [-81.7, 25.9], [-82.8, 28], [-84, 30], [-85.5, 29.7],
  [-88, 30.3], [-89.5, 29.2], [-91.5, 29.5], [-94, 29.6], [-97.2, 27.8], [-97.4, 25.9], [-99.5, 27.5],
  [-101.4, 29.8], [-102.8, 29.3], [-104.5, 29.7], [-106.5, 31.8], [-108.2, 31.3], [-111, 31.3], [-114.8, 32.5],
  [-117.1, 32.5], [-118.5, 34], [-120.6, 34.6], [-122, 36.9], [-122.5, 37.8], [-123.8, 39.8], [-124.3, 40.4],
  [-124.2, 42], [-124.1, 46.2],
];

// Map window: longitude -125 to -66, latitude 50 to 24.
const WEST = -125;
const NORTH = 50;
const WIDTH = 59;
const HEIGHT = 26;

/** Placeholder map: an outline plus an optional guess pin, answer pin and the line between them. */
@Component({
  selector: 'app-mock-map',
  template: `
    <svg [attr.viewBox]="viewBox" preserveAspectRatio="none">
      <polygon [attr.points]="outline" />
      @if (guess(); as g) {
        @if (answer(); as a) {
          <line [attr.x1]="g[0]" [attr.y1]="-g[1]" [attr.x2]="a[0]" [attr.y2]="-a[1]" />
        }
      }
    </svg>
    @if (answer(); as a) {
      <span class="pin answer" [style.left.%]="x(a)" [style.top.%]="y(a)"></span>
    }
    @if (guess(); as g) {
      <span class="pin" [style.left.%]="x(g)" [style.top.%]="y(g)"></span>
    }
  `,
  styles: `
    :host { display: block; position: relative; aspect-ratio: 1.79; }
    svg { display: block; width: 100%; height: 100%; }
    polygon { fill: #24384a; stroke: #40586e; stroke-width: 1.5; vector-effect: non-scaling-stroke; }
    line { stroke: var(--text); stroke-width: 2; stroke-dasharray: 5 4; vector-effect: non-scaling-stroke; }
    .pin {
      position: absolute; width: 16px; height: 16px; margin: -8px 0 0 -8px; border-radius: 50%;
      background: var(--accent); border: 3px solid var(--bg); box-shadow: 0 0 0 2px var(--accent);
    }
    .pin.answer { background: var(--ok); box-shadow: 0 0 0 2px var(--ok); }
  `,
})
export class MockMap {
  readonly guess = input<LonLat>();
  readonly answer = input<LonLat>();

  protected readonly viewBox = `${WEST} ${-NORTH} ${WIDTH} ${HEIGHT}`;
  protected readonly outline = OUTLINE.map(([lon, lat]) => `${lon},${-lat}`).join(' ');

  protected x([lon]: LonLat): number {
    return ((lon - WEST) / WIDTH) * 100;
  }

  protected y([, lat]: LonLat): number {
    return ((NORTH - lat) / HEIGHT) * 100;
  }
}

// The common tile-grid layout of US states: 11 columns, "." is an empty cell.
const STATE_GRID = [
  'AK . . . . . . . . . ME',
  '. . . . . . . . . VT NH',
  'WA ID MT ND MN IL WI MI NY RI MA',
  'OR NV WY SD IA IN OH PA NJ CT .',
  'CA UT CO NE MO KY WV VA MD DE .',
  '. AZ NM KS AR TN NC SC DC . .',
  '. . . OK LA MS AL GA . . .',
  'HI . . TX . . . . FL . .',
];

@Component({
  selector: 'app-geo-sports-mock',
  imports: [MockOption, MockMap],
  template: `
    <section class="mk-game">
      <h2>Geo Sports <span class="mk-chip">Phase 2</span></h2>
      <p class="mk-sub">
        Find an arena or a player's hometown on the map; closer scores more, over five rounds. The map here is a rough
        outline standing in for real, zoomable map tiles.
      </p>

      <div class="mk-row">
        <app-mock-option
          game="geo-sports"
          letter="A"
          name="Full-screen map"
          bar="Geo Sports #31"
          flush
          plus="The map gets the whole screen, which makes placing a pin precisely much easier on a phone."
          minus="The question and score float over the map and can cover the spot you want to tap."
        >
          <div class="full">
            <app-mock-map class="zoom" [guess]="saltLake" />
            <div class="mk-card prompt float">
              <span class="disc"><img [src]="logo('DEN')" alt="" /></span>
              <div><span class="mk-label">Round 2 of 5</span><b>Where do the Nuggets play?</b></div>
            </div>
            <div class="dock">
              <span class="mk-chip">Score 4,120</span>
              <span class="mk-btn">Confirm pin</span>
            </div>
          </div>
        </app-mock-option>

        <app-mock-option
          game="geo-sports"
          letter="B"
          name="Card, map, result"
          bar="Geo Sports #31"
          plus="Clear separation of question, map and result; the distance reveal is the payoff and reads well."
          minus="The map is small, so fine placement needs pinch-zoom."
        >
          <div class="mk-card prompt">
            <span class="disc"><img [src]="logo('DEN')" alt="" /></span>
            <div><span class="mk-label">Round 2 of 5</span><b>Where do the Nuggets play?</b></div>
          </div>
          <div class="mapcard"><app-mock-map [guess]="saltLake" [answer]="denver" /></div>
          <div class="mk-card result">
            <div><strong>371 miles off</strong><small>Ball Arena, Denver</small></div>
            <b>+3,150</b>
          </div>
          <div class="rounds">
            @for (r of rounds; track $index) {
              <span [class.done]="r">{{ r || '·' }}</span>
            }
          </div>
          <div class="mk-spacer"></div>
          <span class="mk-btn">Next round</span>
        </app-mock-option>

        <app-mock-option
          game="geo-sports"
          letter="C"
          name="Tap the state"
          bar="Geo Sports #31"
          plus="No map library, no zooming and big tap targets; warmer and colder feedback gives several tries."
          minus="Only as precise as a state, so it suits hometowns better than arenas, and US only."
        >
          <div class="mk-card prompt">
            <img class="face" [src]="headshot(201939)" alt="" />
            <div><span class="mk-label">Round 2 of 5 · Hometown</span><b>Which state was Stephen Curry born in?</b></div>
          </div>
          <div class="states">
            @for (s of states; track $index) {
              <span [class]="stateClass(s)">{{ s === '.' ? '' : s }}</span>
            }
          </div>
          <div class="scale"><span>Colder</span><i></i><span>Warmer</span></div>
          <div class="mk-guess">California · about 2,100 miles</div>
          <div class="mk-guess">North Carolina · about 400 miles</div>
          <div class="mk-spacer"></div>
          <p class="mk-small">2 taps left</p>
        </app-mock-option>
      </div>
    </section>
  `,
  styles: `
    .prompt { display: flex; align-items: center; gap: 10px; }
    .prompt div { display: grid; }
    .prompt b { font-size: 15px; line-height: 1.25; }
    .disc { width: 40px; height: 40px; border-radius: 50%; background: #fff; display: grid; place-items: center; flex: none; }
    .disc img { width: 30px; height: 30px; }
    .face { width: 44px; height: 44px; border-radius: 50%; object-fit: cover; background: var(--surface-2); flex: none; }

    .full { flex: 1; position: relative; overflow: hidden; background: #101c28; }
    .zoom { position: absolute; width: 165%; left: -6%; top: 150px; }
    .float { position: absolute; top: 12px; left: 12px; right: 12px; box-shadow: 0 6px 18px rgb(0 0 0 / .45); }
    .dock { position: absolute; left: 12px; right: 12px; bottom: 12px; display: flex; align-items: center; gap: 10px; }
    .dock .mk-chip { background: var(--surface); }
    .dock .mk-btn { flex: 1; }

    .mapcard { border-radius: 12px; overflow: hidden; background: #101c28; padding: 14px 8px; }
    .result { display: flex; align-items: center; justify-content: space-between; }
    .result div { display: grid; }
    .result small { color: var(--muted); font-size: 12px; }
    .result b { font-family: var(--display); font-size: 24px; color: var(--ok); }
    .rounds { display: grid; grid-template-columns: repeat(5, 1fr); gap: 4px; }
    .rounds span {
      text-align: center; font-size: 12px; padding: 5px 0; border-radius: 6px;
      border: 1px solid var(--line); color: var(--muted);
    }
    .rounds .done { background: var(--surface); color: var(--text); }

    .states { display: grid; grid-template-columns: repeat(11, 1fr); gap: 2px; }
    .states span {
      aspect-ratio: 1; border-radius: 4px; background: var(--surface-2); display: grid; place-items: center;
      font-size: 9px; font-weight: 600;
    }
    .states .gap { background: none; }
    .states .cold { background: #2f6fb3; color: #fff; }
    .states .warm { background: #d9772b; color: #fff; }
    .scale { display: flex; align-items: center; gap: 8px; font-size: 11px; color: var(--muted); }
    .scale i { flex: 1; height: 6px; border-radius: 3px; background: linear-gradient(90deg, #2f6fb3, #d9772b, var(--ok)); }
  `,
})
export class GeoSportsMock {
  protected readonly logo = logo;
  protected readonly headshot = headshot;
  protected readonly saltLake: LonLat = [-111.9, 40.8];
  protected readonly denver: LonLat = [-105, 39.75];
  protected readonly rounds = ['4,120', '3,150', '', '', ''];
  protected readonly states = STATE_GRID.flatMap((row) => row.split(' '));

  protected stateClass(state: string): string {
    if (state === '.') return 'gap';
    if (state === 'CA') return 'cold';
    if (state === 'NC') return 'warm';
    return '';
  }
}
