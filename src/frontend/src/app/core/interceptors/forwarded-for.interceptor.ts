import { HttpInterceptorFn } from '@angular/common/http';
import { InjectionToken, REQUEST, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

// Provided only on the render server, so the key never reaches a browser bundle
export const RENDER_SERVER_KEY = new InjectionToken<string>('RENDER_SERVER_KEY');

// Keeps rate limits and request logs per visitor when the render server calls the API
export const forwardedForInterceptor: HttpInterceptorFn = (req, next) => {
  const visitor = inject(REQUEST, { optional: true })?.headers.get('x-forwarded-for');
  const renderServerKey = inject(RENDER_SERVER_KEY, { optional: true });

  if (!req.url.startsWith(environment.apiUrl)) {
    return next(req);
  }

  const headers: Record<string, string> = {};
  if (visitor) headers['X-Forwarded-For'] = visitor;
  if (renderServerKey) headers['X-Render-Server-Key'] = renderServerKey;

  return Object.keys(headers).length > 0 ? next(req.clone({ setHeaders: headers })) : next(req);
};
