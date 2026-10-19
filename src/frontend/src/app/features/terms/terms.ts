import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { APP_CONFIG } from '../../core/config/app.config';
import { SeoService } from '../../core/services/seo.service';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-terms',
  standalone: true,
  templateUrl: './terms.html',
})
export class TermsComponent implements OnInit {
  private seo = inject(SeoService);
  appName = APP_CONFIG.name;
  contactEmail = APP_CONFIG.contact.email;
  lastUpdated = 'February 8, 2026';

  ngOnInit(): void {
    this.seo.updateMeta({
      title: 'Terms of Service | pkgdiff',
      description: 'Terms of Service for pkgdiff — package version comparison tool.',
      url: '/terms',
    });
  }
}
