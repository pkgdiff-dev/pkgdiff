import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideClientHydration, withEventReplay } from '@angular/platform-browser';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';

import { routes } from './app.routes';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { errorInterceptor } from './core/interceptors/error.interceptor';
import { forwardedForInterceptor } from './core/interceptors/forwarded-for.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    // What the server rendered is taken over, not rendered again; API answers travel with the page
    provideClientHydration(withEventReplay()),
    provideHttpClient(withFetch(), withInterceptors([forwardedForInterceptor, authInterceptor, errorInterceptor])),
  ]
};
