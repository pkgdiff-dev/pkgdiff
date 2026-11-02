import { Injectable, RESPONSE_INIT, inject } from '@angular/core';

const RETRY_AFTER_SECONDS = '120';

// Lets a server render answer a missing or failed page with its real status instead of a 200
@Injectable({ providedIn: 'root' })
export class HttpStatusService {
  private response = inject(RESPONSE_INIT, { optional: true });

  notFound(): void {
    if (this.response) {
      this.response.status = 404;
    }
  }

  serviceUnavailable(): void {
    if (this.response) {
      const headers = new Headers(this.response.headers);
      headers.set('Retry-After', RETRY_AFTER_SECONDS);
      this.response.status = 503;
      this.response.headers = headers;
    }
  }
}
