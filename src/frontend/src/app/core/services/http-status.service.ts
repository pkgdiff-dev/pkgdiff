import { Injectable, RESPONSE_INIT, inject } from '@angular/core';

// The HTTP status of the page while it is rendered on the server, so a missing package or post is a
// real 404 and not a 200 that says "not found". In the browser there is no response to set.
@Injectable({ providedIn: 'root' })
export class HttpStatusService {
  private response = inject(RESPONSE_INIT, { optional: true });

  notFound(): void {
    if (this.response) {
      this.response.status = 404;
    }
  }
}
