import { Component, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { RouterLink, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

interface Choice { id: string; label: string; value?: number; }
interface Question { id: string; prompt: string; league: string; topic: string; season: string | null; options: Choice[]; correctOptionId?: string; explanation?: string; source?: {url:string;credit:string;licenseUrl?:string}; }
interface State { id: string; mode: string; date: string | null; position: number; revision: number; answeredCount: number; correctCount: number; complete: boolean; current: Question | null; review: {question:Question;optionId:string;correct:boolean}[]; }

@Component({
  selector: 'app-numbers-dont-lie', imports: [FormsModule, RouterLink],
  template: `
    <a routerLink="/">← All games</a>
    <header><p>Daily & practice · Beta</p><h1>Numbers Don't Lie</h1>
    <p>Five questions. Awards, teams, stats and seasons across four leagues.</p></header>
    <p class="note">88-question beta. Progress saves on the server for this browser’s player ID. Practice expires after 30 days.</p>
    <nav aria-label="Quiz mode"><button (click)="daily()" [disabled]="busy()">Daily challenge</button>
      <button (click)="customize()" [disabled]="busy()">Practice</button></nav>
    @if (error()) { <p role="alert">{{error()}}</p><button (click)="retry()" [disabled]="busy()">Retry</button> }
    @if (busy()) { <p role="status">Saving / loading…</p> }
    @if (setup()) {
      <section><h2>Choose your practice</h2>
        <fieldset><legend>Leagues</legend>
          @for (league of leagues; track league) { <label><input type="checkbox" [checked]="selected.includes(league)" (change)="toggle(league, 'league')">{{league.toUpperCase()}}</label> }
        </fieldset>
        <details><summary>Customize topics and years</summary>
          <fieldset><legend>Topics</legend>@for (topic of topics; track topic) { <label><input type="checkbox" [checked]="chosen.includes(topic)" (change)="toggle(topic, 'topic')">{{topic}}</label> }</fieldset>
          <label>From season year <input type="number" [(ngModel)]="fromYear" min="1900" max="2100"></label>
          <label>Through season year <input type="number" [(ngModel)]="throughYear" min="1900" max="2100"></label>
        </details>
        <button (click)="practice()" [disabled]="busy() || !selected.length || !chosen.length">Start practice</button>
      </section>
    } @else if (state(); as s) {
      <section>
        <p>{{s.mode === 'daily' ? 'Daily · ' + s.date : 'Practice'}} · Score {{s.correctCount}} / {{s.answeredCount}}</p>
        @if (s.complete) {
          <h2 tabindex="-1">That’s the final score.</h2><p class="score">{{s.correctCount}} / 5</p>
          <button (click)="practice()" [disabled]="busy()">Play another round</button><button (click)="customize()">Customize practice</button>
          <button (click)="share()">Share result</button><p role="status">{{shareStatus()}}</p>
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
            <button (click)="advance()" [disabled]="busy()">{{s.position === 4 ? 'See results' : 'Next question'}}</button>
          } @else { <button (click)="answer()" [disabled]="!choice || busy() || !!error()">Lock in answer</button> }
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
  private lastAction: (() => Promise<State>) | null = null;
  constructor() {
    try { const saved: unknown = JSON.parse(localStorage.getItem('knowball.numbers.leagues') || 'null');
      if (Array.isArray(saved)) { const valid = this.leagues.filter(l => saved.includes(l)); if(valid.length) this.selected = valid; }
    } catch {}
    const id = new URLSearchParams(location.search).get('round');
    if (id) this.run(() => firstValueFrom(this.http.get<State>('/api/numbers-dont-lie/quizzes/' + encodeURIComponent(id))));
    else this.daily();
  }
  toggle(value:string, kind:string) {
    const current = kind === 'league' ? this.selected : this.chosen;
    const next = current.includes(value) ? current.filter(v => v !== value) : [...current,value];
    if(kind === 'league') { this.selected = next; try {localStorage.setItem('knowball.numbers.leagues',JSON.stringify(next));} catch {} }
    else this.chosen = next;
  }
  customize() { this.setup.set(true); this.error.set(''); }
  daily() { this.run(() => firstValueFrom(this.http.get<State>('/api/numbers-dont-lie/daily'))); }
  practice() {
    const body = {leagues:this.selected,topics:this.chosen,fromYear:this.fromYear,throughYear:this.throughYear,requestId:crypto.randomUUID()};
    this.run(() => firstValueFrom(this.http.post<State>('/api/numbers-dont-lie/practice',body)));
  }
  answer() { this.move('answers',this.choice); }
  advance() { this.move('continue',null); }
  private move(action:string, optionId:string|null) {
    const s = this.state(); if(!s?.current) return;
    const body = {questionId:s.current.id,optionId,requestId:crypto.randomUUID(),expectedRevision:s.revision};
    this.run(() => firstValueFrom(this.http.post<State>(`/api/numbers-dont-lie/quizzes/${s.id}/${action}`,body)));
  }
  retry() { if(this.lastAction) this.run(this.lastAction); }
  private async run(action:() => Promise<State>) {
    if(this.busy()) return; this.lastAction = action; this.busy.set(true); this.error.set('');
    try {
      const s = await action(); this.state.set(s); this.setup.set(false); this.choice = s.review[s.position]?.optionId || '';
      await this.router.navigate([], {queryParams:{round:s.id},replaceUrl:true});
      setTimeout(() => document.querySelector<HTMLElement>('app-numbers-dont-lie h2')?.focus());
    } catch(e) {
      const err = e as HttpErrorResponse;
      this.error.set(err.error?.error || 'Could not reach the server. Retry to safely restore progress.');
      if(err.status === 409 && this.state()) {
        const id = this.state()!.id;
        this.lastAction = () => firstValueFrom(this.http.get<State>('/api/numbers-dont-lie/quizzes/' + id));
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
