import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, ElementRef, Injector, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { Observable } from 'rxjs';
import { AdminApi, AdminOverview, CuratedPuzzle, CuratedPreview } from '../../core/admin-api';
import { serverMessage } from '../../core/game-store';
import { PlayerSearchResult } from '../../core/player-search.service';
import { WhosThatStage } from '../../games/whos-that/whos-that-stage';
import { PlayerSearch } from '../../shared/player-search/player-search';

/**
 * The admin area: how the data and the puzzle queues stand, and the workbench for
 * Who's That Sportsman?, where a photo becomes a puzzle and gets a day on the calendar.
 */
@Component({
  selector: 'app-admin',
  imports: [DatePipe, PlayerSearch, WhosThatStage],
  template: `
    <h1>Admin</h1>

    @if (!overview()) {
      <form class="gate" (submit)="unlock($event, keyBox.value)">
        <label for="admin-key">Admin key</label>
        <input #keyBox id="admin-key" type="password" autocomplete="off" required />
        <button type="submit" class="kb-btn primary" [disabled]="busy()">Unlock</button>
        <p class="fine">The key is the server's <code>Admin:Key</code> setting. It is kept for this tab only.</p>
      </form>
    } @else {
      @let o = overview()!;
      <section>
        <h2>Queues</h2>
        <table>
          <thead><tr><th>Game</th><th>Puzzles</th><th>Days queued</th><th>Through</th></tr></thead>
          <tbody>
            @for (g of o.games; track g.game) {
              <tr [class.low]="g.daysQueued < 3">
                <td>{{ g.name }}</td>
                <td>{{ g.generated ? 'Generated nightly' : 'Made by hand' }}</td>
                <td>{{ g.daysQueued }}</td>
                <td>{{ g.queuedThrough ? (g.queuedThrough | date: 'MMM d') : 'None' }}</td>
              </tr>
            }
          </tbody>
        </table>
        <p class="fine">
          {{ o.data.players }} players ({{ o.data.activePlayers }} current, {{ o.data.playersWithHeadshots }} with a headshot) on
          {{ o.data.teams }} teams.
          @for (run of o.imports; track run.source) {
            <span [class.bad]="run.status !== 'Succeeded'">
              {{ run.source }}: {{ run.status === 'Succeeded' ? (run.finishedAt | date: 'MMM d, h:mm a') : run.status }}.
            </span>
          }
        </p>
      </section>

      <section>
        <h2 #editorHeading tabindex="-1">{{ editing() ? "Edit draft" : "New Who's That Sportsman? puzzle" }}</h2>
        @for (formKey of [editing()?.id ?? 0]; track formKey) {
        <form class="new" (submit)="create($event)">
          <label>Photo <input type="file" name="photo" accept="image/jpeg,image/png,image/webp" [required]="!editing()" /></label>
          <label>
            Cut-out
            <input type="file" name="cutout" accept="image/png,image/webp" [required]="!editing()" />
            <small>A PNG of just the player with the background removed. In Preview: Tools → Remove Background.</small>
          </label>
          @if (editing()) { <p class="fine wide">Leave both image fields empty to keep the current images, or choose both to replace them.</p> }
          <div class="who">
            <span>Player in the photo</span>
            @if (player(); as p) {
              <span class="picked">{{ p.fullName }} <button type="button" class="kb-btn" (click)="player.set(null)">Change</button></span>
            } @else {
              <app-player-search includeInactive label="Player in the photo" placeholder="Type a player's name…" (selected)="player.set($event)" />
            }
          </div>
          <label>Year <input type="number" name="year" [value]="editing()?.year ?? ''" min="1940" max="2100" required /></label>
          <label>Team at the time <input type="text" name="team" [value]="editing()?.team ?? ''" maxlength="60" required /></label>
          <label class="wide">Caption, shown with the photo at the end <input type="text" name="caption" [value]="editing()?.caption ?? ''" maxlength="160" /></label>
          <label class="wide">Photo source URL <input type="url" name="sourceUrl" maxlength="2048" [value]="editing()?.attribution?.sourceUrl ?? ''" /></label>
          <label>Photo credit <input type="text" name="credit" maxlength="200" [value]="editing()?.attribution?.credit ?? ''" /></label>
          <label>License or permission <input type="text" name="license" maxlength="200" [value]="editing()?.attribution?.license ?? ''" /></label>
          <button type="submit" class="kb-btn primary" [disabled]="busy()">{{ editing() ? 'Save draft' : 'Make draft' }}</button>
          @if (editing()) { <button type="button" class="kb-btn" [disabled]="busy()" (click)="cancelEdit()">Cancel edit</button> }
        </form>
        }
      </section>

      @for (group of groups(); track group.title) {
        <section>
          <h2>{{ group.title }} <small>{{ group.puzzles.length }}</small></h2>
          @if (group.note) {
            <p class="fine">{{ group.note }}</p>
          }
          <ul class="puzzles">
            @for (p of group.puzzles; track p.id) {
              <li>
                <span class="shots">
                  <img class="outline" [src]="p.silhouetteUrl" alt="Silhouette" />
                  <img [src]="p.blurredUrl" alt="Blurred hint" />
                  <img [src]="p.photoUrl" alt="Photo" />
                </span>
                <div>
                  <b>{{ p.playerName }}</b>
                  <span>{{ p.team }}, {{ p.year }}@if (p.caption) { · {{ p.caption }} }</span>
                  @if (p.date) {
                    <span class="when">No. {{ p.number }} · {{ p.date | date: 'EEE, MMM d' }}</span>
                  }
                </div>
                <span class="acts">
                  <button type="button" class="kb-btn preview-button" [disabled]="busy()" (click)="openPreview(p)">Preview</button>
                  @switch (p.state) {
                    @case ('draft') {
                      <button type="button" class="kb-btn" [disabled]="busy()" (click)="edit(p)">Edit</button>
                      <input #day type="date" [min]="o.today" [attr.aria-label]="'Date for ' + p.playerName" />
                      <button type="button" class="kb-btn primary" [disabled]="busy()" (click)="run(api.schedule(p.id, day.value || null))">
                        Schedule
                      </button>
                      <button type="button" class="kb-btn" [disabled]="busy()" (click)="remove(p)">Delete</button>
                    }
                    @case ('scheduled') {
                      <button type="button" class="kb-btn" [disabled]="busy() || $first" (click)="run(api.move(p.id, 'earlier'))">Earlier</button>
                      <button type="button" class="kb-btn" [disabled]="busy() || $last" (click)="run(api.move(p.id, 'later'))">Later</button>
                      <button type="button" class="kb-btn" [disabled]="busy()" (click)="run(api.unschedule(p.id))">Unschedule</button>
                    }
                  }
                </span>
              </li>
            } @empty {
              <li class="none">Nothing here.</li>
            }
          </ul>
        </section>
      }

      @if (previewPuzzle(); as p) {
        <section class="preview" aria-label="Player preview">
          <h2 #previewHeading tabindex="-1">Player preview</h2>
          <p class="fine">Saved puzzle · {{ p.playerName }}. Preview does not change player progress or stats.</p>
          <div class="preview-controls" role="group" aria-label="Preview stage">
            @for (stage of previewStages; track $index) {
              <button type="button" class="kb-btn" [disabled]="previewBusy()" [attr.aria-pressed]="previewStep() === $index" (click)="loadPreview($index)">{{ stage }}</button>
            }
          </div>
          @if (preview(); as frame) {
            <app-whos-that-stage [clues]="frame.clues" [reveal]="frame.answer" />
            <p>Year: {{ frame.clues?.year ?? 'Locked until 1 miss' }}</p>
            <p>Team: {{ frame.clues?.team ?? 'Locked until 2 misses' }}</p>
          }
          <button type="button" class="kb-btn" [disabled]="previewBusy()" (click)="closePreview()">Close preview</button>
        </section>
      }
      <button type="button" class="kb-btn" (click)="lock()">Lock</button>
    }

    @if (message(); as text) {
      <p class="kb-toast" role="alert">{{ text }}</p>
    }
  `,
  styles: `
    h1 { margin: 0 0 16px; font: 400 36px/1.1 var(--display); }
    h2 { margin: 0 0 10px; font: 400 21px/1.15 var(--display); }
    h2 small { font: 700 15px var(--font); color: var(--muted); }
    section { margin-bottom: 30px; }
    .fine, small { color: var(--muted); font-size: 15px; }
    .bad, .low td { color: var(--bad); font-weight: 700; }
    input:not([type="file"]) {
      padding: 8px 10px; font: inherit; color: var(--text); background: var(--surface); border: 2px solid var(--line); border-radius: 4px;
    }

    .gate { display: grid; gap: 8px; max-width: 360px; }
    .gate label { font-weight: 700; }

    table { width: 100%; border-collapse: collapse; margin-bottom: 8px; }
    th, td { padding: 6px 8px; border-bottom: 1px solid var(--tan); text-align: left; }
    th { font-size: 13px; letter-spacing: .08em; text-transform: uppercase; }

    .new { display: grid; grid-template-columns: repeat(2, 1fr); gap: 12px 16px; align-items: start; }
    .new label, .who { display: grid; gap: 4px; font-weight: 700; min-width: 0; }
    .preview { max-width: 540px; padding: 14px; border: 2px solid var(--navy); border-radius: 6px; }
    .preview-controls { display: flex; flex-wrap: wrap; gap: 6px; margin-bottom: 16px; }
    .preview [aria-pressed="true"] { background: var(--navy); color: var(--cream); }
    .preview app-whos-that-stage { margin-bottom: 16px; }
    .new .wide, .new .who { grid-column: 1 / -1; }
    .new button[type="submit"] { justify-self: start; }
    .picked { display: flex; align-items: center; gap: 10px; font: 400 19px var(--display); }

    .puzzles { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; }
    .puzzles li {
      display: flex; flex-wrap: wrap; align-items: center; gap: 10px 14px; padding: 10px;
      background: var(--surface); border: 2px solid var(--navy); border-radius: 6px;
    }
    .puzzles .none { border-style: dashed; color: var(--muted); }
    .shots { display: flex; gap: 4px; }
    .shots img { width: 76px; height: 57px; object-fit: cover; border: 2px solid var(--navy); border-radius: 3px; background: var(--navy); }
    .shots .outline { object-fit: contain; background: var(--mustard); }
    .puzzles div { flex: 1 1 200px; display: grid; min-width: 0; }
    .puzzles b { font: 400 18px/1.2 var(--display); }
    .when { font-weight: 700; color: var(--accent); }
    .acts { display: flex; flex-wrap: wrap; gap: 6px; align-items: center; }

    @media (max-width: 560px) {
      .new { grid-template-columns: 1fr; }
    }
  `,
})
export class Admin {
  protected readonly api = inject(AdminApi);

