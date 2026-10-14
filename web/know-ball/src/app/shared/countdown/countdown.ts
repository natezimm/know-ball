import { Component, DestroyRef, effect, inject, input, output, signal } from '@angular/core';

/** Time left until a moment, as 05:12:33, ticking once a second. Fires `done` when it gets there. */
@Component({
  selector: 'app-countdown',
  template: `<time role="timer">{{ text() }}</time>`,
  styles: `time { font-variant-numeric: tabular-nums; font-weight: 700; }`,
})
export class Countdown {
  /** ISO date-time. */
  readonly until = input.required<string>();
  readonly done = output<void>();

  protected readonly text = signal('');
  private timer?: ReturnType<typeof setInterval>;

  constructor() {
    effect(() => {
      const target = Date.parse(this.until());
      clearInterval(this.timer);
      if (Number.isNaN(target)) return;

      const tick = () => {
        const left = target - Date.now();
        this.text.set(formatCountdown(left));
        if (left <= 0) {
          clearInterval(this.timer);
          this.done.emit();
        }
      };
      tick();
      this.timer = setInterval(tick, 1000);
    });
    inject(DestroyRef).onDestroy(() => clearInterval(this.timer));
  }
}

/** 18753000 → "05:12:33". Never negative. */
export function formatCountdown(ms: number): string {
  const total = Math.max(0, Math.ceil(ms / 1000));
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${pad(Math.floor(total / 3600))}:${pad(Math.floor((total % 3600) / 60))}:${pad(total % 60)}`;
}
