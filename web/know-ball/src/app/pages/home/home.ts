import { Component, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink } from '@angular/router';
import { GameApi, GameSlug, GameStats, TodayEntry } from '../../core/game-api';
import { Tilt } from '../../shared/tilt/tilt';

interface GameCard {
  name: string;
  blurb: string;
  /** What kind of game it is, in a word or two, above its name. */
  kind: string;
  /** Which badge sits in the navy circle; drawn as inline SVG so it is centred whatever the font. */
  icon: 'tile' | 'clipboard' | 'jersey' | 'card' | 'chart';
  /** The play label for a game without a tracked daily puzzle. */
  play?: string;
  /** The colour of this game's card border. One theme for the site, one trim per game. */
  trim: string;
  link: string;
  /** Set for the games whose daily state the home page tracks; the others open whenever you like. */
  daily?: GameSlug;
}

/**
 * What a card says under its blurb, and whether it can be opened. A daily solved today turns foil, in gold when it
 * took one guess, so the cards you have won today look like the ones worth keeping.
 */
export function cardStatus(
  card: Pick<GameCard, 'daily' | 'play'>, today: TodayEntry | undefined, loaded: boolean | null,
): { label: string; open: boolean; done?: boolean; foil?: 'gold' | 'holo' } {
  if (!card.daily) return { label: card.play ?? 'Play anytime', open: true };
  if (loaded === null) return { label: 'Checking today…', open: false };
  if (!today) return loaded ? { label: 'Coming soon', open: false } : { label: 'Play today', open: true };
  if (!today.available) return { label: 'No puzzle today', open: false };

  switch (today.status) {
    case 'won':
      return { label: today.guesses === 1 ? 'Solved in 1 guess' : `Solved in ${today.guesses}`, open: true, done: true, foil: today.guesses === 1 ? 'gold' : 'holo' };
    case 'lost':
      return { label: 'Missed · see the answer', open: true, done: true };
    case 'playing':
      return { label: `Carry on · ${today.guesses} of ${today.maxGuesses}`, open: true };
    default:
      return { label: `Play No. ${today.number}`, open: true };
  }
}

const COUNT_WORDS = ['No', 'One', 'Two', 'Three', 'Four', 'Five'];

