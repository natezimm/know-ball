import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { PageMetaData, providePageMeta } from './page-meta';

@Component({ template: '' })
class Blank {}

const HOME = 'Daily sports trivia.';

describe('page meta', () => {
  let router: Router;

  beforeEach(() => {
    document.head.querySelectorAll('meta[name="description"], meta[name="robots"], link[rel="canonical"]').forEach((e) => e.remove());
    const description = document.createElement('meta');
    description.name = 'description';
    description.content = HOME;
    document.head.appendChild(description);

    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: '', component: Blank },
          { path: 'spordle', component: Blank, data: { description: 'Find the word.' } satisfies PageMetaData },
          { path: 'career-path/puzzle/:id', component: Blank, data: { noindex: true } satisfies PageMetaData },
          {
            path: 'mockups',
            data: { noindex: true } satisfies PageMetaData,
            children: [{ path: 'themes', component: Blank }],
          },
        ]),
        providePageMeta(),
      ],
    });
    router = TestBed.inject(Router);
  });

  const canonical = () => document.head.querySelector<HTMLLinkElement>('link[rel="canonical"]')?.href;
  const meta = (name: string) => document.head.querySelector<HTMLMetaElement>(`meta[name="${name}"]`)?.content;

  it('files each page under its own public address, without query or fragment', async () => {
    await router.navigateByUrl('/spordle?practice=1#board');

    expect(canonical()).toBe('https://knowball.nathanzimmerman.com/spordle');
    expect(meta('description')).toBe('Find the word.');
    expect(meta('robots')).toBeUndefined();
  });

  it("uses the home page's description where a route has none", async () => {
    await router.navigateByUrl('/spordle');
    await router.navigateByUrl('/');

    expect(canonical()).toBe('https://knowball.nathanzimmerman.com/');
    expect(meta('description')).toBe(HOME);
  });

  it('keeps archive puzzles and everything under the design lab out of search', async () => {
    await router.navigateByUrl('/career-path/puzzle/41');
    expect(meta('robots')).toBe('noindex');
    expect(canonical()).toBeUndefined();

    await router.navigateByUrl('/mockups/themes');
    expect(meta('robots')).toBe('noindex');

    await router.navigateByUrl('/spordle');
    expect(meta('robots')).toBeUndefined();
    expect(canonical()).toBe('https://knowball.nathanzimmerman.com/spordle');
  });
});
