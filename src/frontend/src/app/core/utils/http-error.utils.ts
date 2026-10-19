import { HttpErrorResponse } from '@angular/common/http';

export type LoadFailure = 'not-found' | 'error';

// A missing item and a failed request are different things to show
export function loadFailureOf(error: unknown): LoadFailure {
  return error instanceof HttpErrorResponse && error.status === 404 ? 'not-found' : 'error';
}

// Why the API turned a request down (a taken name, a weak password); any other failure keeps the caller's words
export function rejectionMessage(error: unknown, fallback: string): string {
  return error instanceof HttpErrorResponse && error.status === 400 ? apiErrorMessage(error, fallback) : fallback;
}

// The message the API sent with a failure: `error` on its own responses, `detail` on ProblemDetails
export function apiErrorMessage(error: unknown, fallback: string): string {
  // Status 0: no answer at all; what the browser says about it ("Failed to fetch") means nothing to a visitor
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