@Component({
  selector: 'app-home',
  imports: [RouterLink, Tilt],
  template: `
    <section class="hero">
      <p class="kicker">Daily sports trivia</p>
      <h1>{{ countWord() }} ways to prove you <span class="script">know ball.</span></h1>
      <p class="lede">A new puzzle in every daily game at midnight Eastern, and Letter Jacket whenever you want it.</p>
    </section>

    <ul class="grid" aria-label="Games">
      @for (card of cards(); track card.name; let i = $index) {
        <li class="card" kbTilt [style.--trim]="card.trim" [class.off]="!card.status.open" [class.kb-foil]="card.status.foil">
          <i class="kb-shine" [class.gold]="card.status.foil === 'gold'" aria-hidden="true"></i>
          <div class="top">
            <span class="no">{{ card.kind }}</span>
            @if (card.streak) {
              <span class="streak" [attr.aria-label]="card.streak + '-day streak'"><span aria-hidden="true">🔥</span>{{ card.streak }}</span>
            }
            <span class="glyph" aria-hidden="true">
              <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
                @switch (card.icon) {
                  @case ('tile') {
                    <rect x="3.5" y="3.5" width="17" height="17" rx="2" fill="var(--mustard)" stroke="var(--cream)" />
                    <path d="M9.5 7v10h5.5" stroke="var(--navy)" stroke-width="2.4" stroke-linecap="square" stroke-linejoin="miter" />
                  }
                  @case ('clipboard') {
                    <rect x="5" y="4.5" width="14" height="16.5" rx="1.5" />
                    <path d="M9 3h6v3H9z" fill="currentColor" />
                    <path d="M8.5 10.5h1.5M12 10.5h3.5M8.5 14h1.5M12 14h3.5M8.5 17.5h1.5M12 17.5h2" />
                  }
                  @case ('jersey') {
                    <path d="M9 3.5c.5 1.4 1.6 2.2 3 2.2s2.5-.8 3-2.2l5 2.3-1.8 4.4-2.2-.9V20.5H8V9.3l-2.2.9L4 5.8z" />
                    <path d="M10.2 11h3.6l-2.4 6" stroke-width="2" />
                  }
                  @case ('card') {
                    <rect x="4.5" y="3" width="15" height="18" rx="1.5" />
                    <path d="M7.5 6.5h9" stroke="var(--mustard)" stroke-width="2.4" />
                    <path d="M7.5 11h2M11.5 11h5M7.5 14h2M11.5 14h5M7.5 17h2M11.5 17h3.5" />
                  }
                  @case ('chart') {
                    <path d="M4 20.5h16" />
                    <rect x="5.5" y="12" width="3" height="8.5" fill="currentColor" stroke="none" />
                    <rect x="10.5" y="6" width="3" height="14.5" fill="var(--mustard)" stroke="none" />
                    <rect x="15.5" y="9.5" width="3" height="11" fill="currentColor" stroke="none" />
                  }
                }
              </svg>
            </span>
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
          <span class="set-no" aria-hidden="true">No. {{ i + 1 }}</span>
        </li>
      }
    </ul>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .hero { padding: 12px 0 34px; max-width: 720px; }
    .kicker { margin: 0 0 10px; color: var(--accent); font-size: 15px; font-weight: 700; text-transform: uppercase; letter-spacing: .18em; }
    h1 { margin: 0; font: 400 clamp(32px, 6vw, 54px)/1.1 var(--display); }
    .script {
      display: inline-block; font: 400 1.3em/1 var(--script); color: var(--red); white-space: nowrap;
      text-shadow: 1.5px 1.5px 0 var(--cream), 3px 3px 0 var(--navy); transform: rotate(-3deg);
    }
    .lede { color: var(--muted); font-size: 20px; margin: 18px 0 0; }

    /* Three across on a wide screen, with a short last row centred under the full one rather than left behind. */
    .grid {
      list-style: none; margin: 0; padding: 0;
      display: flex; flex-wrap: wrap; justify-content: center; gap: 22px 18px;
    }
    .grid > .card { flex: 0 0 calc((100% - 36px) / 3); }
    /* Two across on a tablet. With an odd number of games the first card runs the full width, so no card sits alone. */
    @media (max-width: 760px) {
      .grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .grid > .card:first-child:nth-last-child(odd) { grid-column: 1 / -1; min-height: 0; }
    }
    @media (max-width: 500px) {
      .grid { grid-template-columns: minmax(0, 1fr); }
    }
    /* Each game is a card from the same set: trim colour outside, then a cream gap and a navy line. */
    .card {
      position: relative; isolation: isolate; min-height: 220px; padding: 22px 20px 20px; display: flex; flex-direction: column; gap: 6px;
      background: var(--surface); border: 6px solid var(--trim); border-radius: 8px;
      box-shadow: inset 0 0 0 2px var(--cream), inset 0 0 0 4px var(--navy), var(--card-lift);
      transition: transform .12s ease, box-shadow .12s ease;
    }
    .card.off { opacity: .75; }
    .top { display: flex; align-items: center; justify-content: space-between; }
    .no { font-size: 14px; font-weight: 700; text-transform: uppercase; letter-spacing: .12em; color: var(--muted); }
    .streak { margin-left: auto; margin-right: 8px; font-weight: 700; font-size: 15px; }
    .glyph {
      width: 42px; height: 42px; border-radius: 50%; display: grid; place-items: center;
      background: var(--navy); color: var(--cream); box-shadow: 0 0 0 2px var(--cream), 0 0 0 4px var(--trim);
    }
    .glyph svg { display: block; }
    h2 { margin: 8px 0 0; font: 400 21px/1.15 var(--display); }
    p { margin: 0; flex: 1; color: var(--muted); font-size: 17px; line-height: 1.3; }
    .badge {
      align-self: flex-start; margin-top: 10px; padding: 3px 12px; border: 2px solid var(--navy); border-radius: 999px;
      font-size: 13px; font-weight: 700; text-transform: uppercase; letter-spacing: .06em;
    }
    .badge.play { background: var(--accent); color: var(--accent-ink); text-decoration: none; }
    .badge.done { background: var(--ok); color: var(--ok-ink); }
    /* Stretch the link over the whole card so the card itself is the tap target. */
    .badge.play::after { content: ""; position: absolute; inset: 0; z-index: 3; border-radius: 4px; }
    /* The card's number in the set, on a tab that breaks the bottom border. */
    .set-no {
      position: absolute; right: 16px; bottom: -13px; padding: 2px 7px 1px; border: 2px solid var(--trim); border-radius: 4px;
      background: var(--surface); color: var(--navy); font: 400 12px/1.2 var(--display); letter-spacing: .06em;
    }
    .card:has(.play):hover { transform: translateY(-3px) rotate(-.6deg); }
    /* With a mouse the card turns toward the pointer and lifts off the table. */
    .card:has(.play).tilting {
      transform: perspective(800px) rotateX(var(--rx)) rotateY(var(--ry)) translateY(-4px);
      box-shadow: inset 0 0 0 2px var(--cream), inset 0 0 0 4px var(--navy), 6px 12px 0 rgb(29 43 83 / .14);
      transition: box-shadow .12s ease;
    }

    @media (prefers-reduced-motion: reduce) {
      .card { transition: none; }
      .card:has(.play):hover, .card:has(.play).tilting { transform: none; }
    }
  `,
})
export class Home {
  /** The five games, in the order the home page shows them. */
  private readonly games: GameCard[] = [
    { name: 'Letter Jacket', kind: 'Word game', link: '/letter-jacket', icon: 'tile', trim: 'var(--red)', blurb: 'Six guesses to find a sports word of 4, 5 or 6 letters.' },
    {
      name: 'Scouting Report', kind: 'Daily', link: '/scouting-report', daily: 'scouting-report', icon: 'clipboard', trim: 'var(--teal)',
      blurb: 'Guess the player in 8 tries with team, position, height, age and number clues.',
    },
    {
      name: 'Career Path', kind: 'Daily', link: '/career-path', daily: 'career-path', icon: 'jersey', trim: 'var(--mustard)',
      blurb: 'Every team they played for, in order. Who is it?',
    },
    {
      name: 'Back of the Card', kind: 'Daily', link: '/back-of-the-card', daily: 'back-of-the-card', icon: 'card', trim: 'var(--gold)',
      blurb: "Every season's stats from a player's card, with the name blacked out. Who is it?",
    },
    { name: "Numbers Don't Lie", kind: 'Daily quiz', link: '/numbers-dont-lie', icon: 'chart', trim: 'var(--navy)', play: "Play today's quiz",
      blurb: 'Five questions about records, nicknames, famous numbers and history. Choose your leagues in practice.' },
  ];

  // Undefined while checking availability; null after a failed request.
  private readonly today = signal<TodayEntry[] | null | undefined>(undefined);
  private readonly stats = signal<GameStats[]>([]);

  protected readonly cards = computed(() => {
    const today = this.today();
    const stats = this.stats();
    return this.games.map((card) => ({
      ...card,
      status: cardStatus(card, today?.find((t) => t.game === card.daily), today === undefined ? null : today !== null),
      streak: stats.find((s) => s.game === card.daily)?.currentStreak ?? 0,
    }));
  });
  protected readonly countWord = computed(() => this.today() === undefined ? 'Five' : COUNT_WORDS[this.cards().filter((c) => c.status.open).length] ?? 'Five');

  constructor() {
    // If the API is down the cards still link to the games, which report the problem themselves.
    const api = inject(GameApi);
    api.today().subscribe({ next: (entries) => this.today.set(entries), error: () => this.today.set(null) });
    // Streaks are a bonus; the cards work without them.
    api.stats().subscribe({ next: (stats) => this.stats.set(stats), error: () => {} });
  }
}