  protected readonly overview = signal<AdminOverview | null>(null);
  protected readonly puzzles = signal<CuratedPuzzle[]>([]);
  protected readonly player = signal<PlayerSearchResult | null>(null);
  protected readonly busy = signal(false);
  protected readonly editing = signal<CuratedPuzzle | null>(null);
  protected readonly previewPuzzle = signal<CuratedPuzzle | null>(null);
  protected readonly preview = signal<CuratedPreview | null>(null);
  protected readonly previewBusy = signal(false);
  protected readonly previewStep = signal(0);
  protected readonly previewStages = ['Silhouette', '1 miss', '2 misses', '3 misses', '4 misses', 'Reveal'];
  private previewVersion = 0;
  private readonly injector = inject(Injector);
  private readonly editorHeading = viewChild<ElementRef<HTMLElement>>('editorHeading');
  private readonly previewHeading = viewChild<ElementRef<HTMLElement>>('previewHeading');
  protected readonly message = signal('');

  protected readonly groups = computed(() => {
    const all = this.puzzles();
    return [
      { title: 'Drafts', note: 'Leave the date empty to take the first free day.', puzzles: all.filter((p) => p.state === 'draft') },
      { title: 'Coming up', note: '', puzzles: all.filter((p) => p.state === 'scheduled') },
      {
        title: 'Published',
        note: 'Live or past puzzles are locked. On a day with nothing scheduled, the one shown longest ago comes round again.',
        puzzles: all.filter((p) => p.state === 'published').slice(0, 30),
      },
    ];
  });

