import { TestBed } from '@angular/core/testing';
import { Attempt, LetterHint } from './spordle-game';
import { SpordleGrid } from './word-grid';

describe('SpordleGrid', () => {
  async function render(inputs: { attempts?: Attempt[]; currentGuess?: string; wordLength?: number; active?: boolean; hint?: LetterHint | null }) {
    const fixture = TestBed.createComponent(SpordleGrid);
    fixture.componentRef.setInput('attempts', inputs.attempts ?? []);
    fixture.componentRef.setInput('currentGuess', inputs.currentGuess ?? '');
    fixture.componentRef.setInput('wordLength', inputs.wordLength ?? 5);
    fixture.componentRef.setInput('active', inputs.active ?? true);
    fixture.componentRef.setInput('hint', inputs.hint ?? null);
    await fixture.whenStable();
    const rows = [...(fixture.nativeElement as HTMLElement).querySelectorAll('.row')];
    return rows.map((row) => ({ label: row.getAttribute('aria-label'), tiles: [...row.querySelectorAll('.tile')] }));
  }

  it('draws six rows of the chosen length', async () => {
    const rows = await render({ wordLength: 4 });
    expect(rows.length).toBe(6);
    expect(rows.every((row) => row.tiles.length === 4)).toBe(true);
  });

  it('colours a scored guess and describes it for screen readers', async () => {
    const [first] = await render({
      attempts: [{ word: 'punts', score: ['absent', 'present', 'absent', 'absent', 'correct'] }],
    });

    expect(first.tiles.map((t) => t.textContent)).toEqual(['p', 'u', 'n', 't', 's']);
    expect(first.tiles[1].classList.contains('present')).toBe(true);
    expect(first.tiles[4].classList.contains('correct')).toBe(true);
    expect(first.tiles.every((t) => t.classList.contains('scored'))).toBe(true);
    expect(first.label).toBe('Guess 1: p absent, u present, n absent, t absent, s correct');
  });

  it('shows the word being typed on the next row with a cursor after it', async () => {
    const rows = await render({
      attempts: [{ word: 'punts', score: ['absent', 'absent', 'absent', 'absent', 'absent'] }],
      currentGuess: 'bl',
    });

    const typing = rows[1].tiles;
    expect(typing.map((t) => t.textContent)).toEqual(['b', 'l', '', '', '']);
    expect(typing[0].classList.contains('filled')).toBe(true);
    expect(typing[2].classList.contains('cursor')).toBe(true);
    expect(typing[3].classList.contains('cursor')).toBe(false);
  });

  it('shows a hinted letter in its place until it is typed over', async () => {
    const hint = { position: 3, letter: 'i' };

    const before = (await render({ currentGuess: 'b', hint }))[0].tiles;
    expect(before[2].textContent).toBe('i');
    expect(before[2].classList.contains('ghost')).toBe(true);

    const after = (await render({ currentGuess: 'bla', hint }))[0].tiles;
    expect(after[2].textContent).toBe('a');
    expect(after[2].classList.contains('ghost')).toBe(false);
  });

  it('has no typing row once the game is over', async () => {
    const rows = await render({ currentGuess: 'bl', active: false });
    expect(rows[0].tiles.every((t) => t.textContent === '')).toBe(true);
  });
});
