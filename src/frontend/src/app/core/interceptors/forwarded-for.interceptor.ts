import { HttpInterceptorFn } from '@angular/common/http';
import { REQUEST, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

// During a server render the API is called by the render server, on behalf of the visitor whose page
// it is. Passing the visitor's address along keeps rate limits and request logs per visitor instead of
// charging every render to the server. In the browser there is no incoming request and nothing happens.
export const forwardedForInterceptor: HttpInterceptorFn = (req, next) => {
  const visitor = inject(REQUEST, { optional: true })?.headers.get('x-forwarded-for');

  if (!visitor || !req.url.startsWith(environment.apiUrl)) {
    return next(req);
  }

  return next(req.clone({ setHeaders: { 'X-Forwarded-For': visitor } }));
};
