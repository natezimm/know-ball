import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { GameApi, GameSlug, TodayEntry } from '../../core/game-api';

interface GameCard {
  name: string;
  blurb: string;
  glyph: string;
  /** The colour of this game's card border. One theme for the site, one trim per game. */
  trim: string;
  link: string;
  /** Set for the daily games; Spordle is played whenever you like. */
  daily?: GameSlug;
}

/** What a card says under its blurb, and whether it can be opened. */
export function cardStatus(card: Pick<GameCard, 'daily'>, today: TodayEntry | undefined, loaded: boolean | null): { label: string; open: boolean; done?: boolean } {
  if (!card.daily) return { label: 'Play anytime', open: true };
  if (loaded === null) return { label: 'Checking today…', open: false };
  if (!today) return loaded ? { label: 'Coming soon', open: false } : { label: 'Play today', open: true };
  if (!today.available) return { label: 'No puzzle today', open: false };

  switch (today.status) {
    case 'won':
      return { label: today.guesses === 1 ? 'Solved in 1 guess' : `Solved in ${today.guesses}`, open: true, done: true };
    case 'lost':
      return { label: 'Missed · see the answer', open: true, done: true };
    case 'playing':
      return { label: `Carry on · ${today.guesses} of ${today.maxGuesses}`, open: true };
    default:
      return { label: `Play No. ${today.number}`, open: true };
  }
}

const COUNT_WORDS = ['No', 'One', 'Two', 'Three', 'Four'];

@Component({
  selector: 'app-home',
  imports: [RouterLink],
  template: `
    <section class="hero">
      <p class="kicker">Daily sports trivia</p>
      <h1>{{ countWord() }} ways to prove you <span class="script">know ball.</span></h1>
      <p class="lede">A new puzzle in every daily game at midnight Eastern, and Spordle whenever you want it.</p>
    </section>

    <ul class="grid" aria-label="Games">
      @for (card of cards(); track card.name; let i = $index) {
        <li class="card" [style.--trim]="card.trim" [class.off]="!card.status.open">
          <div class="top">
            <span class="no">No. {{ i + 1 }}</span>
            <span class="glyph" aria-hidden="true">{{ card.glyph }}</span>
          </div>
          <h2>{{ card.name }}</h2>
          <p>{{ card.blurb }}</p>
          @if (card.status.open) {
            <a class="badge play" [class.done]="card.status.done" [routerLink]="card.link" [attr.aria-label]="card.name + ': ' + card.status.label">
              {{ card.status.label }}
            </a>
          } @else {
            <span class="badge">{{ card.status.label }}</span>
          }
        </li>
      }
    </ul>

    <p class="later"><span>On deck</span> {{ later.join(' · ') }}</p>
  `,
  styles: `
    .hero { padding: 12px 0 34px; max-width: 720px; }
    .kicker { margin: 0 0 10px; color: var(--accent); font-size: 15px; font-weight: 700; text-transform: uppercase; letter-spacing: .18em; }
    h1 { margin: 0; font: 400 clamp(32px, 6vw, 54px)/1.1 var(--display); }
    .script {
      display: inline-block; font: 400 1.3em/1 var(--script); color: var(--red); white-space: nowrap;
      text-shadow: 1.5px 1.5px 0 var(--cream), 3px 3px 0 var(--navy); transform: rotate(-3deg);
    }
    .lede { color: var(--muted); font-size: 20px; margin: 18px 0 0; }

    .grid {
      list-style: none; margin: 0; padding: 0;
      display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 18px;
    }
    /* Each game is a card from the same set: trim colour outside, then a cream gap and a navy line. */
    .card {
      position: relative; min-height: 220px; padding: 22px 20px 20px; display: flex; flex-direction: column; gap: 6px;
      background: var(--surface); border: 6px solid var(--trim); border-radius: 8px;
      box-shadow: inset 0 0 0 2px var(--cream), inset 0 0 0 4px var(--navy);
      transition: transform .12s ease;
    }
    .card.off { opacity: .75; }
    .top { display: flex; align-items: center; justify-content: space-between; }
    .no { font-size: 14px; font-weight: 700; text-transform: uppercase; letter-spacing: .12em; color: var(--muted); }
    .glyph {
      width: 34px; height: 34px; border-radius: 50%; display: grid; place-items: center;
      background: var(--navy); color: var(--cream); font-size: 17px; line-height: 1;
    }
    h2 { margin: 8px 0 0; font: 400 21px/1.15 var(--display); }
    p { margin: 0; flex: 1; color: var(--muted); font-size: 17px; line-height: 1.3; }
    .badge {
      align-self: flex-start; margin-top: 10px; padding: 3px 12px; border: 2px solid var(--navy); border-radius: 999px;
      font-size: 13px; font-weight: 700; text-transform: uppercase; letter-spacing: .06em;
    }
    .badge.play { background: var(--accent); color: var(--accent-ink); text-decoration: none; }
    .badge.done { background: var(--ok); color: var(--ok-ink); }
    /* Stretch the link over the whole card so the card itself is the tap target. */
    .badge.play::after { content: ""; position: absolute; inset: 0; border-radius: 4px; }
    .card:has(.play):hover { transform: translateY(-3px) rotate(-.6deg); }

    .later { margin: 36px 0 0; color: var(--muted); font-size: 17px; }
    .later span { margin-right: 8px; color: var(--text); font-size: 14px; font-weight: 700; text-transform: uppercase; letter-spacing: .12em; }
    @media (prefers-reduced-motion: reduce) {
      .card { transition: none; }
      .card:has(.play):hover { transform: none; }
    }
  `,
})
export class Home {
  /** Version 1: these four. */
  private readonly games: GameCard[] = [
    { name: 'Spordle', link: '/spordle', glyph: '▦', trim: 'var(--red)', blurb: 'Six guesses to find a sports word of 4, 5 or 6 letters.' },
    {
      name: 'Scouting Report', link: '/scouting-report', daily: 'scouting-report', glyph: '◈', trim: 'var(--teal)',
      blurb: 'Guess the player in 8 tries with team, position, height, age and number clues.',
    },
    {
      name: 'Career Path', link: '/career-path', daily: 'career-path', glyph: '→', trim: 'var(--mustard)',
      blurb: 'Every team they played for, in order. Who is it?',
    },
    {
      name: "Who's That Sportsman?", link: '/whos-that', daily: 'whos-that', glyph: '◐', trim: 'var(--navy)',
      blurb: 'Name the player from the outline of an iconic photo.',
    },
  ];

  /** Designed but not scheduled; replay numbers after version 1 decide which comes first. */
  protected readonly later = ['Stat Quiz', 'Geo Sports', 'Sportle', 'Sound It Out', "Who's the GOAT?"];

  // Undefined while checking availability; null after a failed request.
  private readonly today = signal<TodayEntry[] | null | undefined>(undefined);

  protected readonly cards = computed(() => {
    const today = this.today();
    return this.games.map((card) => ({ ...card, status: cardStatus(card, today?.find((t) => t.game === card.daily), today === undefined ? null : today !== null) }));
  });
  protected readonly countWord = computed(() => this.today() === undefined ? 'Four' : COUNT_WORDS[this.cards().filter((c) => c.status.open).length] ?? 'Four');

  constructor() {
    // If the API is down the cards still link to the games, which report the problem themselves.
    inject(GameApi).today().subscribe({ next: (entries) => this.today.set(entries), error: () => this.today.set(null) });
  }
}

