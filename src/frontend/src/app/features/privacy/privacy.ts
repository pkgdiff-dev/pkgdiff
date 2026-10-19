import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { APP_CONFIG } from '../../core/config/app.config';
import { SeoService } from '../../core/services/seo.service';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-privacy',
  standalone: true,
  templateUrl: './privacy.html',
})
export class PrivacyComponent implements OnInit {
  private seo = inject(SeoService);
  appName = APP_CONFIG.name;
  contactEmail = APP_CONFIG.contact.email;
  siteUrl = 'pkgdiff.dev';
  controllerName = 'Enes Yağız';
  controllerCountry = 'Türkiye';
  lastUpdated = 'October 19, 2026';

  ngOnInit(): void {
    this.seo.updateMeta({
      title: 'Privacy Policy | pkgdiff',
      description: 'Privacy Policy for pkgdiff. Learn how we handle your data.',
      url: '/privacy',
    });
  }
}
