import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <header class="bar" id="site-header">
      <a class="brand" routerLink="/">
        <span class="mark" aria-hidden="true">★</span>
        <span>KNOW BALL</span>
      </a>
      <nav aria-label="Main">
        <a routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">Games</a>
      </nav>
    </header>
    <main>
      <router-outlet />
    </main>
    <footer class="footer">
      <span>Built by <a href="https://www.nathanzimmerman.com/">Nathan Zimmerman</a></span>
      <nav aria-label="Project">
        <a href="https://github.com/natezimm/know-ball">GitHub</a>
        <a routerLink="/mockups">Design Lab</a>
        <a href="/preview/numbers-dont-lie/index.html?ngsw-bypass=true">Numbers Don't Lie preview</a>
      </nav>
    </footer>
  `,
  styles: `
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
    .footer nav a { color: var(--muted); font-size: 12px; white-space: normal; }
    .footer a:hover { color: var(--navy); }
    @media (max-width: 480px) {
      .bar { padding: 10px 14px; gap: 8px; }
      .brand { font-size: 16px; gap: 7px; }
      nav { gap: 0; }
      nav a { padding: 6px 7px; font-size: 13px; letter-spacing: .05em; }
    }
    @media (max-width: 380px) {
      .brand { font-size: 15px; }
      nav a { padding: 6px 5px; font-size: 12px; letter-spacing: .03em; }
    }
  `,
})
export class App {}
