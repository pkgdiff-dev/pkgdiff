import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SeoService } from '../../core/services/seo.service';
import { HttpStatusService } from '../../core/services/http-status.service';

@Component({
  selector: 'app-not-found',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './not-found.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotFoundComponent {
  constructor() {
    inject(HttpStatusService).notFound();
    inject(SeoService).setNotFound(
      '404 - Page Not Found | pkgdiff',
      'The requested page could not be found. Return to pkgdiff to compare package versions across NuGet, npm, PyPI, Maven, Pub and Packagist registries.');
  }
}
