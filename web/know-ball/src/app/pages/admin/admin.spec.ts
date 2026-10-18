import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AdminOverview, CuratedPuzzle } from '../../core/admin-api';
import { Admin } from './admin';

const overview: AdminOverview = {
  today: '2026-10-13',
  games: [
    { game: 'scouting-report', name: 'Scouting Report', generated: true, daysQueued: 30, queuedThrough: '2026-11-11' },
    { game: 'whos-that', name: "Who's That Sportsman?", generated: false, daysQueued: 1, queuedThrough: '2026-10-13' },
  ],
  data: { players: 5533, activePlayers: 629, playersWithHeadshots: 5199, teams: 99 },
  imports: [{ source: 'espn', status: 'Failed', finishedAt: null, error: 'timeout' }],
};

const puzzle = (id: number, state: CuratedPuzzle['state'], name: string, date: string | null = null): CuratedPuzzle => ({
  id, state, date, number: date ? id : null, playerId: id, playerName: name, year: 2000, team: 'Team USA', caption: null,
  silhouetteUrl: `/photos/s${id}.png`, blurredUrl: `/photos/b${id}.jpg`, photoUrl: `/photos/p${id}.jpg`,
});

describe('Admin', () => {
  let fixture: ComponentFixture<Admin>;
  let http: HttpTestingController;
  let el: HTMLElement;

  beforeEach(async () => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    fixture = TestBed.createComponent(Admin);
    http = TestBed.inject(HttpTestingController);
    el = fixture.nativeElement;
    await fixture.whenStable();
  });

  afterEach(() => http.verify());

  async function unlock(puzzles: CuratedPuzzle[] = []) {
    el.querySelector<HTMLInputElement>('#admin-key')!.value = 'the-key';
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    const check = http.expectOne('/api/admin/overview');
    expect(check.request.headers.get('X-Admin-Key')).toBe('the-key');
    check.flush(overview);
    http.expectOne('/api/admin/whos-that/puzzles').flush(puzzles);
    await fixture.whenStable();
  }

  it('asks for the key first and says so when it is wrong', async () => {
    expect(el.querySelector('.gate')).not.toBeNull();

    el.querySelector<HTMLInputElement>('#admin-key')!.value = 'guess';
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    http.expectOne('/api/admin/overview').flush({ error: 'Wrong admin key.' }, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();

    expect(el.querySelector('.kb-toast')?.textContent).toBe('Wrong admin key.');
    expect(el.querySelector('.gate')).not.toBeNull();
  });

  it('explains when the server has the admin area switched off', async () => {
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    http.expectOne('/api/admin/overview').flush('', { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();

    expect(el.querySelector('.kb-toast')?.textContent).toContain('switched off');
  });

  it('shows the queues, flags a short one and a failed import', async () => {
    await unlock();

    const rows = [...el.querySelectorAll('tbody tr')];
    expect(rows.map((r) => r.children[2].textContent)).toEqual(['30', '1']);
    expect(rows.map((r) => r.classList.contains('low'))).toEqual([false, true]);
    expect(el.querySelector('.fine .bad')?.textContent).toContain('espn: Failed');
  });

  it('groups puzzles and offers each the actions its state allows', async () => {
    await unlock([puzzle(3, 'draft', 'Tim Duncan'), puzzle(4, 'scheduled', 'Kobe Bryant', '2026-10-14'), puzzle(5, 'scheduled', 'Ray Allen', '2026-10-15'), puzzle(1, 'published', 'Vince Carter', '2026-10-13')]);

    const sections = [...el.querySelectorAll('section')].slice(2);
    const buttons = (i: number) => [...sections[i].querySelectorAll<HTMLButtonElement>('.acts button')].map((b) => `${b.textContent?.trim()}${b.disabled ? ' (off)' : ''}`);
    expect(sections.map((s) => s.querySelector('h2')?.textContent?.replace(/\s+/g, ' ').trim())).toEqual(['Drafts 1', 'Coming up 2', 'Published 1']);
    expect(buttons(0)).toEqual(['Preview', 'Edit', 'Schedule', 'Delete']);
    expect(buttons(1)).toEqual(['Preview', 'Earlier (off)', 'Later', 'Unschedule', 'Preview', 'Earlier', 'Later (off)', 'Unschedule']);
    expect(buttons(2)).toEqual(['Preview']); // a live puzzle is locked
  });

  it('schedules a draft on the first free day when no date is picked, then reloads', async () => {
    await unlock([puzzle(3, 'draft', 'Tim Duncan')]);

    el.querySelector<HTMLButtonElement>('.acts .primary')!.click();
    const request = http.expectOne('/api/admin/whos-that/puzzles/3/schedule');
    expect(request.request.body).toEqual({});
    expect(request.request.headers.get('X-Admin-Key')).toBe('the-key');
    request.flush(puzzle(3, 'scheduled', 'Tim Duncan', '2026-10-14'));

    http.expectOne('/api/admin/overview').flush(overview);
    http.expectOne('/api/admin/whos-that/puzzles').flush([puzzle(3, 'scheduled', 'Tim Duncan', '2026-10-14')]);
    await fixture.whenStable();

    expect(el.querySelector('.when')?.textContent).toContain('Oct 14');
  });

  it('will not make a puzzle without a player picked', async () => {
    await unlock();

    el.querySelector('form.new')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(el.querySelector('.kb-toast')?.textContent).toBe('Pick the player in the photo from the list.');
    http.expectNone('/api/admin/whos-that/puzzles');
  });

  it('forgets the key when locked', async () => {
    await unlock();
    expect(sessionStorage.getItem('knowball.adminKey')).toBe('the-key');

    [...el.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.trim() === 'Lock')!.click();
    await fixture.whenStable();

    expect(sessionStorage.getItem('knowball.adminKey')).toBeNull();
    expect(el.querySelector('.gate')).not.toBeNull();
  });
  it('prefills a draft and saves metadata without requiring replacement images', async () => {
    await unlock([puzzle(3, 'draft', 'Tim Duncan')]);
    [...el.querySelectorAll<HTMLButtonElement>('button')].find(b => b.textContent?.trim() === 'Edit')!.click();
    await fixture.whenStable();
    const form = el.querySelector<HTMLFormElement>('form.new')!;
    expect(form.querySelector<HTMLInputElement>('[name="year"]')!.value).toBe('2000');
    expect(form.querySelector<HTMLInputElement>('[name="photo"]')!.required).toBe(false);
    form.querySelector<HTMLInputElement>('[name="caption"]')!.value = 'Corrected story';
    form.dispatchEvent(new Event('submit'));
    const req = http.expectOne('/api/admin/whos-that/puzzles/3');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.get('caption')).toBe('Corrected story');
    expect(req.request.body.get('playerId')).toBe('3');
    req.flush(puzzle(3, 'draft', 'Tim Duncan'));
    http.expectOne('/api/admin/overview').flush(overview);
    http.expectOne('/api/admin/whos-that/puzzles').flush([puzzle(3, 'draft', 'Tim Duncan')]);
    await fixture.whenStable();
    expect(form.isConnected).toBe(false);
    expect(el.textContent).toContain('New Who');
  });

  it('previews stages through the admin API and clears a reveal before the next request', async () => {
    await unlock([puzzle(3, 'draft', 'Tim Duncan')]);
    el.querySelector<HTMLButtonElement>('.preview-button')!.click();
    const req = http.expectOne(r => r.url.endsWith('/3/preview'));
    expect(req.request.headers.get('X-Admin-Key')).toBe('the-key');
    expect(req.request.params.get('reveal')).toBe('false');
    req.flush({ clues: { silhouetteUrl: '/photos/s3.png', year: null, team: null, blurredUrl: null }, answer: null });
    await fixture.whenStable();
    const preview = el.querySelector('.preview')!;
    expect(preview.textContent).toContain('Locked until 1 miss');
    [...preview.querySelectorAll<HTMLButtonElement>('button')].find(b => b.textContent === 'Reveal')!.click();
    const reveal = http.expectOne(r => r.url.endsWith('/3/preview'));
    expect(reveal.request.params.get('reveal')).toBe('true');
    reveal.flush({ clues: null, answer: { name: 'Tim Duncan', photoUrl: '/photos/p3.jpg', caption: 'The story', attribution: { credit: 'Credit', sourceUrl: 'https://example.com/photo' } } });
    await fixture.whenStable();
    expect(preview.querySelector('a')?.href).toBe('https://example.com/photo');
    [...preview.querySelectorAll<HTMLButtonElement>('button')].find(b => b.textContent === 'Silhouette')!.click();
    await fixture.whenStable();
    expect(preview.querySelector('a')).toBeNull();
    http.expectOne(r => r.url.endsWith('/3/preview')).flush({ clues: null, answer: null });
    await fixture.whenStable();
  });

});
