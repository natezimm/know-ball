import { Component, ViewEncapsulation, ChangeDetectionStrategy } from '@angular/core';
import { ThemeScreen } from '../theme-screen';

/** Live TV graphics: slanted panels, condensed italic type, a red chyron and a white scorebug. */
@Component({
  selector: 'app-theme-broadcast',
  imports: [ThemeScreen],
  encapsulation: ViewEncapsulation.None,
  host: { class: 'th-host th-tv' },
  template: `<app-theme-screen />`,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .th-tv .ts {
      --navy: #0a1230; --red: #e4002b; --gold: #ffd400;
      background: linear-gradient(160deg, var(--navy), #18246a 55%, var(--navy)); color: #fff;
      font-family: "Barlow", system-ui, sans-serif;
    }
    .th-tv .ts::before { content: ""; position: absolute; inset: -20% 52% -20% -30%; background: rgb(255 255 255 / .045); transform: skewX(-18deg); }
    .th-tv .ts-bar { background: linear-gradient(105deg, var(--red) 0 60%, transparent 60%); padding: 9px 14px; }
    .th-tv .ts-brand, .th-tv .ts-title, .th-tv .ts-tile, .th-tv .ts-btn, .th-tv .ts-seg i,
    .th-tv .ts-key, .th-tv .ts-status, .th-tv .ts-chip {
      font-family: "Barlow Condensed", "Arial Narrow", sans-serif; font-style: italic; text-transform: uppercase;
    }
    .th-tv .ts-brand { font-size: 21px; font-weight: 800; letter-spacing: .02em; }
    .th-tv .ts-mark { width: 8px; height: 18px; background: #fff; transform: skewX(-14deg); }
    .th-tv .ts-nav span { font-weight: 600; font-size: 10px; letter-spacing: .08em; text-transform: uppercase; color: #aab4e8; padding: 2px 6px; }
    .th-tv .ts-nav .on { color: #fff; border-bottom: 2px solid var(--red); }
    .th-tv .ts-title {
      font-size: 34px; font-weight: 800; background: #fff; color: var(--navy); padding: 1px 14px 1px 10px;
      margin-left: 5px; border-left: 6px solid var(--red); transform: skewX(-10deg);
    }
    .th-tv .ts-btn, .th-tv .ts-chip { font-size: 15px; font-weight: 700; padding: 2px 10px; background: rgb(255 255 255 / .12); transform: skewX(-10deg); }
    .th-tv .ts-btn { border-left: 3px solid var(--red); }
    .th-tv .ts-chip.on { background: var(--gold); color: var(--navy); }
    .th-tv .ts-seg i { font-size: 17px; background: rgb(255 255 255 / .12); transform: skewX(-10deg); }
    .th-tv .ts-seg .on { background: var(--red); }
    .th-tv .ts-label { color: #aab4e8; font-weight: 600; margin-right: 6px; }
    .th-tv .ts-status { font-size: 16px; font-weight: 700; background: #fff; color: var(--navy); padding: 1px 10px; margin-right: 6px; box-shadow: 5px 0 0 var(--red); transform: skewX(-10deg); }
    .th-tv .ts-row { gap: 6px; }
    .th-tv .ts-tile { font-size: 33px; font-weight: 800; background: rgb(255 255 255 / .09); border-bottom: 3px solid rgb(255 255 255 / .22); transform: skewX(-8deg); }
    .th-tv .ts-tile.typed { background: #fff; color: var(--navy); border-color: var(--red); }
    .th-tv .ts-tile.correct { background: #12b76a; border-color: #0a7a45; }
    .th-tv .ts-tile.present { background: var(--gold); color: var(--navy); border-color: #b39500; }
    .th-tv .ts-tile.absent { background: #1d2757; color: #7f8ac4; border-color: #141c40; }
    .th-tv .ts-key { font-size: 18px; font-weight: 700; background: #1f2b6b; border-bottom: 2px solid #121a45; transform: skewX(-8deg); }
    .th-tv .ts-key.wide { font-size: 11px; }
    .th-tv .ts-key.correct { background: #12b76a; border-color: #0a7a45; }
    .th-tv .ts-key.present { background: var(--gold); color: var(--navy); border-color: #b39500; }
    .th-tv .ts-key.absent { background: #131a40; color: #5d68a3; }
  `,
})
export class BroadcastTheme {}
