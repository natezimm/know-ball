import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { useMemoryStorage } from './memory-storage';
import { LetterJacket, shareResult } from './letter-jacket';

describe('Letter Jacket page', () => {
  let fixture: ComponentFixture<LetterJacket>;
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
    fixture = TestBed.createComponent(LetterJacket);
    http = TestBed.inject(HttpTestingController);
    el = fixture.nativeElement;
    http.expectOne('/api/letter-jacket/games').flush({ gameId: 'g1', wordLength: 5, maxAttempts: 6 });
    await fixture.whenStable();
  });

  afterEach(() => http.verify());

  it('starts a game and shows the board, keyboard and hint buttons', () => {
    expect(el.querySelector('h1')?.textContent).toBe('Letter Jacket');
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
    const stats = el.querySelector<HTMLButtonElement>('[aria-label="Stats"]')!;

    'blit'.split('').forEach((letter) => press(letter));
    expect(press('Enter', stats).defaultPrevented).toBe(false);
    http.expectNone('/api/letter-jacket/games/g1/guesses');

    press('z');
    expect(press('Enter', stats).defaultPrevented).toBe(true);
    http.expectOne('/api/letter-jacket/games/g1/guesses').flush({ valid: false });
  });

  it('starts a new game when the word length changes', async () => {
    const six = el.querySelector<HTMLButtonElement>('[aria-label="6 letters"]')!;
    six.click();

    const request = http.expectOne('/api/letter-jacket/games');
    expect(request.request.body).toEqual({ wordLength: 6 });
    request.flush({ gameId: 'g2', wordLength: 6, maxAttempts: 6 });
    await fixture.whenStable();

    expect(el.querySelectorAll('.row.active .tile').length).toBe(6);
    expect(six.getAttribute('aria-pressed')).toBe('true');
  });
});

describe('Letter Jacket share text', () => {
  it('uses Know Ball marks rather than coloured squares', () => {
    const text = shareResult([
      { word: 'blitz', score: ['correct', 'present', 'absent', 'absent', 'correct'] },
      { word: 'bunts', score: ['correct', 'correct', 'correct', 'correct', 'correct'] },
    ], true, 5);

    expect(text).toBe([
      'Know Ball · Letter Jacket 2/6 (5 letters)',
      '● ◐ ○ ○ ●',
      '● ● ● ● ●',
      'https://knowball.nathanzimmerman.com/letter-jacket',
    ].join('\n'));
    expect(text).not.toMatch(/[\u{1F7E9}\u{1F7E8}\u2B1C]/u);
  });

  it('marks a loss with an X', () => {
    const miss = { word: 'bunts', score: Array(5).fill('absent') };
    expect(shareResult(Array(6).fill(miss), false, 5).split('\n')[0]).toBe('Know Ball · Letter Jacket X/6 (5 letters)');
  });
});
