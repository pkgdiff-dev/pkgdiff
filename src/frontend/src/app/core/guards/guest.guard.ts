import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.hasValidSession()) {
    // Already logged in, redirect to dashboard
    return router.createUrlTree(['/admin/dashboard']);
  }

  // Not logged in, allow access to login page
  return true;
};
