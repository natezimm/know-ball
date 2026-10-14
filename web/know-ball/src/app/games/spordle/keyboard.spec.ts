import { TestBed } from '@angular/core/testing';
import { SpordleKeyboard } from './keyboard';

describe('SpordleKeyboard', () => {
  async function render() {
    const fixture = TestBed.createComponent(SpordleKeyboard);
    fixture.componentRef.setInput('statuses', { e: 'correct', u: 'present', z: 'absent' });
    await fixture.whenStable();
    const keys = [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('.key')];
    const key = (label: string) => keys.find((k) => k.getAttribute('aria-label')?.startsWith(label))!;
    return { fixture, keys, key };
  }

  it('has the 26 letters plus Enter and Backspace', async () => {
    const { keys } = await render();
    expect(keys.length).toBe(28);
  });

  it('emits the key that was clicked', async () => {
    const { fixture, key } = await render();
    const pressed: string[] = [];
    fixture.componentInstance.pressed.subscribe((k) => pressed.push(k));

    key('q').click();
    key('Enter').click();
    key('Backspace').click();

    expect(pressed).toEqual(['q', 'Enter', 'Backspace']);
  });

  it('colours keys by their best result and says so in the label', async () => {
    const { key } = await render();

    expect(key('e').classList.contains('correct')).toBe(true);
    expect(key('u').classList.contains('present')).toBe(true);
    expect(key('z').classList.contains('absent')).toBe(true);
    expect(key('e').getAttribute('aria-label')).toBe('e, correct');
    expect(key('q').getAttribute('aria-label')).toBe('q');
  });

  it('does not take focus when clicked with a pointer', async () => {
    const { key } = await render();
    const event = new MouseEvent('mousedown', { bubbles: true, cancelable: true });
    key('q').dispatchEvent(event);
    expect(event.defaultPrevented).toBe(true);
  });
});
