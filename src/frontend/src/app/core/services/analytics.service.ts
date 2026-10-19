import { Injectable, PLATFORM_ID, inject } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { Router, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs/operators';
import { environment } from '../../../environments/environment';

type GtagArguments = [command: string, ...parameters: unknown[]];

interface AnalyticsWindow {
  dataLayer?: unknown[];
  gtag?: (...args: GtagArguments) => void;
}

@Injectable({ providedIn: 'root' })
export class AnalyticsService {
  private router = inject(Router);
  private isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  // Lives as long as the application, so the subscription is never torn down
  init(): void {
    if (!this.isBrowser || !environment.gaId) return;

    const gtag = this.loadScript(environment.gaId);

    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd)
    ).subscribe(event => {
      if (event.urlAfterRedirects.startsWith('/admin')) return;

      gtag('event', 'page_view', {
        page_path: event.urlAfterRedirects,
        page_location: window.location.href,
        page_title: document.title
      });
    });
  }

  private loadScript(gaId: string): (...args: GtagArguments) => void {
    const script = document.createElement('script');
    script.async = true;
    script.src = `https://www.googletagmanager.com/gtag/js?id=${gaId}`;
    document.head.appendChild(script);

    const analytics = window as unknown as AnalyticsWindow;
    const dataLayer = analytics.dataLayer ?? (analytics.dataLayer = []);
    // gtag.js reads the queue entries as the arguments objects the snippet pushes, not as arrays
    const gtag = function (..._args: GtagArguments) {
      dataLayer.push(arguments);
    };
    analytics.gtag = gtag;

    gtag('js', new Date());
    gtag('config', gaId, { send_page_view: false });
    return gtag;
  }
}
