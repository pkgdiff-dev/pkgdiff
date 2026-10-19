import { ChangeDetectionStrategy, Component, DestroyRef, PLATFORM_ID, signal, inject } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, NavigationStart, NavigationEnd, NavigationCancel, NavigationError } from '@angular/router';

@Component({
  selector: 'app-route-loader',
  standalone: true,
  imports: [],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (visible()) {
      <div class="route-overlay" [class.route-overlay--fade-out]="fadeOut()">
        <!-- Animated spinner -->
        <div class="route-spinner">
          <div class="route-spinner__ring"></div>
          <div class="route-spinner__ring route-spinner__ring--inner"></div>
          <div class="route-spinner__dot"></div>
        </div>
      </div>
    }
  `,
  styles: [`
    .route-overlay {
      position: fixed;
      inset: 0;
      z-index: 9999;
      display: flex;
      align-items: center;
      justify-content: center;
      background: rgba(10, 10, 10, 0.65);
      backdrop-filter: blur(6px);
      -webkit-backdrop-filter: blur(6px);
      animation: overlay-in 0.15s ease forwards;
    }

    .route-overlay--fade-out {
      animation: overlay-out 0.2s ease forwards;
    }

    @keyframes overlay-in {
      from { opacity: 0; }
      to   { opacity: 1; }
    }

    @keyframes overlay-out {
      from { opacity: 1; }
      to   { opacity: 0; }
    }

    /* ── Spinner ── */
    .route-spinner {
      position: relative;
      width: 52px;
      height: 52px;
    }

    .route-spinner__ring {
      position: absolute;
      inset: 0;
      border-radius: 50%;
      border: 2.5px solid transparent;
      border-top-color: #137fec;
      animation: spin 0.9s linear infinite;
    }

    .route-spinner__ring--inner {
      inset: 8px;
      border-top-color: #60b8ff;
      animation-duration: 0.6s;
      animation-direction: reverse;
      opacity: 0.7;
    }

    .route-spinner__dot {
      position: absolute;
      inset: 0;
      margin: auto;
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: #137fec;
      box-shadow: 0 0 10px 2px rgba(19, 127, 236, 0.6);
      animation: pulse 0.9s ease-in-out infinite alternate;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }

    @keyframes pulse {
      from { transform: scale(0.8); opacity: 0.6; }
      to   { transform: scale(1.2); opacity: 1; }
    }
  `]
})
export class RouteLoaderComponent {
  private router = inject(Router);

  visible = signal(false);
  fadeOut = signal(false);
  private hideTimer: ReturnType<typeof setTimeout> | null = null;
  private showTime = 0;
  private readonly MIN_DISPLAY_MS = 350;

  constructor() {
    // On a server-rendered page the overlay would only hide content until hydration
    if (!isPlatformBrowser(inject(PLATFORM_ID))) return;

    inject(DestroyRef).onDestroy(() => {
      if (this.hideTimer) clearTimeout(this.hideTimer);
    });

    this.router.events.pipe(takeUntilDestroyed()).subscribe(event => {
      if (event instanceof NavigationStart) {
        if (this.hideTimer) { clearTimeout(this.hideTimer); this.hideTimer = null; }
        this.fadeOut.set(false);
        this.showTime = Date.now();
        this.visible.set(true);
      } else if (
        event instanceof NavigationEnd ||
        event instanceof NavigationCancel ||
        event instanceof NavigationError
      ) {
        const elapsed = Date.now() - this.showTime;
        const remaining = Math.max(0, this.MIN_DISPLAY_MS - elapsed);
        this.hideTimer = setTimeout(() => {
          this.fadeOut.set(true);
          setTimeout(() => { this.visible.set(false); this.fadeOut.set(false); }, 220);
          this.hideTimer = null;
        }, remaining);
      }
    });
  }

}
