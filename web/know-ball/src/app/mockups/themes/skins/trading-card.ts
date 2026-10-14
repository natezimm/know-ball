import { Component, ViewEncapsulation } from '@angular/core';
import { ThemeScreen } from '../theme-screen';

/** A 1950s trading card: cream stock, red and navy borders, slab type and a jersey-script wordmark. */
@Component({
  selector: 'app-theme-trading-card',
  imports: [ThemeScreen],
  encapsulation: ViewEncapsulation.None,
  host: { class: 'th-host th-card' },
  template: `<app-theme-screen />`,
  styles: `
    .th-card .ts {
      --navy: #1d2b53; --red: #b92d22; --cream: #f6ecd2; --stock: #fbf5e2; --mustard: #e0a526;
      background: var(--cream) radial-gradient(rgb(29 43 83 / .08) 1px, transparent 1.4px) 0 0 / 5px 5px;
      color: var(--navy); font-family: "Rokkitt", Georgia, serif;
      box-shadow: inset 0 0 0 7px var(--red), inset 0 0 0 9px var(--cream), inset 0 0 0 11px var(--navy);
    }
    .th-card .ts-bar { margin: 11px 11px 0; padding: 7px 12px; background: var(--navy); color: var(--cream); }
    .th-card .ts-main { padding: 10px 24px 24px; gap: 9px; }
    .th-card .ts-brand { font: 400 14px/1 "Alfa Slab One", Georgia, serif; letter-spacing: .06em; text-transform: uppercase; }
    .th-card .ts-mark { width: auto; height: auto; }
    .th-card .ts-mark::before { content: "★"; color: var(--mustard); font-style: normal; font-size: 14px; }
    .th-card .ts-nav span { font-weight: 700; font-size: 11px; text-transform: uppercase; letter-spacing: .08em; opacity: .7; padding: 2px 6px; }
    .th-card .ts-nav .on { opacity: 1; color: var(--mustard); }
    .th-card .ts-title {
      font: 400 44px/1 "Yellowtail", "Brush Script MT", cursive; color: var(--red);
      text-shadow: 1.5px 1.5px 0 var(--cream), 3px 3px 0 var(--navy); transform: rotate(-5deg); margin: -4px 0 0 2px;
    }
    .th-card .ts-btn { border: 2px solid var(--navy); border-radius: 999px; background: var(--stock); font-weight: 700; font-size: 11px; text-transform: uppercase; letter-spacing: .05em; padding: 4px 9px; }
    .th-card .ts-btn:first-child { background: var(--red); color: var(--cream); }
    .th-card .ts-seg i { width: 28px; height: 28px; border: 2px solid var(--navy); border-radius: 50%; background: var(--stock); font-size: 15px; }
    .th-card .ts-seg .on { background: var(--navy); color: var(--cream); }
    .th-card .ts-label { font-weight: 700; font-size: 11px; }
    .th-card .ts-status { font-style: italic; font-weight: 500; font-size: 15px; }
    .th-card .ts-chip { border: 2px dashed var(--navy); border-radius: 3px; font-weight: 700; font-size: 13px; padding: 1px 9px; }
    .th-card .ts-chip.on { border-style: solid; background: var(--mustard); }
    .th-card .ts-row { grid-template-columns: repeat(5, 44px); }
    .th-card .ts-tile { height: 44px; background: var(--stock); border: 2px solid var(--navy); border-radius: 4px; font: 400 23px/1 "Alfa Slab One", Georgia, serif; }
    .th-card .ts-tile.typed { border-color: var(--red); }
    .th-card .ts-tile.correct { background: #1f6f6b; color: var(--cream); }
    .th-card .ts-tile.present { background: var(--mustard); }
    .th-card .ts-tile.absent { background: #d9cba6; color: #6b5f3e; border-color: #9a8c66; }
    .th-card .ts-key { height: 36px; background: var(--stock); border: 2px solid var(--navy); border-radius: 4px; font-size: 16px; }
    .th-card .ts-key.wide { font-size: 9px; }
    .th-card .ts-key.correct { background: #1f6f6b; color: var(--cream); }
    .th-card .ts-key.present { background: var(--mustard); }
    .th-card .ts-key.absent { background: #d9cba6; color: #6b5f3e; border-color: #9a8c66; }
  `,
})
export class TradingCardTheme {}
