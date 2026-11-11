import { Component, ChangeDetectionStrategy, computed, input, signal } from '@angular/core';
import { headshot, logo } from '../mock-data';
import { MockOption } from '../option';

interface Slot {
  name: string;
  short: string;
  pos: string;
  line: string;
  id: number;
}

// 2015-16 Golden State Warriors from the imported per-team season lines: the five with the most starts,
// then the three bench players with the most minutes.
const STARTERS: Slot[] = [
  { name: 'Harrison Barnes', short: 'Barnes', pos: 'SF', line: '11.7 pts · 4.9 reb', id: 203084 },
  { name: 'Draymond Green', short: 'Green', pos: 'PF', line: '14.0 pts · 9.5 reb · 7.4 ast', id: 203110 },
  { name: 'Andrew Bogut', short: 'Bogut', pos: 'C', line: '5.4 pts · 7.0 reb', id: 101106 },
  { name: 'Stephen Curry', short: 'Curry', pos: 'PG', line: '30.1 pts · 6.7 ast', id: 201939 },
  { name: 'Klay Thompson', short: 'Thompson', pos: 'SG', line: '22.1 pts · 3.8 reb', id: 202691 },
];

const BENCH: Slot[] = [
  { name: 'Andre Iguodala', short: 'Iguodala', pos: 'SF', line: '7.0 pts · 4.0 reb', id: 2738 },
  { name: 'Shaun Livingston', short: 'Livingston', pos: 'PG', line: '6.3 pts · 3.0 ast', id: 2733 },
  { name: 'Leandro Barbosa', short: 'Barbosa', pos: 'SG', line: '6.4 pts · 1.7 reb', id: 2571 },
];

/** One spot in the photo: a blank figure with its clue, or the player once named. */
@Component({
  selector: 'app-tp-spot',
  template: `
    <div class="frame" [class.found]="found()" [class.small]="small()">
      @if (found()) {
        <span class="initials">{{ initials() }}</span>
        @if (!failed()) {
          <img [src]="src()" alt="" (error)="failed.set(true)" />
        }
      } @else {
        <span class="figure" aria-hidden="true"><i></i><u></u></span>
        <span class="pos">{{ slot().pos }}</span>
      }
    </div>
    <span class="plate" [class.found]="found()">
      @if (found()) {
        <b>{{ slot().short }}</b>
      } @else {
        <small>{{ slot().line }}</small>
      }
    </span>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    :host { display: grid; justify-items: center; gap: 4px; min-width: 0; }
    .frame {
      position: relative; width: 74px; height: 74px; border-radius: 50%; overflow: hidden;
      background: rgb(255 255 255 / .06); border: 2px dashed rgb(255 255 255 / .28); display: grid; place-items: end center;
    }
    .frame.small { width: 54px; height: 54px; }
    .frame.found { border: 2px solid var(--accent); background: #2a3c50; place-items: center; }
    .frame img { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; object-position: top; }
    .initials { font-family: var(--display); font-size: 20px; color: var(--text); }
    .figure { position: relative; width: 70%; height: 78%; opacity: .35; }
    .figure i { position: absolute; left: 32%; top: 6%; width: 36%; aspect-ratio: 1; border-radius: 50%; background: #c9d6e3; }
    .figure u { position: absolute; left: 6%; right: 6%; bottom: -4%; height: 46%; border-radius: 50% 50% 0 0; background: #c9d6e3; }
    .pos {
      position: absolute; top: 4px; left: 50%; transform: translateX(-50%); font-family: var(--display);
      font-size: 13px; letter-spacing: .06em; color: var(--text);
    }
    .plate {
      max-width: 96px; padding: 2px 6px; border-radius: 4px; background: rgb(0 0 0 / .35);
      text-align: center; line-height: 1.2;
    }
    .plate b { font-size: 12px; }
    .plate small { font-size: 10px; color: var(--muted); }
    .plate.found { background: var(--accent); color: var(--accent-ink); }
  `,
})
export class TeamPhotoSpot {
  readonly slot = input.required<Slot>();
  readonly found = input(false);
  readonly small = input(false);

  protected readonly failed = signal(false);
  protected readonly src = computed(() => headshot(this.slot().id));
  protected readonly initials = computed(() =>
    this.slot()
      .name.split(' ')
      .map((p) => p[0])
      .join(''),
  );
}

