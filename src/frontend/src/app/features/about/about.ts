import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { APP_CONFIG } from '../../core/config/app.config';
import { SeoService } from '../../core/services/seo.service';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-about',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './about.html',
})
export class AboutComponent {
  appName = APP_CONFIG.name;
  contactEmail = APP_CONFIG.contact.email;
  operatorName = 'Enes Yağız';

  constructor() {
    inject(SeoService).updateMeta({
      title: 'About pkgdiff: Who Runs It and Where the Data Comes From',
      description: 'Who operates pkgdiff, which sources its release notes and security data come from, how changes are categorised, and what the data cannot tell you.',
      url: '/about',
    });
  }
}