  private messageTimer?: ReturnType<typeof setTimeout>;

  constructor() {
    if (this.api.key()) this.refresh();
  }

  protected unlock(event: Event, key: string): void {
    event.preventDefault();
    this.busy.set(true);
    this.api.overview(key).subscribe({
      next: (overview) => {
        this.api.setKey(key);
        this.overview.set(overview);
        this.busy.set(false);
        this.loadPuzzles();
      },
      error: (error) => this.fail(error, error instanceof HttpErrorResponse && error.status === 404
        ? 'The admin area is switched off: the server has no Admin:Key set.'
        : 'That key was not accepted.'),
    });
  }

  protected lock(): void {
    this.cancelEdit();
    this.closePreview();
    this.api.setKey('');
    this.overview.set(null);
    this.puzzles.set([]);
  }

  protected create(event: Event): void {
    event.preventDefault();
    const formElement = event.target as HTMLFormElement;
    const player = this.player();
    if (!player) {
      this.show('Pick the player in the photo from the list.');
      return;
    }

    const form = new FormData(formElement);
    form.set('playerId', String(player.id));
    this.busy.set(true);
    const editing = this.editing();
    const action = editing ? this.api.update(editing.id, form) : this.api.create(form);
    action.subscribe({
      next: () => {
        this.cancelEdit();
        this.closePreview();
        formElement.reset();
        this.player.set(null);
        this.busy.set(false);
        this.refresh();
      },
      error: (error) => this.fail(error, 'The puzzle could not be made.'),
    });
  }

