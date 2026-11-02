import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { EnvironmentInjector, createEnvironmentInjector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { NumbersDontLie, shareText } from './numbers-dont-lie';
import { useMemoryStorage } from '../spordle/memory-storage';

const ROOT = '/api/numbers-dont-lie';
const filters = { leagues: ['nba'], topics: ['stats'], fromYear: 2023, throughYear: 2023 };
const daily = { id: 'daily', mode: 'daily', date: '2026-10-28', position: 0, revision: 0, answeredCount: 0,
  correctCount: 0, complete: false, current: null, review: [] };
const fresh = { eligibleFacts: 6, freshFacts: 6, canStartFresh: true, canStartWithRepeats: true,
  leagues: [{ league: 'nba', eligible: 6, fresh: 6 }] };

describe('Numbers practice filters and coverage', () => {
  let game: NumbersDontLie;
  let http: HttpTestingController;
  async function settle() { await Promise.resolve(); await Promise.resolve(); await Promise.resolve(); }
  function create() { game = TestBed.runInInjectionContext(() => new NumbersDontLie()); }
  beforeEach(async () => {
    useMemoryStorage();
    TestBed.configureTestingModule({ providers: [provideHttpClient(withXhr()), provideHttpClientTesting(),
      { provide: Router, useValue: { navigate: vi.fn().mockResolvedValue(true) } }] });
    http = TestBed.inject(HttpTestingController);
    create(); http.expectOne(ROOT + '/daily').flush(daily); await settle();
  });
  afterEach(() => http.verify());

  it('persists all filters and restores the saved round filters over later preferences', async () => {
    game.selected = ['nba']; game.chosen = ['stats']; game.fromYear = 2023; game.throughYear = 2023;
    game.filtersChanged(); http.expectOne(ROOT + '/practice/coverage').flush(fresh); await settle();
    create(); http.expectOne(ROOT + '/daily').flush(daily); await settle();
    expect(game.selected).toEqual(['nba']); expect(game.chosen).toEqual(['stats']);
    expect(game.fromYear).toBe(2023); expect(game.throughYear).toBe(2023);
    game.practice();
    http.expectOne(ROOT + '/practice/coverage').flush(fresh); await settle();
    const start = http.expectOne(ROOT + '/practice');
    expect(start.request.body).toMatchObject(filters);
    start.flush({ ...daily, id: 'practice', mode: 'practice', complete: true, filters }); await settle();
    game.selected = ['nfl']; // A saved practice response remains authoritative.
    game.retry();
    http.expectOne(ROOT + '/practice').flush({ ...daily, id: 'practice', mode: 'practice', complete: true, filters }); await settle();
    expect(game.selected).toEqual(['nba']);
    expect(game.filterSummary()).toBe('NBA · stats · 2023–2023');
  });

  it('does not let an older coverage response enable starting with newer filters', async () => {
    game.customize(); const old = http.expectOne(ROOT + '/practice/coverage');
    game.chosen = ['stats']; game.filtersChanged(); const latest = http.expectOne(ROOT + '/practice/coverage');
    latest.flush({ ...fresh, freshFacts: 1, canStartFresh: false }); await settle();
    old.flush(fresh); await settle();
    expect(game.coverage()!.freshFacts).toBe(1); expect(game.canStart()).toBe(false);
  });

  it('requires repeat opt-in after exhaustion and retains start receipts after a lost response', async () => {
    game.practice();
    http.expectOne(ROOT + '/practice/coverage').flush({ ...fresh, freshFacts: 1, canStartFresh: false }); await settle();
    expect(game.setup()).toBe(true); expect(game.canStart()).toBe(false);
    http.expectNone(ROOT + '/practice');
    game.allowRepeats = true; game.practice();
    http.expectOne(ROOT + '/practice/coverage').flush({ ...fresh, freshFacts: 1, canStartFresh: false }); await settle();
    const first = http.expectOne(ROOT + '/practice');
    const id = first.request.body.requestId;
    expect(first.request.body.allowRepeats).toBe(true);
    first.error(new ProgressEvent('error')); await settle();
    game.retry();
    http.expectNone(ROOT + '/practice/coverage');
    const retry = http.expectOne(ROOT + '/practice'); expect(retry.request.body.requestId).toBe(id);
    retry.flush({ ...daily, id: 'practice', mode: 'practice', filters }); await settle();
    expect(game.error()).toBe('');
    game.customize(); http.expectOne(ROOT + '/practice/coverage').flush(fresh); await settle();
    expect(game.allowRepeats).toBe(false);
  });

  it('blocks invalid year ranges locally and handles damaged preferences', async () => {
    game.fromYear = 2024; game.throughYear = 2023; game.filtersChanged();
    expect(game.canStart()).toBe(false); expect(game.coverageError()).toContain('ordered year');
    http.expectNone(ROOT + '/practice/coverage');
    localStorage.setItem('knowball.numbers.filters', '{broken');
    create(); http.expectOne(ROOT + '/daily').flush(daily); await settle();
    expect(game.selected).toEqual(['nba', 'nfl', 'mlb', 'nhl']);
    expect(game.fromYear).toBeNull();
  });

  it('cancels its requests and leaves the URL and focus alone once the page is gone', async () => {
    vi.useFakeTimers();
    const router = TestBed.inject(Router) as unknown as { navigate: ReturnType<typeof vi.fn> };
    router.navigate.mockClear();
    const page = createEnvironmentInjector([], TestBed.inject(EnvironmentInjector));
    const focus = vi.spyOn(HTMLElement.prototype, 'focus');
    const leaving = page.runInContext(() => new NumbersDontLie());
    const pending = http.expectOne(ROOT + '/daily');
    page.destroy();
    expect(pending.cancelled).toBe(true);
    await settle(); vi.runAllTimers();
    expect(router.navigate).not.toHaveBeenCalled();
    expect(focus).not.toHaveBeenCalled();
    expect(leaving.error()).toBe('');
    focus.mockRestore(); vi.useRealTimers();
  });

  it('sends the confidence bet with an answer and restores the saved bet', async () => {
    const question = { id: 'q1', prompt: 'Who?', league: 'nba', topic: 'awards', season: '2010–11', options: [{ id: 'a', label: 'A' }, { id: 'b', label: 'B' }] };
    create(); http.expectOne(ROOT + '/daily').flush({ ...daily, current: question }); await settle();
    expect(game.confidence).toBe(2);
    game.choice = 'a'; game.confidence = 3; game.answer();
    const sent = http.expectOne(ROOT + '/quizzes/daily/answers');
    expect(sent.request.body).toMatchObject({ questionId: 'q1', optionId: 'a', confidence: 3 });
    sent.flush({ ...daily, current: question, answeredCount: 1, correctCount: 1, points: 3, maxPoints: 15,
      review: [{ question: { ...question, correctOptionId: 'a' }, optionId: 'a', correct: true, confidence: 3 }] }); await settle();
    game.confidence = 1;
    create(); http.expectOne(ROOT + '/daily').flush({ ...daily, current: question, answeredCount: 1,
      review: [{ question: { ...question, correctOptionId: 'a' }, optionId: 'a', correct: true, confidence: 3 }] }); await settle();
    expect(game.confidence).toBe(3);
    game.advance();
    expect(http.expectOne(ROOT + '/quizzes/daily/continue').request.body.confidence).toBeUndefined();
  });

  it('shares points, each bet and the daily streak without spoilers', () => {
    const answer = (correct: boolean, confidence?: number) => ({ question: { id: 'x', prompt: 'Secret answer?', league: 'nba', topic: 'awards', season: null, options: [] }, optionId: 'o', correct, confidence });
    const review = [answer(true, 3), answer(true, 1), answer(false, 2), answer(true, 3), answer(true)];
    const daily = shareText({ mode: 'daily', date: '2026-11-02', points: 9, maxPoints: 15, correctCount: 4, review, dailyStreak: 4 });
    expect(daily).toBe("Numbers Don't Lie · 2026-11-02\n9/15 pts · 4/5\n🟩3 🟩1 ⬜2 🟩3 🟩2\n🔥 4-day streak\nhttps://knowball.nathanzimmerman.com/numbers-dont-lie");
    expect(daily).not.toContain('Secret');
    expect(shareText({ mode: 'practice', date: null, points: 9, maxPoints: 15, correctCount: 4, review, dailyStreak: null })).not.toContain('streak');
  });
});
