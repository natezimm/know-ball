import { Component, ViewEncapsulation } from '@angular/core';
import { ThemeScreen } from '../theme-screen';

/** A 16-bit arcade cabinet: pixel type, neon on purple, bevelled blocks and scanlines. */
@Component({
  selector: 'app-theme-arcade',
  imports: [ThemeScreen],
  encapsulation: ViewEncapsulation.None,
  host: { class: 'th-host th-arcade' },
  template: `<app-theme-screen />`,
  styles: `
    .th-arcade .ts {
      --pink: #ff2bd6; --cyan: #00e5ff; --gold: #ffe600; --night: #14012e;
      background: linear-gradient(var(--night), #2c0a52); color: #fff;
      font-family: "Press Start 2P", ui-monospace, monospace; text-transform: uppercase;
    }
    .th-arcade .ts::after {
      content: ""; position: absolute; inset: 0; z-index: 2; pointer-events: none;
      background: repeating-linear-gradient(0deg, rgb(0 0 0 / .22) 0 1px, transparent 1px 3px);
    }
    .th-arcade .ts-bar { border-bottom: 3px solid var(--pink); padding: 12px 12px; }
    .th-arcade .ts-main { padding: 12px 12px 14px; }
    .th-arcade .ts-brand { font-size: 11px; font-weight: 400; color: var(--gold); text-shadow: 2px 2px 0 var(--pink); }
    .th-arcade .ts-mark { width: 10px; height: 10px; background: var(--cyan); box-shadow: 3px 3px 0 var(--pink); }
    .th-arcade .ts-nav { font-size: 7px; gap: 0; }
    .th-arcade .ts-nav span { color: var(--cyan); padding: 4px 5px; }
    .th-arcade .ts-nav .on { color: #fff; }
    .th-arcade .ts-nav .on::before { content: "▶"; margin-right: 3px; color: var(--gold); }
    .th-arcade .ts-title { font-size: 21px; font-weight: 400; color: var(--gold); text-shadow: 3px 3px 0 var(--pink); }
    .th-arcade .ts-btn, .th-arcade .ts-chip, .th-arcade .ts-key {
      background: #2b1458; border: 2px solid; border-color: #8f6bff #1a0838 #1a0838 #8f6bff;
    }
    .th-arcade .ts-btn, .th-arcade .ts-chip { font-size: 7px; padding: 7px 6px; }
    .th-arcade .ts-chip.on { background: var(--gold); color: var(--night); border-color: #fff8a8 #a89600 #a89600 #fff8a8; }
    .th-arcade .ts-seg i { width: 26px; font-size: 9px; font-weight: 400; border: 2px solid var(--cyan); color: var(--cyan); }
    .th-arcade .ts-seg .on { background: var(--cyan); color: var(--night); }
    .th-arcade .ts-label { font-size: 7px; letter-spacing: 0; color: var(--pink); }
    .th-arcade .ts-status { font-size: 8px; color: var(--cyan); }
    .th-arcade .ts-tile { font-size: 20px; font-weight: 400; padding: 2px 0 0 2px; background: #1c0540; border: 3px solid; border-color: #5b2a9d #12032a #12032a #5b2a9d; }
    .th-arcade .ts-tile.typed { border-color: var(--cyan); }
    .th-arcade .ts-tile.correct { background: #39ff14; color: #062b00; border-color: #c9ffbf #128a00 #128a00 #c9ffbf; }
    .th-arcade .ts-tile.present { background: #ff9d00; color: #3a1e00; border-color: #ffd699 #a85f00 #a85f00 #ffd699; }
    .th-arcade .ts-tile.absent { background: #2a1a44; color: #7d6aa6; border-color: #3c2a5e #150a26 #150a26 #3c2a5e; }
    .th-arcade .ts-key { font-size: 9px; font-weight: 400; padding-left: 1px; }
    .th-arcade .ts-key.wide { font-size: 5px; }
    .th-arcade .ts-key.correct { background: #39ff14; color: #062b00; border-color: #c9ffbf #128a00 #128a00 #c9ffbf; }
    .th-arcade .ts-key.present { background: #ff9d00; color: #3a1e00; border-color: #ffd699 #a85f00 #a85f00 #ffd699; }
    .th-arcade .ts-key.absent { background: #1d1033; color: #6a5a8f; border-color: #33224f #120822 #120822 #33224f; }
  `,
})
export class ArcadeTheme {}
