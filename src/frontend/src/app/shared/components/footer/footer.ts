import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LucideAngularModule, Github, Twitter, Linkedin, Package, Info } from 'lucide-angular';
import { LogoComponent } from '../logo/logo';
import { APP_CONFIG } from '../../../core/config/app.config';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-footer',
  standalone: true,
  imports: [LucideAngularModule, LogoComponent, RouterLink],
  templateUrl: './footer.html',
})
export class FooterComponent {
  readonly GithubIcon = Github;
  readonly TwitterIcon = Twitter;
  readonly LinkedinIcon = Linkedin;
  readonly PackageIcon = Package;
  readonly InfoIcon = Info;
  readonly exploreLinks = [
    { path: '/packages', label: 'Packages' },
    { path: '/audit', label: 'Dependency audit' },
    { path: '/blog', label: 'Release digests' },
    { path: '/contributors', label: 'Contributors' },
    { path: '/about', label: 'About' },
  ];

  appName = APP_CONFIG.name;
  appDescription = APP_CONFIG.description;

  currentYear = new Date().getFullYear();
}
