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
    expect([...el.querySelectorAll('nav a')].map((a) => a.textContent)).toEqual(['Games', 'Mockups']);
  });
});
