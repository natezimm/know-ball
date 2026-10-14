import { Component, ViewEncapsulation } from '@angular/core';
import { ThemeScreen } from '../theme-screen';

/** Teal, rounded and outlined with hard little shadows, after boringcashcow.com. */
@Component({
  selector: 'app-theme-sticker-pop',
  imports: [ThemeScreen],
  encapsulation: ViewEncapsulation.None,
  host: { class: 'th-host th-pop' },
  template: `<app-theme-screen />`,
  styles: `
    .th-pop .ts { --ink: #221e26; background: #43c1b3; color: var(--ink); font-family: "DM Sans", system-ui, sans-serif; }
    .th-pop .ts::before, .th-pop .ts::after { content: ""; position: absolute; border: 2px solid var(--ink); }
    .th-pop .ts::before { width: 28px; height: 9px; border-radius: 9px; background: #fff; top: 268px; right: -9px; transform: rotate(-28deg); }
    .th-pop .ts::after { width: 12px; height: 12px; border-radius: 50%; background: #ffd43c; bottom: 176px; left: 5px; }
    .th-pop .ts-bar {
      margin: 10px 10px 0; padding: 6px 12px; background: #fff; border: 2px solid var(--ink);
      border-radius: 999px; box-shadow: 3px 3px 0 var(--ink);
    }
    .th-pop .ts-brand, .th-pop .ts-title, .th-pop .ts-tile, .th-pop .ts-seg i {
      font-family: "Baloo 2", "DM Sans", sans-serif; font-weight: 800;
    }
    .th-pop .ts-brand { font-size: 18px; text-transform: lowercase; letter-spacing: -.01em; }
    .th-pop .ts-mark { width: 20px; height: 20px; border-radius: 50%; background: #ffd43c; border: 2px solid var(--ink); box-sizing: border-box; }
    .th-pop .ts-nav span { font-weight: 600; border-radius: 999px; }
    .th-pop .ts-nav .on { background: #ffd43c; border: 2px solid var(--ink); padding: 2px 9px; font-weight: 700; }
    .th-pop .ts-title { font-size: 34px; }
    .th-pop .ts-btn {
      background: #fff; border: 2px solid var(--ink); border-radius: 999px; box-shadow: 2px 2px 0 var(--ink);
      font-weight: 700; padding: 5px 11px;
    }
    .th-pop .ts-btn:first-child { background: #ffd43c; }
    .th-pop .ts-seg i { background: #fff; border: 2px solid var(--ink); border-radius: 10px; font-size: 15px; padding-top: 2px; }
    .th-pop .ts-seg .on { background: #ffd43c; box-shadow: 2px 2px 0 var(--ink); }
    .th-pop .ts-label { font-weight: 700; color: #13524b; }
    .th-pop .ts-status { font-weight: 700; }
    .th-pop .ts-chip { background: #ffd9e1; border: 1.5px solid var(--ink); border-radius: 999px; font-weight: 600; padding: 4px 10px; }
    .th-pop .ts-chip.on { background: #fff3b0; }
    .th-pop .ts-chip.on::before { content: "●"; color: #ff5d73; margin-right: 5px; font-size: 9px; }
    .th-pop .ts-tile {
      background: #fff; border: 2px solid var(--ink); border-radius: 12px; box-shadow: 2px 2px 0 var(--ink);
      font-size: 27px; padding-top: 4px;
    }
    .th-pop .ts-tile.typed { background: #e3faf6; }
    .th-pop .ts-tile.correct { background: #5fd38d; }
    .th-pop .ts-tile.present { background: #ffd43c; }
    .th-pop .ts-tile.absent { background: #cfc9d6; box-shadow: none; color: #6a6472; }
    .th-pop .ts-key { background: #fff; border: 2px solid var(--ink); border-radius: 9px; box-shadow: 0 2px 0 var(--ink); }
    .th-pop .ts-key.correct { background: #5fd38d; }
    .th-pop .ts-key.present { background: #ffd43c; }
    .th-pop .ts-key.absent { background: #cfc9d6; box-shadow: none; color: #6a6472; }
  `,
})
export class StickerPopTheme {}
