import { DOCUMENT } from '@angular/common';
import { Component, inject } from '@angular/core';
import { MockOption } from '../option';
import { ArcadeTheme } from './skins/arcade';
import { BackPageTheme } from './skins/back-page';
import { BroadcastTheme } from './skins/broadcast';
import { ChalkTalkTheme } from './skins/chalk-talk';
import { NightCourtTheme } from './skins/night-court';
import { PaperGridTheme } from './skins/paper-grid';
import { ScoreboardTheme } from './skins/scoreboard';
import { StickerPopTheme } from './skins/sticker-pop';
import { TradingCardTheme } from './skins/trading-card';

// Google Fonts families the skins use. One stylesheet each, so a family that fails to load
// costs only its own theme. The real app would bundle whichever fonts the chosen theme needs.
const FONTS = [
  'Baloo+2:wght@700;800',
  'DM+Sans:wght@400;600;700',
  'Space+Grotesk:wght@500;600;700',
  'IBM+Plex+Mono:wght@400;500',
  'Doto:wght@900',
  'Share+Tech+Mono',
  'Cabin+Sketch:wght@700',
  'Patrick+Hand',
  'UnifrakturCook:wght@700',
  'Playfair+Display:ital,wght@0,800;1,900',
  'Libre+Baskerville:ital,wght@0,400;0,700;1,400',
  'Press+Start+2P',
  'Barlow+Condensed:ital,wght@1,600;1,700;1,800',
  'Barlow:wght@500;600',
  'Alfa+Slab+One',
  'Rokkitt:ital,wght@0,500;0,700;1,500',
  'Yellowtail',
];

/** One game, nine looks: the same Spordle screen in every candidate theme. */
@Component({
  selector: 'app-themes-mock',
  imports: [
    MockOption,
    NightCourtTheme,
    StickerPopTheme,
    PaperGridTheme,
    ScoreboardTheme,
    ChalkTalkTheme,
    BackPageTheme,
    ArcadeTheme,
    BroadcastTheme,
    TradingCardTheme,
  ],
  template: `
    <section class="mk-game">
      <h2>Themes <span class="mk-chip">Whole site</span></h2>
      <p class="mk-sub">
        The same Spordle screen, three guesses into a game, in nine different looks. Trading Card (I) was chosen
        and is now the app's theme; A is the look it replaced. B and C follow boringcashcow.com and your sudoku.
        The preview pulls its fonts from Google Fonts.
      </p>

      <div class="mk-row">
        <app-mock-option
          game="themes"
          letter="A"
          name="Night Court (previous)"
          bare
          plus="Easy on the eyes at night, and the amber accent carries across every game."
          minus="The safest and least distinctive: many sports sites are dark navy with one bright colour."
        >
          <app-theme-night-court />
        </app-mock-option>

        <app-mock-option
          game="themes"
          letter="B"
          name="Sticker Pop"
          bare
          plus="Friendly and instantly recognisable; outlined white cards will frame player photos and logos well."
          minus="Playful to the point of cute, and a bright teal page is tiring for long sessions; no natural dark mode."
        >
          <app-theme-sticker-pop />
        </app-mock-option>

        <app-mock-option
          game="themes"
          letter="C"
          name="Paper Grid"
          bare
          plus="Matches your sudoku, so the sites read as a family; crisp, fast and has a ready-made dark mode."
          minus="Stark: black rules and flat colour can feel more like a tool than a sports site."
        >
          <app-theme-paper-grid />
        </app-mock-option>

        <app-mock-option
          game="themes"
          letter="D"
          name="Scoreboard"
          bare
          plus="Unmistakably sport, and a natural fit for stats, timers and streak counters."
          minus="Results show as lit letters, not filled tiles, so they are harder to scan; dot-matrix type is poor for long text."
        >
          <app-theme-scoreboard />
        </app-mock-option>

        <app-mock-option
          game="themes"
          letter="E"
          name="Chalk Talk"
          bare
          plus="The most personality: circles, strike-throughs and hand lettering read like a coach's play diagram."
          minus="Hand-drawn shapes need care on every new screen, and hand lettering is slower to read in tables."
        >
          <app-theme-chalk-talk />
        </app-mock-option>

        <app-mock-option
          game="themes"
          letter="F"
          name="Back Page"
          bare
          plus="Daily puzzles feel at home on a newspaper page; highlighter and red pen make results clear without relying on colour alone."
          minus="Leans puzzle more than sport, and a newsprint page is bright; photos would need a halftone treatment to belong."
        >
          <app-theme-back-page />
        </app-mock-option>

        <app-mock-option
          game="themes"
          letter="G"
          name="Arcade"
          bare
          plus="Loud and fun, and wins, streaks and high scores have an obvious visual language."
          minus="Pixel type is wide and tiring, so labels must stay tiny or short; the novelty may wear off."
        >
          <app-theme-arcade />
        </app-mock-option>

        <app-mock-option
          game="themes"
          letter="H"
          name="Broadcast"
          bare
          plus="Looks like a live game on TV; scorebugs and lower-thirds suit player names, stats and the GOAT ranking."
          minus="Slanted shapes cost a little legibility and need tuning at each size; close to what the leagues' own apps do."
        >
          <app-theme-broadcast />
        </app-mock-option>

        <app-mock-option
          game="themes"
          letter="I"
          name="Trading Card (chosen)"
          bare
          plus="Warm and nostalgic; Scouting Report, Sportle and the silhouette game could each be a card."
          minus="Heavy borders eat screen width on phones, and the retro look skews toward baseball."
        >
          <app-theme-trading-card />
        </app-mock-option>
      </div>
    </section>
  `,
})
export class ThemesMock {
  constructor() {
    const document = inject(DOCUMENT);
    for (const family of FONTS) {
      const id = `mock-font-${family.split(':')[0]}`;
      if (document.getElementById(id)) continue;
      const link = document.createElement('link');
      link.id = id;
      link.rel = 'stylesheet';
      link.href = `https://fonts.googleapis.com/css2?family=${family}&display=swap`;
      document.head.appendChild(link);
    }
  }
}
