import { Component, DestroyRef, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { easternDate, nextEasternMidnight } from '../../core/games';
import { Countdown } from '../../shared/countdown/countdown';
import { NextGame } from '../../shared/next-game/next-game';
import { Observable, firstValueFrom } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

interface Choice { id: string; label: string; value?: number; }
interface Question { id: string; prompt: string; league: string; topic: string; season: string | null; options: Choice[]; correctOptionId?: string; explanation?: string; source?: {url:string;credit:string;licenseUrl?:string}; }
interface Filters { leagues: string[]; topics: string[]; fromYear: number | null; throughYear: number | null; }
interface Coverage { eligibleFacts: number; freshFacts: number; canStartFresh: boolean; canStartWithRepeats: boolean; leagues: { league: string; eligible: number; fresh: number }[]; }
interface Answer { question: Question; optionId: string; correct: boolean; }
interface State { filters?: Filters | null; id: string; mode: string; date: string | null; position: number; revision: number; answeredCount: number; correctCount: number; dailyStreak?: number | null; complete: boolean; current: Question | null; review: Answer[]; }

@Component({
  selector: 'app-numbers-dont-lie', imports: [Countdown, DatePipe, FormsModule, NextGame],
  template: `
    <div class="nd">
    <header class="kb-game-top">
      <h1 class="kb-wordmark" aria-label="Numbers Don't Lie">Numbers Don't Lie</h1>
      <div class="kb-icons">
        <button type="button" class="kb-icon" aria-label="How to play" title="How to play" (click)="openHelp(helpDialog)">?</button>
        <button type="button" class="kb-icon" aria-label="Practice" title="Practice" (click)="customize()" [disabled]="busy()">
          <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M2 3h7.2a2 2 0 0 1 3.6 0H14v2h-1.2a2 2 0 0 1-3.6 0H2zm0 8h1.2a2 2 0 0 1 3.6 0H14v2H6.8a2 2 0 0 1-3.6 0H2z" /></svg>
        </button>
      </div>
    </header>

    @if (!setup() && state(); as s) {
      <div class="kb-meta">
        <span class="kb-label">@if (s.mode === 'daily') { Daily · {{ s.date | date: 'EEE, MMM d' }} } @else { Practice round }</span>
        <span class="kb-dots" role="img" [attr.aria-label]="s.correctCount + ' right of ' + s.answeredCount + ' answered'">
          @for (n of [0, 1, 2, 3, 4]; track n) { <i [class.hit]="s.review[n]?.correct" [class.miss]="s.review[n] && !s.review[n].correct"></i> }
        </span>
      </div>
    } @else {
      <div class="kb-meta"><span class="kb-label">Practice</span></div>
    }
    @if (setup() || state()?.mode === 'practice' || isPastDaily()) {
      <nav class="modes" aria-label="Quiz mode"><button type="button" class="kb-btn" (click)="daily()" [disabled]="busy()">Today's quiz</button></nav>
    }

    @if (error()) { <div class="problem" role="alert"><p>{{error()}}</p><button class="kb-btn" (click)="retry()" [disabled]="busy()">Retry</button></div> }
    @if (busy()) { <p class="kb-label busy" role="status">Loading…</p> }

    @if (setup()) {
      <section class="panel"><h2>Choose your practice</h2>
        <fieldset class="chips"><legend class="kb-label">Leagues</legend>
          @for (league of leagues; track league) { <label class="kb-chip"><input type="checkbox" [checked]="selected.includes(league)" (change)="toggle(league, 'league')">{{league.toUpperCase()}}</label> }
        </fieldset>
        <details><summary>Customize topics and years</summary>
          <fieldset class="chips"><legend class="kb-label">Topics</legend>@for (topic of topics; track topic) { <label class="kb-chip"><input type="checkbox" [checked]="chosen.includes(topic)" (change)="toggle(topic, 'topic')">{{topic}}</label> }</fieldset>
          <div class="years">
            <label>From season year <input type="number" [(ngModel)]="fromYear" (ngModelChange)="filtersChanged()" min="1900" max="2100" placeholder="Any"></label>
            <label>Through season year <input type="number" [(ngModel)]="throughYear" (ngModelChange)="filtersChanged()" min="1900" max="2100" placeholder="Any"></label>
          </div>
        </details>
        <p class="filter-summary">{{filterSummary()}}</p>
        @if (coverageBusy()) { <p class="muted" role="status">Checking available questions…</p> }
        @if (coverageError()) { <p role="alert">{{coverageError()}}</p><button class="kb-btn" (click)="checkCoverage()">Check availability</button> }
        @if (coverage(); as c) {
          @if (c.canStartFresh) {
            <p class="muted" role="status">Ready: {{c.freshFacts}} questions you haven't seen match.</p>
          } @else {
            <p role="status">Only {{c.freshFacts}} new {{c.freshFacts === 1 ? 'question matches' : 'questions match'}}, not enough for a fresh round. Add leagues or topics, or widen the years.</p>
            @if (c.canStartWithRepeats) { <label class="check"><input type="checkbox" [(ngModel)]="allowRepeats">Allow previously included facts for this round</label> }
            @else { <p>These filters cannot fill a five-question round, even with repeats.</p> }
          }
        }
        <button class="kb-btn primary" (click)="practice()" [disabled]="busy() || coverageBusy() || !canStart()">Start practice</button>
      </section>
    } @else if (state(); as s) {
      <section class="panel">
        @if (s.mode === 'practice') { <p class="filter-summary">{{filterSummary(s.filters)}}</p> }
        @if (s.complete) {
          <div class="final kb-result" [class.won]="s.correctCount === 5" role="status">
            <div class="kb-result-who">
              <h2 class="kb-label" tabindex="-1">That’s the final score.</h2>
              <p class="score">{{s.correctCount}} / 5</p>
              <p class="pips" [attr.aria-label]="s.correctCount + ' of 5 right'">
                @for (a of s.review; track a.question.id) { <span [class.right]="a.correct" aria-hidden="true">{{a.correct ? '✓' : '✗'}}</span> }
              </p>
              @if (s.mode === 'daily' && s.dailyStreak) { <p class="final-detail"><span aria-hidden="true">🔥 </span>Daily streak: {{s.dailyStreak}} {{s.dailyStreak === 1 ? 'day' : 'days'}}</p> }
              @if (s.mode === 'daily' && !isPastDaily()) { <p class="kb-result-detail">Next quiz in <app-countdown [until]="nextDaily" (done)="nextDaily = nextEasternMidnight()" /></p> }
            </div>
            <div class="kb-result-next">
              <button class="kb-btn primary" (click)="share()">Share result</button>
              <button class="kb-btn" (click)="practice()" [disabled]="busy()">Play another round</button>
              <app-next-game current="numbers-dont-lie" />
              <button class="kb-link" (click)="customize()">Customize practice</button>
            </div>
          </div>
          @if (shareStatus()) { <p class="muted" role="status">{{shareStatus()}}</p> }
          <ol class="recap">
            @for (answer of s.review; track answer.question.id) {
              <li [class.right]="answer.correct"><details><summary><span class="mark" aria-hidden="true">{{answer.correct ? '✓' : '✗'}}</span><span>{{answer.correct ? 'Correct' : 'Missed'}} · {{answer.question.prompt}}</span></summary>
                <p>{{answer.question.explanation}}</p>
                @if (answer.question.source; as source) { <a [href]="source.url" target="_blank" rel="noopener">{{source.credit}}</a> }
              </details></li>
            }
          </ol>
        } @else if (s.current; as q) {
          <p class="qmeta">{{q.league.toUpperCase()}} · {{questionLabel(q)}} · Question {{s.position + 1}} of 5</p>
          @if (hotStreak(s) >= 3) { <p class="hot"><span aria-hidden="true">🔥 </span>Hot streak: {{hotStreak(s)}}</p> }
          <h2 tabindex="-1">{{q.prompt}}</h2>
          <fieldset [disabled]="busy() || s.answeredCount > s.position || !!error()"><legend class="visually-hidden">Choose one answer</legend>
            @let shown = (s.review[s.position]?.question ?? q);
            @for (option of shown.options; track option.id; let i = $index) {
              @let answered = s.review[s.position];
              @let isAnswer = !!answered && option.id === answered.question.correctOptionId;
              @let isMiss = !!answered && !answered.correct && option.id === answered.optionId;
              <label class="choice" [class.is-answer]="isAnswer" [class.is-miss]="isMiss" [class.locked]="!!answered">
                <input type="radio" name="answer" [value]="option.id" [(ngModel)]="choice">
                @if (answered && option.value !== undefined) { <span class="bar" aria-hidden="true" [style.--share]="valueShare(shown, option) + '%'"></span> }
                <span class="letter" aria-hidden="true">{{'ABCDEF'[i]}}</span>
                <span class="choice-label">{{option.label}}
                  @if (isAnswer) { <span class="tag"><span aria-hidden="true">✓ </span>Answer</span> }
                  @else if (isMiss) { <span class="tag"><span aria-hidden="true">✗ </span>Your pick</span> }
                </span>
                @if (answered && option.value !== undefined) { <span class="choice-value">{{option.value}}</span> }
              </label>
            }
          </fieldset>
          @if (s.review[s.position]; as answer) {
            <div class="feedback" role="status">
              <div class="verdict" [class.right]="answer.correct" [class.wrong]="!answer.correct" tabindex="-1">
                <span class="verdict-icon" aria-hidden="true">{{answer.correct ? '✓' : '✗'}}</span>
                <div><h3>{{answer.correct ? 'Correct!' : 'Not quite.'}}</h3>
                <p>{{answer.correct ? '' : 'The answer is '}}<strong>{{correctLabel(answer.question)}}</strong></p></div>
              </div>
            <p>{{answer.question.explanation}}</p>
            @if (answer.question.source; as source) {
              <a [href]="source.url" target="_blank" rel="noopener">{{source.credit}}</a>
              @if (source.licenseUrl) { <p class="muted"><a [href]="source.licenseUrl" target="_blank" rel="noopener">CC BY 4.0</a> · Weekly totals aggregated by Know Ball; no endorsement.</p> }
            }</div>
            <button class="kb-btn primary go" (click)="advance()" [disabled]="busy()">{{s.position === 4 ? 'See results' : 'Next question'}}</button>
          } @else { <button class="kb-btn primary go" (click)="answer()" [disabled]="!choice || busy() || !!error()">Lock in answer</button> }
        }
      </section>
    }
    </div>

    <dialog #helpDialog class="kb-dialog" aria-labelledby="nd-help-title" (click)="closeOnBackdrop($event)">
      <div class="kb-dialog-head">
        <h2 id="nd-help-title">How to play</h2>
        <button type="button" class="kb-close" aria-label="Close" (click)="helpDialog.close()">×</button>
      </div>
      <p>Five questions a day about records, nicknames, famous numbers and history across the NBA, NFL, MLB and NHL.</p>
      <p>Pick an answer and lock it in. The reveal shows the real numbers behind every choice.</p>
      <p>Finish the daily quiz on consecutive days to build a streak. A new quiz arrives at midnight Eastern.</p>
      <p>Practice (the sliders button) lets you choose leagues, topics and seasons, and never touches your streak.</p>
      <p class="muted">Your progress is saved in this browser. Practice rounds are kept for 30 days.</p>
    </dialog>
  `,
  styles: `
    :host {display:block;padding-bottom:calc(24px + env(safe-area-inset-bottom));}
    .nd {max-width:620px;margin:0 auto;display:grid;gap:12px;--trim:var(--navy);}
    .kb-game-top .kb-wordmark {font-size:clamp(27px,8vw,44px);}
    h2 {font:400 24px/1.3 var(--display);margin:4px 0 0;overflow-wrap:anywhere;text-wrap:balance;}
    p,label {font-size:18px;line-height:1.45;} p {margin:0;}
    .muted {color:var(--muted);font-size:16px;}
    .modes {display:flex;} .busy {color:var(--muted);}
    .problem {display:flex;flex-wrap:wrap;align-items:center;gap:8px 12px;padding:10px 12px;border:2px solid var(--bad);border-radius:6px;background:var(--surface);}
    .panel {display:grid;gap:12px;padding:20px;background:var(--surface);border:2px solid var(--navy);border-radius:8px;box-shadow:4px 4px 0 var(--navy);}
    fieldset {border:0;padding:0;margin:0;min-width:0;}
    .qmeta {font-size:13px;font-weight:700;letter-spacing:.1em;text-transform:uppercase;color:var(--muted);}
    .hot {justify-self:start;padding:2px 8px;border-radius:4px;background:var(--mustard);color:var(--navy);font-weight:700;font-size:15px;}

    /* Each answer is a card: a lettered badge, the name, and once locked, its real number with a bar to scale. */
    .choice {position:relative;display:grid;grid-template-columns:auto 1fr auto;align-items:center;gap:12px;margin:8px 0;padding:11px 12px;
      border:2px solid var(--navy);border-radius:6px;background:var(--surface);cursor:pointer;overflow:hidden;}
    .choice input {position:absolute;inset:0;width:100%;height:100%;margin:0;opacity:0;cursor:pointer;z-index:2;}
    .choice:hover:not(.locked) {background:var(--surface-2);}
    .choice:has(input:checked):not(.locked) {background:var(--navy);color:var(--cream);transform:translateX(4px);}
    .choice:has(input:checked):not(.locked) .letter {background:var(--mustard);color:var(--navy);}
    .choice:has(input:focus-visible) {outline:3px solid var(--mustard);outline-offset:2px;}
    .choice.locked {cursor:default;} .choice.locked input {cursor:default;}
    .letter {position:relative;z-index:1;display:grid;place-items:center;width:30px;height:30px;border:2px solid currentColor;border-radius:50%;
      background:var(--cream);color:var(--navy);font:400 14px/1 var(--display);}
    .choice-label {position:relative;z-index:1;min-width:0;overflow-wrap:anywhere;font-weight:700;}
    .choice-value {position:relative;z-index:1;font:400 20px/1 var(--display);font-variant-numeric:tabular-nums;}
    .bar {position:absolute;inset:0 auto 0 0;width:var(--share);background:color-mix(in srgb, var(--navy) 9%, transparent);
      animation:grow .7s cubic-bezier(.2,.9,.3,1) both;}
    .is-answer .bar {background:color-mix(in srgb, var(--ok) 22%, transparent);}
    .is-miss .bar {background:color-mix(in srgb, var(--bad) 16%, transparent);}
    @keyframes grow {from {width:0;}}
    .tag {display:inline-block;margin-left:6px;vertical-align:2px;font:400 11px/1 var(--display);letter-spacing:.06em;text-transform:uppercase;padding:4px 7px;border-radius:4px;white-space:nowrap;}
    .choice.is-answer {border:3px solid var(--ok);}
    .choice.is-answer .tag {background:var(--ok);color:var(--ok-ink);}
    .choice.is-answer .letter {background:var(--ok);color:var(--ok-ink);border-color:var(--ok);}
    .choice.is-miss {border:3px solid var(--bad);}
    .choice.is-miss .tag {background:var(--bad);color:var(--cream);}
    .choice.is-miss .letter {background:var(--bad);color:var(--cream);border-color:var(--bad);}
    .go {justify-self:start;padding:10px 20px;font-size:14px;}

    .feedback {display:grid;gap:10px;}
    .verdict {display:flex;align-items:center;gap:14px;padding:14px 16px;border:3px solid var(--navy);border-radius:8px;
      box-shadow:4px 4px 0 var(--navy);animation:verdict-in .35s cubic-bezier(.2,1.4,.4,1) both;outline-offset:4px;scroll-margin:96px 0 24px;}
    .verdict.right {background:var(--ok);color:var(--ok-ink);} .verdict.wrong {background:var(--bad);color:var(--cream);}
    .verdict h3 {font:400 clamp(26px,7vw,34px)/1.1 var(--display);margin:0;letter-spacing:.02em;}
    .verdict p {margin:4px 0 0;font-size:19px;line-height:1.3;overflow-wrap:anywhere;}
    .verdict-icon {flex:none;display:grid;place-items:center;width:48px;height:48px;border-radius:50%;
      background:var(--cream);color:var(--navy);font:700 28px/1 var(--font);border:3px solid var(--navy);}
    .verdict.right .verdict-icon {color:var(--ok);} .verdict.wrong .verdict-icon {color:var(--bad);}
    @keyframes verdict-in {from {transform:scale(.85);opacity:0;}}
    @media (prefers-reduced-motion: reduce) {.verdict, .bar {animation:none;}}
    a {overflow-wrap:anywhere;}

    .final {grid-template-columns:1fr;}
    .final .kb-result-who > h2.kb-label {font:700 13px/1.2 var(--font);color:var(--muted);margin:0;}
    .score {font:400 64px/1 var(--display);color:var(--teal);margin:4px 0;}
    .pips {display:flex;gap:6px;}
    .pips span {display:grid;place-items:center;width:28px;height:28px;border-radius:4px;border:2px solid var(--navy);
      background:var(--miss);color:var(--miss-ink);font-weight:700;}
    .pips .right {background:var(--ok);color:var(--ok-ink);}
    .final-detail {font-weight:700;}
    .recap {list-style:none;margin:0;padding:0;display:grid;}
    .recap li {border-bottom:1px dashed var(--miss-line);}
    .recap summary {display:flex;gap:10px;align-items:baseline;padding:10px 0;cursor:pointer;list-style:none;font-size:17px;}
    .recap summary::-webkit-details-marker {display:none;}
    .recap summary::after {content:"+";margin-left:auto;font-weight:700;}
    .recap details[open] summary::after {content:"−";}
    .recap .mark {flex:none;display:grid;place-items:center;width:22px;height:22px;border-radius:50%;background:var(--bad);color:var(--cream);font-size:13px;font-weight:700;}
    .recap .right .mark {background:var(--ok);}
    .recap details > :not(summary) {margin:0 0 10px 32px;}

    .chips {display:flex;flex-wrap:wrap;gap:8px;align-items:center;}
    .chips legend {float:left;width:100%;margin-bottom:6px;}
    details {display:grid;gap:10px;}
    details > summary {cursor:pointer;font-weight:700;}
    .years {display:flex;flex-wrap:wrap;gap:8px 16px;margin-top:10px;}
    .years label {display:grid;gap:2px;font-size:15px;color:var(--muted);}
    .years input {width:120px;padding:6px 8px;border:2px solid var(--navy);border-radius:4px;background:var(--cream);color:var(--navy);font:700 16px/1.2 var(--font);}
    .filter-summary {font-weight:700;}
    .check {display:flex;gap:8px;align-items:center;} .check input {width:18px;height:18px;accent-color:var(--navy);}
    [role=alert] {color:var(--red);} @media(max-width:400px){.panel{padding:14px;}}
  `
})
export class NumbersDontLie {
  private http = inject(HttpClient); private router = inject(Router);
  /** When tomorrow's daily quiz opens. */
  nextDaily = nextEasternMidnight();
  protected readonly nextEasternMidnight = nextEasternMidnight;
  /** A daily round from an earlier day, reopened from its address: it needs a way to today's quiz. */
  isPastDaily() { const s = this.state(); return s?.mode === 'daily' && !!s.date && s.date !== easternDate(); }
  readonly questionLabel = questionLabel;
  readonly leagues = ['nba','nfl','mlb','nhl']; readonly topics = ['history','nicknames','awards','teams','seasons','stats'];
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
      setTimeout(() => {
        if (this.destroyed) return;
        // After an answer, bring the verdict into view so it is not missed below the choices on a phone.
        const verdict = document.querySelector<HTMLElement>('app-numbers-dont-lie .verdict');
        if (!verdict) { document.querySelector<HTMLElement>('app-numbers-dont-lie h2')?.focus(); return; }
        verdict.focus({preventScroll: true});
        const smooth = !window.matchMedia?.('(prefers-reduced-motion: reduce)')?.matches;
        verdict.scrollIntoView?.({block: 'center', behavior: smooth ? 'smooth' : 'auto'});
      });
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
  /** Correct answers in a row, ending with the latest answer. */
  hotStreak(s: State) { let n = 0; for (let i = s.review.length - 1; i >= 0 && s.review[i].correct; i--) n++; return n; }
  openHelp(d: HTMLDialogElement) { if (d.open) return; if (typeof d.showModal === 'function') d.showModal(); else d.setAttribute('open', ''); }
  closeOnBackdrop(event: MouseEvent) { const d = event.currentTarget as HTMLDialogElement; if (event.target === d) d.close(); }
  /**
   * How far the bar behind an option reaches: the smallest number among the choices gets a stub and the
   * largest the full width, so close totals such as 120 and 144 still look different.
   */
  valueShare(q: Question, option: Choice) {
    const values = q.options.map(o => o.value ?? 0);
    const low = Math.min(...values), high = Math.max(...values);
    return high === low ? 100 : Math.round(15 + 85 * ((option.value ?? 0) - low) / (high - low));
  }
  correctLabel(q:Question) { return q.options.find(o => o.id === q.correctOptionId)?.label || ''; }
  async share() {
    const s = this.state(); if(!s?.complete) return;
    const text = shareText(s);
    try { await navigator.clipboard.writeText(text); this.shareStatus.set('Copied!'); } catch { this.shareStatus.set(text); }
  }
}

/** Spoiler-free share text: the score, a square per answer and the daily streak. */
/** The header's middle segment: the season, a prompt to name it, or the topic for career-long questions. */
export function questionLabel(q: Pick<Question,'season'|'topic'>): string {
  if (q.season) return q.season;
  if (q.topic === 'seasons') return 'Name the season';
  return q.topic.charAt(0).toUpperCase() + q.topic.slice(1);
}

export function shareText(s: Pick<State, 'date' | 'mode' | 'correctCount' | 'review' | 'dailyStreak'>): string {
  const squares = s.review.map(a => a.correct ? '🟩' : '⬜').join('');
  const streak = s.mode === 'daily' && s.dailyStreak ? `\n🔥 ${s.dailyStreak}-day streak` : '';
  return `Numbers Don't Lie · ${s.date || 'Practice'}\n${s.correctCount}/5 ${squares}${streak}\nhttps://knowball.nathanzimmerman.com/numbers-dont-lie`;
}
