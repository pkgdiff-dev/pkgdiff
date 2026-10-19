import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth.service';

// A route without data.permission is open to every signed-in user
export const permissionGuard: CanActivateFn = (route) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const required: unknown = route.data['permission'];
  if (typeof required !== 'string' || auth.hasPermission(required)) {
    return true;
  }

  // The dashboard needs no permission, so this can never loop
  return router.createUrlTree(['/admin/dashboard']);
};
