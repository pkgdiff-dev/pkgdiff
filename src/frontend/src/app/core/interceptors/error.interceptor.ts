import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { PLATFORM_ID, inject } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { catchError, throwError } from 'rxjs';
import { ToastService } from '../services/toast.service';

// Says what no caller can say better: the server cannot be reached, or it is refusing requests for now.
// Every other failure stays with the caller, which knows what was being loaded and resets its own state.
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const toast = inject(ToastService);
  const isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  return next(req).pipe(
    catchError((error: unknown) => {
      if (isBrowser && error instanceof HttpErrorResponse) {
        if (error.status === 0) {
          toast.error('Cannot reach the server. Check your connection and try again.');
        } else if (error.status === 429) {
          toast.error('Too many requests. Please wait a moment and try again.');
        }
      }
      return throwError(() => error);
    }),
  );
};
