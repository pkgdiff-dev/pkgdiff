import { Directive, TemplateRef, ViewContainerRef, effect, inject, input, untracked } from '@angular/core';
import { AuthService } from '../../core/services/auth.service';

// Renders its content only for a user who holds the permission: *appHasPermission="'Permissions.Users.Edit'"
@Directive({
  selector: '[appHasPermission]',
  standalone: true,
})
export class HasPermissionDirective {
  private templateRef = inject<TemplateRef<unknown>>(TemplateRef);
  private viewContainer = inject(ViewContainerRef);
  private auth = inject(AuthService);

  readonly appHasPermission = input.required<string>();

  private shown = false;

  constructor() {
    effect(() => {
      // Follows both the permission asked for and the user who is signed in
      this.auth.currentUser();
      const allowed = this.auth.hasPermission(this.appHasPermission());
      untracked(() => this.render(allowed));
    });
  }

  private render(allowed: boolean): void {
    if (allowed === this.shown) return;

    this.shown = allowed;
    if (allowed) {
      this.viewContainer.createEmbeddedView(this.templateRef);
    } else {
      this.viewContainer.clear();
    }
  }
}
