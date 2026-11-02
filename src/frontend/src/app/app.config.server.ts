import { ApplicationConfig, mergeApplicationConfig } from '@angular/core';
import { provideServerRendering, withRoutes } from '@angular/ssr';
import { appConfig } from './app.config';
import { serverRoutes } from './app.routes.server';
import { RENDER_SERVER_KEY } from './core/interceptors/forwarded-for.interceptor';

const serverConfig: ApplicationConfig = {
  providers: [
    provideServerRendering(withRoutes(serverRoutes)),
    { provide: RENDER_SERVER_KEY, useFactory: () => process.env['RENDER_SERVER_KEY'] ?? '' },
  ],
};

export const config = mergeApplicationConfig(appConfig, serverConfig);
