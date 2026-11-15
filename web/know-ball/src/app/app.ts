import { Component, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <a class="skip" href="#main" (click)="skip($event, main)">Skip to content</a>
    <header class="bar" id="site-header">
      <a class="brand" routerLink="/">
        <span class="mark" aria-hidden="true">★</span>
        <span>KNOW BALL</span>
      </a>
      <nav aria-label="Main">
        <a routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">Games</a>
      </nav>
    </header>
    <main #main id="main" tabindex="-1">
      <router-outlet />
    </main>
    <footer class="footer">
      <span>Built by <a href="https://www.nathanzimmerman.com/">Nathan Zimmerman</a></span>
      <nav aria-label="Project">
        <a href="https://github.com/natezimm/know-ball">GitHub</a>
      </nav>
      <p class="disclaimer">Not affiliated with or endorsed by the NBA, NFL, MLB or NHL.</p>
    </footer>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    /* The first stop for the Tab key, off screen until it has focus. */
    .skip {
      position: absolute; left: 8px; top: -60px; z-index: 30; padding: 8px 14px; border: 2px solid var(--navy); border-radius: 4px;
      background: var(--cream); color: var(--navy); font-weight: 700; text-decoration: none;
    }
    .skip:focus { top: 8px; }
    /* A navy banner with the card's red, cream and navy border running along its bottom edge. */
    .bar {
      display: flex; align-items: center; justify-content: space-between; gap: 16px;
      padding: 12px 20px; position: sticky; top: 0; z-index: 10;
      background: var(--navy); color: var(--cream);
      border-bottom: 5px solid var(--red); box-shadow: 0 2px 0 var(--cream), 0 4px 0 var(--navy);
    }
    .brand {
      display: flex; align-items: center; gap: 9px; text-decoration: none; white-space: nowrap;
      font-family: var(--display); font-weight: 400; font-size: 19px; letter-spacing: .06em;
    }
    .mark { color: var(--mustard); font-size: 18px; line-height: 1; }
    nav { display: flex; gap: 2px; }
    nav a {
      text-decoration: none; padding: 6px 10px; white-space: nowrap; color: rgb(246 236 210 / .72);
      font-size: 15px; font-weight: 700; text-transform: uppercase; letter-spacing: .08em;
    }
    nav a:hover { color: var(--cream); }
    nav a.active { color: var(--mustard); }
    main { max-width: 1040px; margin: 0 auto; padding: 36px 20px 64px; }
    .footer {
      max-width: 1040px; margin: 0 auto; padding: 18px 20px calc(20px + env(safe-area-inset-bottom, 0px));
      display: flex; flex-wrap: wrap; justify-content: space-between; align-items: center; gap: 8px 20px;
      border-top: 1px solid var(--tan); color: var(--muted); font-size: 15px;
    }
    .footer > span a { text-underline-offset: 3px; }
    .footer nav { flex-wrap: wrap; }
    .footer nav a { color: var(--muted); font-size: 13px; white-space: normal; }
    .footer a:hover { color: var(--navy); }
    .disclaimer { flex-basis: 100%; margin: 0; font-size: 13px; }
    @media (max-width: 480px) {
      .bar { padding: 10px 14px; gap: 8px; }
      .brand { font-size: 16px; gap: 7px; }
      nav { gap: 0; }
      nav a { padding: 6px 7px; font-size: 13px; letter-spacing: .05em; }
    }
    @media (max-width: 380px) {
      .brand { font-size: 15px; }
      nav a { padding: 6px 5px; letter-spacing: .03em; }
    }
  `,
})
export class App {
  /** Moves focus past the header. Done by hand: with the app's base address a "#main" link would load the home page. */
  protected skip(event: Event, main: HTMLElement): void {
    event.preventDefault();
    // On a game page the guess box is where the player was heading.
    const first = main.querySelector<HTMLElement>('input:not([disabled])');
    (first ?? main).focus();
  }
}
