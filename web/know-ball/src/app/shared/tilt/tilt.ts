import { Directive, ElementRef, inject } from '@angular/core';

/**
 * Tilts a card toward the mouse and moves its foil glare with it, like turning a card in the light.
 * It only sets CSS variables (--mx, --my, --rx, --ry) and a `tilting` class; styles.css decides what they do,
 * and turns the tilt off for reduced motion. Touch and pen are left alone so scrolling never jiggles a card.
 */
@Directive({
  selector: '[kbTilt]',
  host: { '(pointermove)': 'move($event)', '(pointerleave)': 'reset()' },
})
export class Tilt {
  private readonly el: HTMLElement = inject(ElementRef).nativeElement;

  protected move(event: PointerEvent): void {
    if (event.pointerType !== 'mouse') return;
    const box = this.el.getBoundingClientRect();
    if (!box.width || !box.height) return;
    const x = Math.min(1, Math.max(0, (event.clientX - box.left) / box.width));
    const y = Math.min(1, Math.max(0, (event.clientY - box.top) / box.height));
    const style = this.el.style;
    style.setProperty('--mx', `${(x * 100).toFixed(1)}%`);
    style.setProperty('--my', `${(y * 100).toFixed(1)}%`);
    style.setProperty('--rx', `${((0.5 - y) * 9).toFixed(2)}deg`);
    style.setProperty('--ry', `${((x - 0.5) * 11).toFixed(2)}deg`);
    this.el.classList.add('tilting');
  }

  protected reset(): void {
    for (const name of ['--mx', '--my', '--rx', '--ry']) this.el.style.removeProperty(name);
    this.el.classList.remove('tilting');
  }
}
