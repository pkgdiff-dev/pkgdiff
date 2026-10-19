import { HttpErrorResponse } from '@angular/common/http';

export type LoadFailure = 'not-found' | 'error';

export function loadFailureOf(error: unknown): LoadFailure {
  return error instanceof HttpErrorResponse && error.status === 404 ? 'not-found' : 'error';
}

export function rejectionMessage(error: unknown, fallback: string): string {
  return error instanceof HttpErrorResponse && error.status === 400 ? apiErrorMessage(error, fallback) : fallback;
}

// `error` on the API's own responses, `detail` on ProblemDetails
export function apiErrorMessage(error: unknown, fallback: string): string {
  // Status 0 means no response; the browser's own text means nothing to a visitor
  if (!(error instanceof HttpErrorResponse) || error.status === 0) return fallback;

  const body: unknown = error.error;
  if (body && typeof body === 'object') {
    const fields = body as Record<string, unknown>;
    for (const key of ['error', 'detail', 'message']) {
      const value = fields[key];
      if (typeof value === 'string' && value.trim()) return value;
    }
  }
  return fallback;
}
