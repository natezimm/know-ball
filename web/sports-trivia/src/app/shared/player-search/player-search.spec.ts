import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PlayerSearch } from './player-search';
import { PlayerSearchResult } from '../../core/player-search.service';

const rows: PlayerSearchResult[] = [
  { id: 1, fullName: 'LeBron James', position: 'F', teamAbbreviation: 'LAL', teamName: 'Los Angeles Lakers', headshotUrl: null },
  { id: 2, fullName: 'Bronny James', position: 'G', teamAbbreviation: 'LAL', teamName: 'Los Angeles Lakers', headshotUrl: null },
];

const tick = () => new Promise((r) => setTimeout(r, 0));

describe('PlayerSearch', () => {
  let fixture: ComponentFixture<PlayerSearch>;
  let http: HttpTestingController;
  let input: HTMLInputElement;

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    fixture = TestBed.createComponent(PlayerSearch);
    fixture.componentRef.setInput('debounceMs', 0);
    http = TestBed.inject(HttpTestingController);
    await fixture.whenStable();
    input = fixture.nativeElement.querySelector('input');
  });

  afterEach(() => http.verify());

  async function type(text: string) {
    input.value = text;
    input.dispatchEvent(new Event('input'));
    await tick();
  }

  async function key(k: string) {
    input.dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true }));
    await fixture.whenStable();
  }

  it('does not search for a single character', async () => {
    await type('j');
    http.expectNone((r) => r.url === '/api/players/search');
  });

  it('shows results and picks with the keyboard', async () => {
    const picked: PlayerSearchResult[] = [];
    fixture.componentInstance.selected.subscribe((p) => picked.push(p));

    await type('jam');
    http.expectOne((r) => r.url === '/api/players/search' && r.params.get('q') === 'jam').flush(rows);
    await fixture.whenStable();

    const options = fixture.nativeElement.querySelectorAll('[role="option"]');
    expect(options.length).toBe(2);
    expect(input.getAttribute('aria-expanded')).toBe('true');

    await key('ArrowDown');
    await key('Enter');

    expect(picked.map((p) => p.fullName)).toEqual(['Bronny James']);
    expect(input.value).toBe('');
    expect(fixture.nativeElement.querySelectorAll('[role="option"]').length).toBe(0);
  });

  it('says so when nothing matches', async () => {
    await type('zzz');
    http.expectOne((r) => r.url === '/api/players/search').flush([]);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('No players match');
  });

  it('shows an error when the API fails', async () => {
    await type('cur');
    http.expectOne((r) => r.url === '/api/players/search').flush('boom', { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
  });

  it('asks for retired players only when told to, and shows their years in place of a team', async () => {
    await type('cart');
    const active = http.expectOne((r) => r.url === '/api/players/search');
    expect(active.request.params.has('includeInactive')).toBe(false);
    active.flush([]);

    fixture.componentRef.setInput('includeInactive', true);
    await type('carte');
    const all = http.expectOne((r) => r.url === '/api/players/search');
    expect(all.request.params.get('includeInactive')).toBe('true');
    all.flush([{ id: 9, fullName: 'Vince Carter', position: 'G-F', teamAbbreviation: null, teamName: null, headshotUrl: null, isActive: false, years: '1998–2020' }]);
    await fixture.whenStable();

    const option = fixture.nativeElement.querySelector('[role="option"]');
    expect([...option.querySelectorAll('.meta span')].map((span: Element) => span.textContent)).toEqual(['1998–2020', 'G-F']);
    expect(option.querySelector('app-avatar').textContent.trim()).toBe('VC');
  });
});
