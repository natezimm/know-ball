import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { useMemoryStorage } from './memory-storage';
import { Spordle } from './spordle';

describe('Spordle page', () => {
  let fixture: ComponentFixture<Spordle>;
  let http: HttpTestingController;
  let el: HTMLElement;

  const press = (key: string, target: EventTarget = window) => {
    const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
    target.dispatchEvent(event);
    return event;
  };
  const typedRow = () => [...el.querySelectorAll('.row.active .tile')].map((t) => t.textContent).join('');

  beforeEach(async () => {
    useMemoryStorage();
    TestBed.configureTestingModule({ providers: [provideHttpClient(withXhr()), provideHttpClientTesting()] });
    fixture = TestBed.createComponent(Spordle);
    http = TestBed.inject(HttpTestingController);
    el = fixture.nativeElement;
    http.expectOne('/api/spordle/games').flush({ gameId: 'g1', wordLength: 5, maxAttempts: 6 });
    await fixture.whenStable();
  });

  afterEach(() => http.verify());

  it('starts a game and shows the board, keyboard and hint buttons', () => {
    expect(el.querySelector('h1')?.textContent).toBe('Spordle');
    expect(el.querySelectorAll('.row').length).toBe(6);
    expect(el.querySelectorAll('.key').length).toBe(28);
    expect(el.querySelector('.status')?.textContent).toBe('Guess 1 of 6');
    expect([...el.querySelectorAll('.hint')].map((b) => b.textContent?.trim())).toEqual(['Category', 'Letter']);
  });

  it('types from the physical keyboard, ignoring shortcuts', async () => {
    press('b');
    press('l');
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'r', metaKey: true }));
    await fixture.whenStable();

    expect(typedRow()).toBe('bl');
  });

  it('types from the on-screen keyboard', async () => {
    const key = [...el.querySelectorAll<HTMLButtonElement>('.key')].find((k) => k.textContent?.trim() === 'q')!;
    key.click();
    await fixture.whenStable();

    expect(typedRow()).toBe('q');
  });

  it('lets Enter activate a focused button until a full word is typed', async () => {
    const stats = [...el.querySelectorAll<HTMLButtonElement>('.btn')].find((b) => b.textContent?.trim() === 'Stats')!;

    'blit'.split('').forEach((letter) => press(letter));
    expect(press('Enter', stats).defaultPrevented).toBe(false);
    http.expectNone('/api/spordle/games/g1/guesses');

    press('z');
    expect(press('Enter', stats).defaultPrevented).toBe(true);
    http.expectOne('/api/spordle/games/g1/guesses').flush({ valid: false });
  });

  it('starts a new game when the word length changes', async () => {
    const six = el.querySelector<HTMLButtonElement>('[aria-label="6 letters"]')!;
    six.click();

    const request = http.expectOne('/api/spordle/games');
    expect(request.request.body).toEqual({ wordLength: 6 });
    request.flush({ gameId: 'g2', wordLength: 6, maxAttempts: 6 });
    await fixture.whenStable();

    expect(el.querySelectorAll('.row.active .tile').length).toBe(6);
    expect(six.getAttribute('aria-pressed')).toBe('true');
  });
});
