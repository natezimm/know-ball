import { Component, ViewEncapsulation } from '@angular/core';
import { ThemeScreen } from '../theme-screen';

/** A coach's chalkboard: hand-drawn boxes, chalk lettering, circles and strike-throughs like a play diagram. */
@Component({
  selector: 'app-theme-chalk-talk',
  imports: [ThemeScreen],
  encapsulation: ViewEncapsulation.None,
  host: { class: 'th-host th-chalk' },
  template: `<app-theme-screen />`,
  styles: `
    .th-chalk .ts {
      --chalk: #f4f1e8; --yellow: #ffe066; --board: #23402f;
      --hand: 255px 15px 225px 15px / 15px 225px 15px 255px;
      background:
        radial-gradient(ellipse at 25% 15%, rgb(255 255 255 / .08), transparent 55%),
        radial-gradient(ellipse at 85% 80%, rgb(255 255 255 / .05), transparent 50%), var(--board);
      color: var(--chalk); font-family: "Patrick Hand", "Comic Sans MS", cursive;
      box-shadow: inset 0 0 0 7px #7a4e24, inset 0 0 0 9px #55361a;
    }
    .th-chalk .ts-bar { padding: 18px 20px 4px; }
    .th-chalk .ts-main { padding: 8px 20px 20px; gap: 9px; }
    .th-chalk .ts-brand, .th-chalk .ts-title, .th-chalk .ts-tile { font-family: "Cabin Sketch", "Patrick Hand", cursive; font-weight: 700; }
    .th-chalk .ts-brand { font-size: 19px; }
    .th-chalk .ts-mark { box-sizing: border-box; border: 2px solid var(--chalk); border-radius: 50%; }
    .th-chalk .ts-nav span { font-size: 15px; padding: 0 6px; opacity: .7; }
    .th-chalk .ts-nav .on { opacity: 1; text-decoration: underline wavy var(--yellow); text-underline-offset: 4px; }
    .th-chalk .ts-title { font-size: 36px; color: var(--yellow); transform: rotate(-3deg); }
    .th-chalk .ts-btn, .th-chalk .ts-chip, .th-chalk .ts-seg i, .th-chalk .ts-tile, .th-chalk .ts-key {
      border: 2px solid var(--chalk); border-radius: var(--hand);
    }
    .th-chalk .ts-btn, .th-chalk .ts-chip { font-size: 15px; padding: 2px 11px; }
    .th-chalk .ts-chip { border-style: dashed; }
    .th-chalk .ts-chip.on { color: var(--yellow); border: 2px solid var(--yellow); }
    .th-chalk .ts-seg i { font-size: 17px; font-weight: 400; border-width: 1.5px; opacity: .7; }
    .th-chalk .ts-seg .on { opacity: 1; background: var(--chalk); color: var(--board); }
    .th-chalk .ts-label { font-size: 13px; letter-spacing: .06em; opacity: .75; }
    .th-chalk .ts-status { font-size: 16px; }
    .th-chalk .ts-row { grid-template-columns: repeat(5, 45px); }
    .th-chalk .ts-tile { height: 45px; font-size: 29px; border-color: rgb(244 241 232 / .5); }
    .th-chalk .ts-row:nth-child(even) .ts-tile { border-radius: 15px 225px 15px 255px / 255px 15px 225px 15px; }
    .th-chalk .ts-tile.typed { border-color: var(--chalk); }
    .th-chalk .ts-tile.correct { background: var(--chalk); border-color: var(--chalk); color: var(--board); }
    .th-chalk .ts-tile.present { border: 2px dashed var(--yellow); border-radius: 50%; color: var(--yellow); }
    .th-chalk .ts-tile.absent {
      border-color: rgb(244 241 232 / .22); color: rgb(244 241 232 / .4);
      background: linear-gradient(to top right, transparent 47%, rgb(244 241 232 / .45) 47% 53%, transparent 53%);
    }
    .th-chalk .ts-key { height: 37px; border-width: 1.5px; font-size: 17px; font-weight: 400; }
    .th-chalk .ts-key.wide { font-size: 11px; }
    .th-chalk .ts-key.correct { background: var(--chalk); color: var(--board); }
    .th-chalk .ts-key.present { border: 1.5px dashed var(--yellow); border-radius: 50%; color: var(--yellow); }
    .th-chalk .ts-key.absent { opacity: .3; text-decoration: line-through; }
  `,
})
export class ChalkTalkTheme {}
