import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { gameState, guess } from '../../core/testing';
import { WhosThat, ladder } from './whos-that';

const clues = (misses: number) => ({
  silhouetteUrl: '/photos/outline.png',
  year: misses >= 1 ? 2000 : null,
  team: misses >= 2 ? 'Team USA' : null,
  blurredUrl: misses >= 3 ? '/photos/blur.jpg' : null,
});

describe("Who's That Sportsman?", () => {
  let fixture: ComponentFixture<WhosThat>;
  let http: HttpTestingController;
  let el: HTMLElement;

  async function open(overrides = {}) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    fixture = TestBed.createComponent(WhosThat);
    http = TestBed.inject(HttpTestingController);
    el = fixture.nativeElement;
    await fixture.whenStable();
    http.expectOne('/api/whos-that/daily').flush(gameState({ game: 'whos-that', maxGuesses: 5, clues: clues(0), ...overrides }));
    await fixture.whenStable();
  }

  const stageImage = () => el.querySelector('.stage img')?.getAttribute('src');
  const rungs = () => [...el.querySelectorAll('.ladder li')].map((li) => `${li.classList.contains('done') ? '✓' : '·'} ${li.querySelector('em')?.textContent}`);

  afterEach(() => http.verify());

  it('opens on the outline with the rest of the ladder locked', async () => {
    await open();

    expect(el.querySelector('h1')?.textContent).toBe("Who's That?");
    expect(el.querySelector('h1')?.getAttribute('aria-label')).toBe("Who's That Sportsman?");
    expect(stageImage()).toBe('/photos/outline.png');
    expect(rungs()).toEqual(['✓ Shown', '· After 1 miss', '· After 2 misses', '· After 3 misses']);
    expect(el.querySelector('.views')).toBeNull();
  });

  it('fills in the year and team as wrong guesses release them', async () => {
    await open({ clues: clues(2), guesses: [guess('Tracy McGrady'), guess('Allen Iverson')] });

    expect(rungs()).toEqual(['✓ Shown', '✓ 2000', '✓ Team USA', '· After 3 misses']);
    expect(stageImage()).toBe('/photos/outline.png');
    expect([...el.querySelectorAll('.misses s')].map((s) => s.textContent)).toEqual(['Allen Iverson', 'Tracy McGrady']);
  });

  it('puts the blurred photo on stage when it is released, and lets the player flip back', async () => {
    await open({ clues: clues(3), guesses: [guess('A'), guess('Bb'), guess('Ccc')] });

    expect(stageImage()).toBe('/photos/blur.jpg');
    const [outline, blurred] = [...el.querySelectorAll<HTMLButtonElement>('.views button')];
    expect(blurred.getAttribute('aria-pressed')).toBe('true');

    outline.click();
    await fixture.whenStable();
    expect(stageImage()).toBe('/photos/outline.png');
    expect(outline.getAttribute('aria-pressed')).toBe('true');
  });

  it('shows the photo and its story at the end', async () => {
    await open({
      status: 'won',
      clues: clues(3),
      guesses: [guess('Vince Carter', true)],
      answer: { playerId: 1, name: 'Vince Carter', headshotUrl: null, photoUrl: '/photos/full.jpg', caption: 'The dunk', year: 2000, team: 'Team USA' },
    });

    expect(stageImage()).toBe('/photos/full.jpg');
    expect(el.querySelector('figcaption')?.textContent).toBe('The dunk');
    expect(el.querySelector('.facts')?.textContent).toBe('Team USA, 2000');
    expect(el.querySelector('.views')).toBeNull();
  });

  it('describes the ladder for a puzzle with nothing released', () => {
    expect(ladder(null).map((r) => r.value)).toEqual(['Shown', null, null, null]);
  });
});
