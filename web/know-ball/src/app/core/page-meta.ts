import { DOCUMENT, EnvironmentProviders, inject, provideEnvironmentInitializer } from '@angular/core';
import { Meta } from '@angular/platform-browser';
import { ActivatedRouteSnapshot, NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';

/** The public address search engines should file every page under. */
export const SITE_ORIGIN = 'https://knowball.nathanzimmerman.com';

/** Route `data` that describes a page to search engines. */
export interface PageMetaData {
  /** The page's search-result snippet; the home page's description when left out. */
  description?: string;
  /** Keeps the page and everything under it out of search results. */
  noindex?: boolean;
}

/**
 * Every route is served the same index.html, so this sets the canonical link, description
 * and robots tag for the page the router lands on. Search engines that run the app (Google
 * does) then file each game under its own address instead of the home page's, and skip the
 * design lab and archive puzzles.
 */
export function providePageMeta(): EnvironmentProviders {
  return provideEnvironmentInitializer(() => {
    const router = inject(Router);
    const meta = inject(Meta);
    const document = inject(DOCUMENT);
    const homeDescription = meta.getTag('name="description"')?.content ?? '';

    router.events.pipe(filter((e) => e instanceof NavigationEnd)).subscribe(() => {
      const data = pageData(router.routerState.snapshot.root);
      meta.updateTag({ name: 'description', content: data.description ?? homeDescription });

      let canonical = document.head.querySelector<HTMLLinkElement>('link[rel="canonical"]');
      if (data.noindex) {
        meta.updateTag({ name: 'robots', content: 'noindex' });
        canonical?.remove();
        return;
      }
      meta.removeTag('name="robots"');
      if (!canonical) {
        canonical = document.createElement('link');
        canonical.rel = 'canonical';
        document.head.appendChild(canonical);
      }
      const path = router.parseUrl(router.url).root.children['primary']?.segments.map((s) => s.path).join('/') ?? '';
      canonical.href = `${SITE_ORIGIN}/${path}`;
    });
  });
}

/** The deepest route's description; noindex if any route on the way down sets it. */
function pageData(route: ActivatedRouteSnapshot): PageMetaData {
  const result: PageMetaData = {};
  for (let r: ActivatedRouteSnapshot | null = route; r; r = r.firstChild) {
    const data = r.data as PageMetaData;
    if (data.description) result.description = data.description;
    if (data.noindex) result.noindex = true;
  }
  return result;
}
