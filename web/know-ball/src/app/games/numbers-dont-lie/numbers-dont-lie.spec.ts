import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { EnvironmentInjector, createEnvironmentInjector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { NumbersDontLie } from './numbers-dont-lie';
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
});