@Component({
  selector: 'app-team-photo-mock',
  imports: [MockOption, TeamPhotoSpot],
  template: `
    <section class="mk-game">
      <h2>Team Photo <span class="mk-chip">Next up</span></h2>
      <p class="mk-sub">
        A blank team photo of one famous team-season. Name the players to fill it in: the five who started the most
        games, with an optional bench row. Sample: the 2015–16 Warriors, drawn from the season lines we already import.
      </p>

      <div class="mk-row">
        <app-mock-option
          game="team-photo"
          letter="A"
          name="Starting five"
          bar="Team Photo #1"
          plus="One clear goal that fits any phone: five spots, big faces, a stat line under each blank to nudge you."
          minus="Only five answers, so a famous team can be done in a minute by fans of that era."
        >
          <div class="mk-input"><span>Name a player…</span>⌕</div>
          <div class="photo">
            <div class="plaque">
              <span class="disc"><img [src]="logo('GSW')" alt="" /></span>
              <div><b>2015–16 Golden State</b><small>73–9 · started the most games</small></div>
            </div>
            <div class="back">
              @for (s of backRow; track s.id) {
                <app-tp-spot [slot]="s" [found]="s.short === 'Green'" />
              }
            </div>
            <div class="front">
              @for (s of frontRow; track s.id) {
                <app-tp-spot [slot]="s" [found]="true" />
              }
            </div>
          </div>
          <div class="mk-meta"><span>3 of 5 in the photo</span><span>Misses 1 of 4</span></div>
          <div class="mk-guess">Kevin Durant · not on this team</div>
        </app-mock-option>

        <app-mock-option
          game="team-photo"
          letter="B"
          name="Five plus the bench"
          bar="Team Photo #1"
          plus="Naming a bench player is never a miss: he takes a seat on the bench, so knowing the team always pays."
          minus="Eight spots is a lot on a phone; the bench faces get small and the stat clues get cramped."
        >
          <div class="mk-input"><span>Name a player…</span>⌕</div>
          <div class="photo">
            <div class="plaque">
              <span class="disc"><img [src]="logo('GSW')" alt="" /></span>
              <div><b>2015–16 Golden State</b><small>73–9</small></div>
            </div>
            <div class="back">
              @for (s of backRow; track s.id) {
                <app-tp-spot [slot]="s" [found]="s.short === 'Green'" />
              }
            </div>
            <div class="front">
              @for (s of frontRow; track s.id) {
                <app-tp-spot [slot]="s" [found]="true" />
              }
            </div>
            <div class="bench">
              <span class="mk-label">Bench</span>
              <div class="bench-row">
                @for (s of bench; track s.id) {
                  <app-tp-spot [slot]="s" [found]="s.short === 'Iguodala'" [small]="true" />
                }
              </div>
            </div>
          </div>
          <div class="mk-meta"><span>Starters 3 of 5 · bench 1 of 3</span><span>Misses 1 of 4</span></div>
          <div class="note">Andre Iguodala came off the bench · no miss</div>
        </app-mock-option>

        <app-mock-option
          game="team-photo"
          letter="C"
          name="The finished photo"
          bar="Team Photo #1"
          plus="The photo itself is the result: every face in place, with a caption you would find under a real one."
          minus="A lost game shows the missing faces greyed out; that needs care so it doesn't read like a win."
        >
          <div class="photo done">
            <div class="plaque">
              <span class="disc"><img [src]="logo('GSW')" alt="" /></span>
              <div><b>2015–16 Golden State</b><small>73–9</small></div>
            </div>
            <div class="back">
              @for (s of backRow; track s.id) {
                <app-tp-spot [slot]="s" [found]="true" />
              }
            </div>
            <div class="front">
              @for (s of frontRow; track s.id) {
                <app-tp-spot [slot]="s" [found]="true" />
              }
            </div>
            <p class="caption">
              Back row, left to right: Harrison Barnes, Draymond Green, Andrew Bogut. Front row: Stephen Curry, Klay
              Thompson.
            </p>
          </div>
          <div class="mk-card result">
            <div><strong>Full photo</strong><small>5 of 5 · 1 miss · bench 1 of 3</small></div>
            <b>●●●●●</b>
          </div>
          <div class="mk-spacer"></div>
          <div class="mk-pair">
            <span class="mk-btn ghost">Stats</span>
            <span class="mk-btn">Share</span>
          </div>
        </app-mock-option>
      </div>
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .photo {
      display: grid; gap: 10px; padding: 12px 8px 14px; border-radius: 12px; border: 6px solid #e9e2d0;
      background:
        repeating-linear-gradient(180deg, transparent 0 22px, rgb(255 255 255 / .03) 22px 24px),
        linear-gradient(180deg, #1d4f86 0%, #183a5f 58%, #6b4a2b 58%, #5a3d22 100%);
      box-shadow: 0 6px 18px rgb(0 0 0 / .4);
    }
    .plaque { display: flex; align-items: center; gap: 8px; justify-self: center; padding: 4px 10px 4px 4px; border-radius: 999px; background: rgb(0 0 0 / .4); }
    .plaque div { display: grid; line-height: 1.15; }
    .plaque b { font-family: var(--display); font-size: 14px; letter-spacing: .03em; }
    .plaque small { font-size: 11px; color: var(--muted); }
    .disc { width: 30px; height: 30px; border-radius: 50%; background: #fff; display: grid; place-items: center; flex: none; }
    .disc img { width: 22px; height: 22px; }
    .back { display: grid; grid-template-columns: repeat(3, 1fr); gap: 4px; }
    .front { display: grid; grid-template-columns: repeat(2, 96px); justify-content: center; gap: 22px; }
    .bench { display: grid; gap: 4px; padding-top: 8px; border-top: 1px dashed rgb(255 255 255 / .25); }
    .bench .mk-label { justify-self: center; color: #e9e2d0; }
    .bench-row { display: grid; grid-template-columns: repeat(3, 1fr); }
    .caption { margin: 0; padding: 6px 4px 0; font-size: 11px; line-height: 1.35; color: #e9e2d0; font-style: italic; text-align: center; }
    .note {
      padding: 7px 10px; border-radius: 8px; background: var(--surface); font-size: 13px;
      border-left: 3px solid var(--accent);
    }
    .result { display: flex; align-items: center; justify-content: space-between; }
    .result div { display: grid; }
    .result small { color: var(--muted); font-size: 12px; }
    .result b { color: var(--accent); letter-spacing: .1em; }
  `,
})
export class TeamPhotoMock {
  protected readonly logo = logo;
  protected readonly backRow = STARTERS.slice(0, 3);
  protected readonly frontRow = STARTERS.slice(3);
  protected readonly bench = BENCH;
}
