import { Component, DestroyRef, ElementRef, afterRenderEffect, booleanAttribute, inject, input, output, signal, viewChild, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, catchError, map, of, switchMap, timer } from 'rxjs';
import { MIN_QUERY_LENGTH, PlayerSearchResult, PlayerSearchService } from '../../core/player-search.service';
import { Avatar } from '../avatar/avatar';

let nextId = 0;

/**
 * Autocomplete used by every guess box. Accessible combobox:
 * arrow keys move, Enter picks, Escape closes.
 */
@Component({
  selector: 'app-player-search',
  imports: [Avatar],
  template: `
    <div class="combo" [class.open]="open()">
      <label class="visually-hidden" [for]="inputId">{{ label() }}</label>
      <input
        #box
        [id]="inputId"
        type="text"
        role="combobox"
        autocomplete="off"
        spellcheck="false"
        [attr.aria-expanded]="open()"
        [attr.aria-controls]="listId"
        [attr.aria-activedescendant]="activeIndex() >= 0 ? listId + '-' + activeIndex() : null"
        aria-autocomplete="list"
        [placeholder]="placeholder()"
        [disabled]="disabled()"
        [value]="query()"
        (input)="onInput($any($event.target).value)"
        (keydown)="onKeydown($event)"
        (focus)="open.set(results().length > 0)"
        (blur)="onBlur()"
      />
      @if (loading()) {
        <span class="spinner" aria-hidden="true"></span>
      }
      @if (open()) {
        <ul class="list" role="listbox" [id]="listId" [style.--results-height.px]="resultsHeight()">
          @for (p of results(); track p.id; let i = $index) {
            <li
              role="option"
              [id]="listId + '-' + i"
              [attr.aria-selected]="i === activeIndex()"
              [class.active]="i === activeIndex()"
              (mousedown)="$event.preventDefault(); choose(p)"
              (mouseenter)="activeIndex.set(i)"
            >
              <app-avatar [src]="p.headshotUrl" [name]="p.fullName" />
              <span class="name">{{ p.fullName }}</span>
              <span class="meta">
                @if (p.teamAbbreviation) {
                  <span class="team">{{ p.teamAbbreviation }}</span>
                } @else if (p.years) {
                  <span>{{ p.years }}</span>
                }
                @if (p.position) { <span>{{ p.position }}</span> }
              </span>
            </li>
          }
        </ul>
      }
      @if (noMatches()) {
        <p class="hint" role="status">No players match "{{ query() }}"</p>
      }
      @if (error()) {
        <p class="hint error" role="alert">Search is unavailable right now.</p>
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .combo { position: relative; }
    input {
      width: 100%; padding: 14px 44px 14px 16px; font: inherit; font-size: 17px;
      color: var(--text); background: var(--surface); border: 2px solid var(--line);
      border-radius: var(--radius); transition: border-color .15s;
    }
    input::placeholder { color: var(--muted); }
    input:focus { outline: none; border-color: var(--accent); }
    .open input { border-bottom-left-radius: 0; border-bottom-right-radius: 0; }
    .spinner {
      position: absolute; right: 16px; top: 50%; width: 16px; height: 16px; margin-top: -8px;
      border: 2px solid var(--line); border-top-color: var(--accent); border-radius: 50%;
      animation: spin .7s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }
    .list {
      position: absolute; left: 0; right: 0; z-index: 5; margin: 0; padding: 4px; list-style: none;
      background: var(--surface); border: 2px solid var(--accent); border-top: 0;
      border-radius: 0 0 var(--radius) var(--radius); max-height: var(--results-height, min(320px, 45dvh)); overflow-y: auto;
    }
    li {
      display: flex; align-items: center; gap: 10px;
      padding: 7px 10px; border-radius: 4px; cursor: pointer;
    }
    li.active { background: var(--surface-2); }
    app-avatar { --size: 34px; }
    .name { flex: 1; font-weight: 700; line-height: 1.15; }
    .meta { display: flex; gap: 8px; color: var(--muted); font-size: 15px; white-space: nowrap; }
    .team {
      font-weight: 700; letter-spacing: .06em; color: var(--accent);
    }
    .hint { margin: 8px 4px 0; color: var(--muted); font-size: 16px; }
    .hint.error { color: var(--bad); font-weight: 700; }
  `,
})
export class PlayerSearch {
  private readonly api = inject(PlayerSearchService);

  readonly label = input('Search for a player');
  readonly placeholder = input('Type a player name…');
  readonly league = input('nba');
  readonly puzzleId = input<number>();
  readonly disabled = input(false);
  /** Offer retired players too. */
  readonly includeInactive = input(false, { transform: booleanAttribute });
  /** Debounce in ms; tests set 0. */
  readonly debounceMs = input(200);

