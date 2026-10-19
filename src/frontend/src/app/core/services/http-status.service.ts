import { Injectable, RESPONSE_INIT, inject } from '@angular/core';

// Lets a server render answer a missing page with a real 404 instead of a 200
@Injectable({ providedIn: 'root' })
export class HttpStatusService {
  private response = inject(RESPONSE_INIT, { optional: true });

  notFound(): void {
    if (this.response) {
      this.response.status = 404;
    }
  }
}
