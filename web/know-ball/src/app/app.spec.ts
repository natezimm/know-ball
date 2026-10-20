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
    expect(el.querySelector('.footer a[href="/mockups"]')?.textContent).toBe('Design Lab');
  });
});