  readonly selected = output<PlayerSearchResult>();

  protected readonly inputId = `player-search-${nextId++}`;
  protected readonly listId = `${this.inputId}-list`;

  protected readonly query = signal('');
  protected readonly results = signal<PlayerSearchResult[]>([]);
  protected readonly activeIndex = signal(-1);
  protected readonly open = signal(false);
  protected readonly loading = signal(false);
  protected readonly error = signal(false);
  protected readonly noMatches = signal(false);
  protected readonly resultsHeight = signal(320);

  private readonly box = viewChild.required<ElementRef<HTMLInputElement>>('box');
  private readonly typed$ = new Subject<string>();

  constructor() {
    // The list opens below the box; keep it above the keyboard, which on iOS covers the
    // bottom of the layout viewport while only the visual viewport shrinks.
    const measure = () => {
      if (!this.open()) return;
      const viewport = window.visualViewport;
      const bottom = viewport ? viewport.offsetTop + viewport.height : window.innerHeight;
      const room = bottom - this.box().nativeElement.getBoundingClientRect().bottom - 8;
      this.resultsHeight.set(Math.max(0, Math.min(320, room)));
    };
    afterRenderEffect(measure);
    // Follow the box after a viewport or scroll change has been painted.
    let frame = 0;
    const onResize = () => {
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(measure);
    };
    window.addEventListener('resize', onResize);
    window.addEventListener('scroll', onResize, true);
    window.visualViewport?.addEventListener('resize', onResize);
    window.visualViewport?.addEventListener('scroll', onResize);
    inject(DestroyRef).onDestroy(() => {
      cancelAnimationFrame(frame);
      window.removeEventListener('resize', onResize);
      window.removeEventListener('scroll', onResize, true);
      window.visualViewport?.removeEventListener('resize', onResize);
      window.visualViewport?.removeEventListener('scroll', onResize);
    });
    this.typed$
      .pipe(
        map((q) => q.trim()),
        // Cancel the previous request immediately, before waiting for the new query.
        switchMap((q) => {
          this.error.set(false);
          this.loading.set(q.length >= MIN_QUERY_LENGTH);
          if (q.length < MIN_QUERY_LENGTH) return of({ q, rows: [] as PlayerSearchResult[] });
          return timer(this.debounceMs()).pipe(
            switchMap(() => this.api.search(q, this.league(), 8, this.includeInactive(), this.puzzleId())),
            map((rows) => ({ q, rows })),
            catchError(() => {
              this.error.set(true);
              return of({ q, rows: [] as PlayerSearchResult[] });
            }),
          );
        }),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe(({ q, rows }) => {
        this.loading.set(false);
        this.results.set(rows);
        this.activeIndex.set(rows.length > 0 ? 0 : -1);
        this.open.set(rows.length > 0);
        this.noMatches.set(rows.length === 0 && q.length >= MIN_QUERY_LENGTH && !this.error());
      });
  }

  protected onInput(value: string): void {
    this.query.set(value);
    this.results.set([]);
    this.activeIndex.set(-1);
    this.open.set(false);
    this.noMatches.set(false);
    this.typed$.next(value);
  }

  protected onKeydown(event: KeyboardEvent): void {
    const count = this.results().length;
    switch (event.key) {
      case 'ArrowDown':
        if (!count) return;
        event.preventDefault();
        this.open.set(true);
        this.activeIndex.update((i) => (i + 1) % count);
        break;
      case 'ArrowUp':
        if (!count) return;
        event.preventDefault();
        this.open.set(true);
        this.activeIndex.update((i) => (i <= 0 ? count - 1 : i - 1));
        break;
      case 'Enter': {
        const pick = this.results()[this.activeIndex()];
        if (this.open() && pick) {
          event.preventDefault();
          this.choose(pick);
        }
        break;
      }
      case 'Escape':
        this.open.set(false);
        break;
    }
  }

  protected onBlur(): void {
    this.open.set(false);
  }

  protected choose(player: PlayerSearchResult): void {
    if (this.disabled() || !this.open() || !this.results().includes(player)) return;
    this.selected.emit(player);
    this.clear();
  }

  /** Empties the box so the next guess starts fresh. */
  clear(): void {
    this.query.set('');
    this.results.set([]);
    this.activeIndex.set(-1);
    this.open.set(false);
    this.noMatches.set(false);
    this.typed$.next('');
  }

  focus(): void {
    this.box().nativeElement.focus();
  }
}
