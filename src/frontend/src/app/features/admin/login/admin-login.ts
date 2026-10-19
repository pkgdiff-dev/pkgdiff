import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { LucideAngularModule, Lock, User } from 'lucide-angular';
import { AuthService } from '../../../core/services/auth.service';
import { ToastService } from '../../../core/services/toast.service';
import { ThemeService } from '../../../core/services/theme.service';
import { LogoComponent } from '../../../shared/components/logo/logo';
import { GridPatternComponent } from '../../../shared/components/grid-pattern/grid-pattern';
import { CookieConsentComponent } from '../../../shared/components/cookie-consent/cookie-consent';
import { InputComponent } from '../../../shared/components/input/input.component';
import { ButtonComponent } from '../../../shared/components/button/button.component';

@Component({
  selector: 'app-admin-login',
  standalone: true,
  templateUrl: './admin-login.html',
  imports: [FormsModule, LucideAngularModule, LogoComponent, GridPatternComponent, CookieConsentComponent, InputComponent, ButtonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminLoginComponent {
  private auth = inject(AuthService);
  private router = inject(Router);
  private toast = inject(ToastService);

  readonly LockIcon = Lock;
  readonly UserIcon = User;

  username = signal('');
  password = signal('');
  loading = signal(false);

  constructor() {
    // This page has no layout around it, so nothing else creates the service that applies the stored theme
    inject(ThemeService);
  }

  handleLogin(): void {
    // Enter in the password field arrives twice: as the key handler and as the form's submit
    if (this.loading()) return;

    if (!this.username().trim() || !this.password().trim()) {
      this.toast.warning('Please fill in all fields.');
      return;
    }

    this.loading.set(true);
    this.auth.login(this.username(), this.password()).subscribe({
      next: (response) => {
        this.auth.setUser(response);
        this.router.navigate(['/admin/dashboard']);
      },
      error: () => {
        this.loading.set(false);
        this.toast.error('Invalid username or password.');
      },
    });
  }
}
