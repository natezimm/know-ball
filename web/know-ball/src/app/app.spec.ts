import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';

describe('App', () => {
  it('renders the brand and navigation', async () => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.brand')?.textContent).toContain('KNOW BALL');
    expect([...el.querySelectorAll('.bar nav a')].map((a) => a.textContent)).toEqual(['Games']);
    expect(el.querySelector('.footer a[href="https://www.nathanzimmerman.com/"]')?.textContent).toBe('Nathan Zimmerman');
    expect(el.querySelector('.footer a[href="https://github.com/natezimm/know-ball"]')).not.toBeNull();
    expect([...el.querySelectorAll('.footer nav a')].map((a) => a.textContent)).toEqual(['GitHub']);
    expect(el.querySelector('.footer .disclaimer')?.textContent).toBe('Not affiliated with or endorsed by the NBA, NFL, MLB or NHL.');
  });

  it('offers a skip link that moves focus past the header without leaving the page', async () => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const el: HTMLElement = fixture.nativeElement;
    document.body.appendChild(el);

    const skip = el.querySelector<HTMLAnchorElement>('a.skip')!;
    expect(el.querySelector('a, button')).toBe(skip);
    const click = new MouseEvent('click', { bubbles: true, cancelable: true });
    skip.dispatchEvent(click);

    expect(click.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(el.querySelector('main'));
    el.remove();
  });
});
