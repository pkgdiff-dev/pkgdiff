import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { LucideAngularModule, type LucideIconData, Sun, Moon, Menu, X, Package, ShieldCheck } from 'lucide-angular';
import { ThemeService } from '../../../core/services/theme.service';
import { LogoComponent } from '../logo/logo';

interface NavLink {
  path: string;
  label: string;
  title: string;
  icon: LucideIconData;
  primary?: boolean;
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-header',
  standalone: true,
  imports: [LucideAngularModule, LogoComponent, RouterLink, RouterLinkActive, NgTemplateOutlet],
  templateUrl: './header.html',
  host: { '(document:keydown.escape)': 'closeMenu()' },
})
export class HeaderComponent {
  protected theme = inject(ThemeService);

  readonly SunIcon = Sun;
  readonly MoonIcon = Moon;
  readonly MenuIcon = Menu;
  readonly CloseIcon = X;

  readonly links: NavLink[] = [
    { path: '/packages', label: 'Packages', title: 'Browse all tracked packages', icon: Package },
    {
      path: '/audit', label: 'Audit', title: 'Check the known vulnerabilities of your dependency file',
      icon: ShieldCheck, primary: true,
    },
  ];

  readonly moreLinks = [
    { path: '/blog', label: 'Blog' },
    { path: '/contributors', label: 'Contributors' },
  ];

  menuOpen = signal(false);

  toggleTheme(): void {
    this.theme.toggleTheme();
  }

  toggleMenu(): void {
    this.menuOpen.update(open => !open);
  }

  closeMenu(): void {
    this.menuOpen.set(false);
  }
}
