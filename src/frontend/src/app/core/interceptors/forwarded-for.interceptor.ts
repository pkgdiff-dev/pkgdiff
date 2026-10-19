import { HttpInterceptorFn } from '@angular/common/http';
import { REQUEST, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

// Keeps rate limits and request logs per visitor when the render server calls the API
export const forwardedForInterceptor: HttpInterceptorFn = (req, next) => {
  const visitor = inject(REQUEST, { optional: true })?.headers.get('x-forwarded-for');

  if (!visitor || !req.url.startsWith(environment.apiUrl)) {
    return next(req);
  }

  return next(req.clone({ setHeaders: { 'X-Forwarded-For': visitor } }));
};
