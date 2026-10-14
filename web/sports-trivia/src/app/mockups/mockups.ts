import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MOCK_GAMES } from './mock-data';
import { MockupPicks } from './picks';

/**
 * Design-option gallery: one page per game, each with 2-4 phone-sized sketches to choose between.
 * The `mk-` styles here are global on purpose so every game page shares the same frame and primitives.
 */
@Component({
  selector: 'app-mockups',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="mk-page">
      <header>
        <p class="mk-kicker">Design mockups</p>
        <h1 class="mk-title">The games still on deck</h1>
        <p class="mk-sub">
          Static sketches with sample data, drawn at phone size, for the theme and the five games that come after
          version 1. The four games that are built have left this gallery. Star the options you like; picks are saved
          in this browser.
        </p>
      </header>

      <nav class="mk-tabs" aria-label="Games">
        @for (g of games; track g.path) {
          <a [routerLink]="g.path" routerLinkActive="active">
            {{ g.name }}
            @if (picks.chosen(g.path); as chosen) {
              <span class="mk-count">{{ chosen }}</span>
            }
          </a>
        }
      </nav>

      <router-outlet />

      <footer class="mk-picks">
        <strong>Your picks</strong>
        <span>{{ picks.summary() || 'None yet' }}</span>
        <button type="button" class="mk-btn ghost" [disabled]="!picks.summary()" (click)="copy()">
          {{ copied() ? 'Copied' : 'Copy' }}
        </button>
      </footer>
    </div>
  `,
  styles: `
    .mk-page {
      width: min(1480px, 100vw - 48px); margin-left: 50%; transform: translateX(-50%);
      box-sizing: border-box; padding: 24px; border-radius: 14px;
      /* The sketches were drawn on the old dark palette; keep it for them whatever the app's theme is. */
      --bg: #0f1720; --surface: #17212c; --surface-2: #1f2b38; --line: #2c3b4b; --text: #eef2f6; --muted: #93a3b5;
      --accent: #f5a524; --accent-ink: #1b1300; --radius: 12px;
      --font: "Inter", system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;
      --display: "Oswald", "Arial Narrow", "Inter", system-ui, sans-serif;
      --ok: #2ea043; --close: #b8860b; --miss: #33455a; --bad: #e5534b;
      background: var(--bg); color: var(--text); font-family: var(--font); font-size: 16px; font-weight: 400;
      color-scheme: dark;
    }
    .mk-kicker {
      margin: 0 0 6px; color: var(--accent); font-family: var(--display);
      text-transform: uppercase; letter-spacing: .18em; font-size: 12px;
    }
    .mk-title { margin: 0; font-family: var(--display); font-size: clamp(26px, 4vw, 38px); line-height: 1.1; }
    .mk-sub { color: var(--muted); margin: 8px 0 0; max-width: 72ch; }
    .mk-tabs { display: flex; flex-wrap: wrap; gap: 6px; margin: 22px 0 26px; }
    .mk-tabs a {
      text-decoration: none; color: var(--muted); padding: 6px 12px; border-radius: 999px;
      border: 1px solid var(--line); font-size: 14px;
    }
    .mk-tabs a:hover { color: var(--text); }
    .mk-tabs a.active { background: var(--accent); border-color: var(--accent); color: var(--accent-ink); font-weight: 600; }
    .mk-count { margin-left: 6px; font-size: 12px; font-weight: 700; }
    .mk-game h2 { margin: 0; font-family: var(--display); font-size: 26px; display: flex; align-items: center; gap: 10px; }
    .mk-row { display: flex; flex-wrap: wrap; gap: 32px 24px; margin-top: 22px; align-items: flex-start; }

    app-mock-option { display: flex; flex-direction: column; gap: 10px; width: 340px; max-width: 100%; }
    .mk-opt { display: flex; align-items: center; gap: 10px; }
    .mk-opt h3 { margin: 0; font-size: 16px; flex: 1; }
    .mk-letter {
      width: 26px; height: 26px; border-radius: 8px; display: grid; place-items: center;
      background: var(--surface-2); border: 1px solid var(--line); font-family: var(--display); font-weight: 700;
    }
    .mk-pick {
      border: 1px solid var(--line); background: none; color: var(--muted); border-radius: 999px;
      padding: 4px 12px; font: inherit; font-size: 13px; cursor: pointer;
    }
    .mk-pick.on { background: var(--accent); border-color: var(--accent); color: var(--accent-ink); font-weight: 700; }
    .mk-phone {
      height: 640px; display: flex; flex-direction: column; overflow: hidden; background: var(--bg);
      border: 1px solid var(--line); border-radius: 26px; box-shadow: 0 0 0 5px var(--surface);
    }
    .mk-appbar {
      display: flex; align-items: center; gap: 10px; padding: 10px 16px;
      border-bottom: 1px solid var(--line); font-size: 13px; color: var(--muted);
    }
    .mk-appbar b {
      flex: 1; text-align: center; color: var(--text); font-family: var(--display);
      letter-spacing: .1em; text-transform: uppercase; font-size: 14px;
    }
    .mk-body {
      flex: 1; min-height: 0; padding: 14px; display: flex; flex-direction: column; gap: 12px;
      position: relative; overflow: hidden;
    }
    .mk-body.flush { padding: 0; }
    .mk-notes { list-style: none; margin: 0; padding: 0; display: grid; gap: 4px; font-size: 13px; color: var(--muted); }
    .mk-notes li { padding-left: 18px; position: relative; }
    .mk-notes li::before { position: absolute; left: 2px; font-weight: 700; }
    .mk-notes .plus::before { content: "+"; color: var(--ok); }
    .mk-notes .minus::before { content: "−"; color: var(--bad); }

    .mk-input {
      display: flex; align-items: center; gap: 8px; padding: 10px 12px; border-radius: 10px;
      background: var(--surface); border: 1px solid var(--line); color: var(--muted); font-size: 14px;
    }
    .mk-input span { flex: 1; }
    .mk-btn {
      border: 0; border-radius: 10px; padding: 10px 14px; background: var(--accent); color: var(--accent-ink);
      font: inherit; font-size: 14px; font-weight: 700; text-align: center; cursor: pointer;
    }
    .mk-btn.ghost { background: none; border: 1px solid var(--line); color: var(--text); font-weight: 500; }
    .mk-btn:disabled { opacity: .5; cursor: default; }
    .mk-chip {
      display: inline-flex; align-items: center; gap: 6px; padding: 3px 10px; border-radius: 999px;
      border: 1px solid var(--line); font-family: var(--font); font-size: 12px; font-weight: 400; color: var(--muted);
    }
    .mk-chip.on { border-color: var(--accent); color: var(--accent); }
    .mk-card { background: var(--surface); border: 1px solid var(--line); border-radius: 12px; padding: 12px; }
    .mk-guess {
      display: flex; align-items: center; gap: 8px; padding: 7px 10px; border-radius: 8px;
      background: var(--surface); font-size: 13px;
    }
    .mk-guess::before { content: "✕"; color: var(--bad); font-weight: 700; }
    .mk-ok { background: var(--ok); color: #fff; }
    .mk-close { background: var(--close); color: #fff; }
    .mk-miss { background: var(--miss); color: var(--text); }
    .mk-label { font-size: 10px; letter-spacing: .08em; text-transform: uppercase; color: var(--muted); }
    .mk-small { margin: 0; font-size: 12px; color: var(--muted); }
    .mk-ask { margin: 0; font-family: var(--display); font-size: 20px; line-height: 1.2; }
    .mk-meta { display: flex; justify-content: space-between; align-items: center; font-size: 13px; color: var(--muted); }
    .mk-pair { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
    .mk-spacer { flex: 1; }

    .mk-picks {
      position: sticky; bottom: 12px; margin-top: 36px; padding: 10px 14px; display: flex; align-items: center;
      gap: 12px; font-size: 14px; background: var(--surface-2); border: 1px solid var(--line); border-radius: 12px;
    }
    .mk-picks span { flex: 1; color: var(--muted); }
    .mk-picks .mk-btn { padding: 6px 12px; }
  `,
})
export class Mockups {
  protected readonly games = MOCK_GAMES;
  protected readonly picks = inject(MockupPicks);
  protected readonly copied = signal(false);

  protected async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.picks.summary());
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    } catch {
      // Clipboard blocked: the picks are on screen to copy by hand.
    }
  }
}
