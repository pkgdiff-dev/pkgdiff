import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { APP_CONFIG } from '../../core/config/app.config';
import { SeoService } from '../../core/services/seo.service';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-cookies',
  standalone: true,
  templateUrl: './cookies.html',
})
export class CookiesComponent implements OnInit {
  private seo = inject(SeoService);
  appName = APP_CONFIG.name;
  contactEmail = APP_CONFIG.contact.email;
  lastUpdated = 'October 19, 2026';

  ngOnInit() {
    this.seo.updateMeta({
      title: 'Cookie Policy | pkgdiff',
      description: 'Cookie Policy for pkgdiff. Learn about our use of local storage and cookies.',
      url: '/cookies',
    });
  }
}