  protected edit(puzzle: CuratedPuzzle): void {
    this.editing.set(puzzle);
    this.player.set({ id: puzzle.playerId, fullName: puzzle.playerName, headshotUrl: null, position: null, teamAbbreviation: null, teamName: null });
    this.closePreview();
    afterNextRender(() => this.editorHeading()?.nativeElement.focus(), { injector: this.injector });
  }

  protected cancelEdit(): void {
    this.editing.set(null);
    this.player.set(null);
  }

  protected openPreview(puzzle: CuratedPuzzle): void {
    this.previewPuzzle.set(puzzle);
    this.loadPreview(0);
    afterNextRender(() => this.previewHeading()?.nativeElement.focus(), { injector: this.injector });
  }

  protected loadPreview(step: number): void {
    const puzzle = this.previewPuzzle();
    if (!puzzle) return;
    const version = ++this.previewVersion;
    this.previewBusy.set(true);
    this.preview.set(null);
    this.api.preview(puzzle.id, Math.min(step, 4), step === 5).subscribe({
      next: (frame) => {
        if (version !== this.previewVersion) return;
        this.preview.set(frame);
        this.previewStep.set(step);
        this.previewBusy.set(false);
      },
      error: (error) => {
        if (version !== this.previewVersion) return;
        this.previewBusy.set(false);
        this.show(serverMessage(error) ?? 'The preview could not be loaded.');
      },
    });
  }

  protected closePreview(): void {
    this.previewVersion++;
    this.previewPuzzle.set(null);
    this.preview.set(null);
    this.previewBusy.set(false);
  }

  /** Runs a change and then reloads the lists, since scheduling and moving shift other rows too. */
  protected run(action: Observable<unknown>): void {
    this.cancelEdit();
    this.closePreview();
    this.busy.set(true);
    action.subscribe({
      next: () => {
        this.busy.set(false);
        this.refresh();
      },
      error: (error) => this.fail(error, 'That change did not go through.'),
    });
  }

  protected remove(puzzle: CuratedPuzzle): void {
    if (window.confirm(`Delete the ${puzzle.playerName} draft and its images? This cannot be undone.`)) this.run(this.api.remove(puzzle.id));
  }

  private refresh(): void {
    this.api.overview().subscribe({
      next: (overview) => this.overview.set(overview),
      // A key that has stopped working sends the page back to the gate.
      error: () => this.lock(),
    });
    this.loadPuzzles();
  }

  private loadPuzzles(): void {
    this.api.list().subscribe({ next: (puzzles) => this.puzzles.set(puzzles), error: () => this.puzzles.set([]) });
  }

  private fail(error: unknown, fallback: string): void {
    this.busy.set(false);
    this.show(serverMessage(error) ?? fallback);
  }

  private show(text: string): void {
    this.message.set(text);
    clearTimeout(this.messageTimer);
    this.messageTimer = setTimeout(() => this.message.set(''), 5000);
  }
}
