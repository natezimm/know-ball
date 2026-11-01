import { Component, DestroyRef, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { RouterLink, Router } from '@angular/router';
import { Observable, firstValueFrom } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

interface Choice { id: string; label: string; value?: number; }
interface Question { id: string; prompt: string; league: string; topic: string; season: string | null; options: Choice[]; correctOptionId?: string; explanation?: string; source?: {url:string;credit:string;licenseUrl?:string}; }
interface Filters { leagues: string[]; topics: string[]; fromYear: number | null; throughYear: number | null; }
interface Coverage { eligibleFacts: number; freshFacts: number; canStartFresh: boolean; canStartWithRepeats: boolean; leagues: { league: string; eligible: number; fresh: number }[]; }
interface State { filters?: Filters | null; id: string; mode: string; date: string | null; position: number; revision: number; answeredCount: number; correctCount: number; complete: boolean; current: Question | null; review: {question:Question;optionId:string;correct:boolean}[]; }

@Component({
  selector: 'app-numbers-dont-lie', imports: [FormsModule, RouterLink],
  template: `
    <a routerLink="/">← All games</a>
    <header><p>Daily & practice · Beta</p><h1>Numbers Don't Lie</h1>
    <p>Five questions. Awards, teams, stats and seasons across four leagues.</p></header>
    <p class="note">96-question beta. Your progress saves in this browser. Practice rounds last 30 days.</p>
    <nav aria-label="Quiz mode"><button class="kb-btn primary" (click)="daily()" [disabled]="busy()">Daily challenge</button>
      <button class="kb-btn" (click)="customize()" [disabled]="busy()">Practice</button></nav>
    @if (error()) { <p role="alert">{{error()}}</p><button class="kb-btn" (click)="retry()" [disabled]="busy()">Retry</button> }
    @if (busy()) { <p role="status">Saving / loading…</p> }
    @if (setup()) {
      <section><h2>Choose your practice</h2>
        <fieldset><legend>Leagues</legend>
          @for (league of leagues; track league) { <label><input type="checkbox" [checked]="selected.includes(league)" (change)="toggle(league, 'league')">{{league.toUpperCase()}}</label> }
        </fieldset>
        <details><summary>Customize topics and years</summary>
          <fieldset><legend>Topics</legend>@for (topic of topics; track topic) { <label><input type="checkbox" [checked]="chosen.includes(topic)" (change)="toggle(topic, 'topic')">{{topic}}</label> }</fieldset>
          <label>From season year <input type="number" [(ngModel)]="fromYear" (ngModelChange)="filtersChanged()" min="1900" max="2100"></label>
          <label>Through season year <input type="number" [(ngModel)]="throughYear" (ngModelChange)="filtersChanged()" min="1900" max="2100"></label>
        </details>
        <p class="filter-summary">{{filterSummary()}}</p>
        @if (coverageBusy()) { <p role="status">Checking available questions…</p> }
        @if (coverageError()) { <p role="alert">{{coverageError()}}</p><button class="kb-btn" (click)="checkCoverage()">Check availability</button> }
        @if (coverage(); as c) {
          <p role="status">{{c.freshFacts}} fresh facts / {{c.eligibleFacts}} matching facts.</p>
          <p class="note">Fresh facts were not included in your rounds in the last 30 days. Five questions are balanced across your selected leagues.</p>
          @for (entry of c.leagues; track entry.league) { <p class="note">{{entry.league.toUpperCase()}}: {{entry.fresh}} fresh / {{entry.eligible}} matching</p> }
          @if (!c.canStartFresh) {
            <p role="status">Not enough fresh facts for a full round. Add leagues or topics, or widen the years.</p>
            @if (c.canStartWithRepeats) { <label><input type="checkbox" [(ngModel)]="allowRepeats">Allow previously included facts for this round</label> }
            @else { <p>These filters cannot fill a five-question round, even with repeats.</p> }
          }
        }
        <button class="kb-btn primary" (click)="practice()" [disabled]="busy() || coverageBusy() || !canStart()">Start practice</button>
      </section>
    } @else if (state(); as s) {
      <section>
        <p>{{s.mode === 'daily' ? 'Daily · ' + s.date : 'Practice'}} · Score {{s.correctCount}} / {{s.answeredCount}}</p>
        @if (s.mode === 'practice') { <p class="filter-summary">{{filterSummary(s.filters)}}</p> }
        @if (s.complete) {
          <h2 tabindex="-1">That’s the final score.</h2><p class="score">{{s.correctCount}} / 5</p>
          <button class="kb-btn" (click)="practice()" [disabled]="busy()">Play another round</button><button class="kb-btn" (click)="customize()">Customize practice</button>
          <button class="kb-btn" (click)="share()">Share result</button><p role="status">{{shareStatus()}}</p>
          @for (answer of s.review; track answer.question.id) {
            <details><summary>{{answer.correct ? 'Correct' : 'Missed'}} · {{answer.question.prompt}}</summary>
              <p>{{answer.question.explanation}}</p>
              @if (answer.question.source; as source) { <a [href]="source.url" target="_blank" rel="noopener">{{source.credit}}</a> }
            </details>
          }
        } @else if (s.current; as q) {
          <p>{{q.league.toUpperCase()}} · {{q.season || 'Name the season'}} · Question {{s.position + 1}} of 5</p>
          <h2 tabindex="-1">{{q.prompt}}</h2>
          <fieldset [disabled]="busy() || s.answeredCount > s.position || !!error()"><legend>Choose one answer</legend>
            @for (option of q.options; track option.id) {
              <label class="choice"><input type="radio" name="answer" [value]="option.id" [(ngModel)]="choice">{{option.label}}</label>
            }
          </fieldset>
          @if (s.review[s.position]; as answer) {
            <div class="feedback" role="status"><h3>{{answer.correct ? 'Correct!' : 'Not quite.'}} {{correctLabel(answer.question)}}</h3>
            <p>{{answer.question.explanation}}</p>
            @for (option of answer.question.options; track option.id) { @if (option.value !== undefined) { <p>{{option.label}}: {{option.value}}</p> } }
            @if (answer.question.source; as source) {
              <a [href]="source.url" target="_blank" rel="noopener">{{source.credit}}</a>
              @if (source.licenseUrl) { <p><a [href]="source.licenseUrl" target="_blank" rel="noopener">CC BY 4.0</a> · Weekly totals aggregated by Know Ball; no endorsement.</p> }
            }</div>
            <button class="kb-btn primary" (click)="advance()" [disabled]="busy()">{{s.position === 4 ? 'See results' : 'Next question'}}</button>
          } @else { <button class="kb-btn primary" (click)="answer()" [disabled]="!choice || busy() || !!error()">Lock in answer</button> }
        }
      </section>
    }
  `,
  styles: `
    :host {display:block;max-width:740px;margin:auto;padding-bottom:calc(24px + env(safe-area-inset-bottom));}
    header {margin:24px 0;} h1 {font:400 clamp(30px,6vw,48px)/1.15 var(--display);margin:12px 0;}
    h2 {font:400 25px/1.3 var(--display);overflow-wrap:anywhere;}
    p,label {font-size:18px;line-height:1.5;} .note {color:var(--muted);font-size:15px;}
    section {margin-top:20px;padding:24px;background:var(--surface);border:5px solid var(--teal);border-radius:8px;}
    fieldset {border:0;padding:0;margin:18px 0;} label {display:block;padding:10px 0;}
    .choice {border:2px solid var(--navy);border-radius:6px;margin:10px 0;padding:12px;cursor:pointer;}
    input {margin-right:12px;max-width:100%;} button {margin:8px 8px 8px 0;padding:12px 18px;cursor:pointer;}
    button:disabled {opacity:.6;cursor:default;} .feedback {border-top:3px solid var(--teal);margin-top:20px;}
    .score {font:700 64px var(--display);color:var(--teal);} details {padding:14px 0;} a {overflow-wrap:anywhere;}
    [role=alert] {color:var(--red);} @media(max-width:400px){section{padding:14px;} }
  `
})
export class NumbersDontLie {
  private http = inject(HttpClient); private router = inject(Router);
  readonly leagues = ['nba','nfl','mlb','nhl']; readonly topics = ['stats','awards','teams','seasons'];
  selected = [...this.leagues]; chosen = [...this.topics]; fromYear: number | null = null; throughYear: number | null = null;
  state = signal<State | null>(null); setup = signal(false); busy = signal(false); error = signal(''); shareStatus = signal(''); choice = '';
  readonly coverage = signal<Coverage | null>(null); readonly coverageBusy = signal(false); readonly coverageError = signal('');
  allowRepeats = false;
  private coverageVersion = 0;
  private practiceRequestId = crypto.randomUUID();
  private lastAction: (() => Promise<State>) | null = null;
  private recoverConflict = false;
  private readonly destroyRef = inject(DestroyRef);
  /** Set once the player leaves the page; late responses must not navigate or move focus then. */
  private destroyed = false;
  constructor() {
    this.destroyRef.onDestroy(() => { this.destroyed = true; });
    try {
      const saved = JSON.parse(localStorage.getItem('knowball.numbers.filters') || 'null');
      if (saved) this.restoreFilters(saved);
      else {
        const leagues: unknown = JSON.parse(localStorage.getItem('knowball.numbers.leagues') || 'null');
        if (Array.isArray(leagues)) { const valid = this.leagues.filter(l => leagues.includes(l)); if (valid.length) this.selected = valid; }
      }
    } catch { /* Invalid or blocked storage uses defaults. */ }
    const id = new URLSearchParams(location.search).get('round');
    if (id) this.run(() => this.fetch(this.http.get<State>('/api/numbers-dont-lie/quizzes/' + encodeURIComponent(id))));
    else this.daily();
  }
  toggle(value:string, kind:string) {
    const current = kind === 'league' ? this.selected : this.chosen;
    const next = current.includes(value) ? current.filter(v => v !== value) : [...current,value];
    if(kind === 'league') this.selected = next;
    else this.chosen = next;
    this.filtersChanged();
  }
  private filters(): Filters { return { leagues: [...this.selected], topics: [...this.chosen], fromYear: this.fromYear, throughYear: this.throughYear }; }
  private restoreFilters(value: Partial<Filters>) {
    if (Array.isArray(value.leagues)) { const valid = this.leagues.filter(l => value.leagues!.includes(l)); if (valid.length) this.selected = valid; }
    if (Array.isArray(value.topics)) { const valid = this.topics.filter(t => value.topics!.includes(t)); if (valid.length) this.chosen = valid; }
    const year = (n: unknown): number | null => typeof n === 'number' && Number.isInteger(n) && n >= 1900 && n <= 2100 ? n : null;
    this.fromYear = year(value.fromYear); this.throughYear = year(value.throughYear);
    if (this.fromYear !== null && this.throughYear !== null && this.fromYear > this.throughYear) { this.fromYear = null; this.throughYear = null; }
  }
  private saveFilters() { try { localStorage.setItem('knowball.numbers.filters', JSON.stringify(this.filters())); } catch {} }
  filterSummary(filters: Filters | null | undefined = this.filters()): string {
    const f = filters || this.filters();
    return f.leagues.map(l => l.toUpperCase()).join(', ') + ' · ' + f.topics.join(', ') + ' · ' +
      (f.fromYear === null && f.throughYear === null ? 'All years' : (f.fromYear ?? 'Earliest') + '–' + (f.throughYear ?? 'Latest'));
  }
  filtersChanged() {
    this.saveFilters(); this.allowRepeats = false; this.error.set(''); this.lastAction = null;
    this.practiceRequestId = crypto.randomUUID(); void this.checkCoverage();
  }
  async checkCoverage() {
    const version = ++this.coverageVersion;
    this.coverage.set(null); this.coverageError.set(''); this.coverageBusy.set(false);
    const f = this.filters();
    if (!f.leagues.length || !f.topics.length || [f.fromYear, f.throughYear].some(n => n !== null && (!Number.isInteger(n) || n < 1900 || n > 2100)) ||
      (f.fromYear !== null && f.throughYear !== null && f.fromYear > f.throughYear)) {
      this.coverageError.set('Select at least one league and topic, and an ordered year range (1900–2100).'); return;
    }
    this.coverageBusy.set(true);
    try {
      const c = await this.fetch(this.http.post<Coverage>('/api/numbers-dont-lie/practice/coverage', {...f, requestId: this.practiceRequestId}));
      if (version === this.coverageVersion) this.coverage.set(c);
    } catch { if (this.destroyed) return; if (version === this.coverageVersion) this.coverageError.set('Could not check available questions. Please retry.'); }
    finally { if (version === this.coverageVersion) this.coverageBusy.set(false); }
  }
  canStart() { const c = this.coverage(); return !!c && (c.canStartFresh || (this.allowRepeats && c.canStartWithRepeats)); }
  customize() {
    this.setup.set(true); this.error.set(''); this.lastAction = null; this.allowRepeats = false;
    this.practiceRequestId = crypto.randomUUID(); void this.checkCoverage();
  }
  daily() { this.run(() => this.fetch(this.http.get<State>('/api/numbers-dont-lie/daily'))); }
  practice() {
    const body = {...this.filters(), requestId: this.setup() ? this.practiceRequestId : crypto.randomUUID(), allowRepeats: this.setup() && this.allowRepeats};
    this.saveFilters();
    let checked = false;
    this.run(async () => {
      if (checked) return this.fetch(this.http.post<State>('/api/numbers-dont-lie/practice', body));
      // Recheck before creation, including Play another round after a completed quiz.
      const c = await this.fetch(this.http.post<Coverage>('/api/numbers-dont-lie/practice/coverage', body));
      this.coverage.set(c);
      if (!c.canStartFresh && !(body.allowRepeats && c.canStartWithRepeats)) {
        this.practiceRequestId = body.requestId; this.allowRepeats = false; this.setup.set(true);
        throw new HttpErrorResponse({status: 422, error: {error: 'Choose broader filters or explicitly allow repeats to continue.'}});
      }
      checked = true; // A lost start response must retry its receipt, not fail coverage against its own round.
      return this.fetch(this.http.post<State>('/api/numbers-dont-lie/practice', body));
    });
  }
  answer() { this.move('answers',this.choice); }
  advance() { this.move('continue',null); }
  private move(action:string, optionId:string|null) {
    const s = this.state(); if(!s?.current) return;
    const body = {questionId:s.current.id,optionId,requestId:crypto.randomUUID(),expectedRevision:s.revision};
    this.run(() => this.fetch(this.http.post<State>(`/api/numbers-dont-lie/quizzes/${s.id}/${action}`,body)), true);
  }
  retry() { if(this.lastAction) this.run(this.lastAction, this.recoverConflict); }
  /** One response, cancelled if the page closes first (the promise then rejects and is ignored). */
  private fetch<T>(request: Observable<T>): Promise<T> { return firstValueFrom(request.pipe(takeUntilDestroyed(this.destroyRef))); }
  private async run(action:() => Promise<State>, recoverConflict = false) {
    if(this.busy()) return; this.lastAction = action; this.recoverConflict = recoverConflict; this.busy.set(true); this.error.set('');
    try {
      const s = await action();
      if (this.destroyed) return;
      if (s.mode === 'practice' && s.filters) { this.restoreFilters(s.filters); this.saveFilters(); }
      this.state.set(s); this.setup.set(false); this.choice = s.review[s.position]?.optionId || '';
      await this.router.navigate([], {queryParams:{round:s.id},replaceUrl:true});
      if (this.destroyed) return;
      setTimeout(() => !this.destroyed && document.querySelector<HTMLElement>('app-numbers-dont-lie h2')?.focus());
    } catch(e) {
      if (this.destroyed) return;
      const err = e as HttpErrorResponse;
      this.error.set(err.error?.error || 'Could not reach the server. Retry to safely restore progress.');
      if (err.status === 422) { this.error.set(''); this.lastAction = null; }
      if(recoverConflict && err.status === 409 && this.state()) {
        const id = this.state()!.id;
        this.lastAction = () => this.fetch(this.http.get<State>('/api/numbers-dont-lie/quizzes/' + id));
      }
    } finally { this.busy.set(false); }
  }
  correctLabel(q:Question) { return q.options.find(o => o.id === q.correctOptionId)?.label || ''; }
  async share() {
    const s = this.state(); if(!s?.complete) return;
    const text = `Numbers Don't Lie · ${s.date || 'Practice'}\n${s.correctCount}/5 ${s.review.map(a => a.correct ? '🟩' : '⬜').join('')}\nhttps://knowball.nathanzimmerman.com/numbers-dont-lie`;
    try { await navigator.clipboard.writeText(text); this.shareStatus.set('Copied!'); } catch { this.shareStatus.set(text); }
  }
}
