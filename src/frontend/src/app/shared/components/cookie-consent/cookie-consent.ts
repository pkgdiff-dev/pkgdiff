import { ChangeDetectionStrategy, Component, DestroyRef, afterNextRender, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BrowserStorage } from '../../../core/services/browser-storage.service';

const CONSENT_KEY = 'pkgdiff-cookie-consent';

@Component({
  selector: 'app-cookie-consent',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './cookie-consent.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CookieConsentComponent {
  private storage = inject(BrowserStorage);

  visible = signal<boolean>(false);

  constructor() {
    const destroyRef = inject(DestroyRef);

    // Only a browser knows whether the visitor has already chosen
    afterNextRender(() => {
      if (this.storage.get(CONSENT_KEY)) return;

      // Show banner after a short delay for better UX
      const timer = setTimeout(() => this.visible.set(true), 1000);
      destroyRef.onDestroy(() => clearTimeout(timer));
    });
  }

  accept() {
    this.storage.set(CONSENT_KEY, 'accepted');
    this.visible.set(false);
  }

  decline() {
    this.storage.set(CONSENT_KEY, 'declined');
    this.visible.set(false);
  }
}
