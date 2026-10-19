import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterOutlet, RouterLink, RouterLinkActive, Router, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs';
import {
  LucideAngularModule,
  LayoutDashboard, LogOut, Sun, Moon, Package,
  Activity, Globe, History, Settings, ExternalLink, ChevronDown, Users, Shield, ShieldAlert, FilterX, Bug, FileText, Trophy, Menu, X, Share2
} from 'lucide-angular';
import { AuthService } from '../../../core/services/auth.service';
import { ThemeService } from '../../../core/services/theme.service';
import { LogoComponent } from '../../components/logo/logo';

import { HasPermissionDirective } from '../../directives/has-permission.directive';

@Component({
  selector: 'app-admin-layout',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, LucideAngularModule, LogoComponent, HasPermissionDirective],
  templateUrl: './admin-layout.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'flex-1 flex flex-col',
    '(document:click)': 'onDocumentClick($event)',
    '(document:keydown.escape)': 'closeMenus()',
  },
})
export class AdminLayoutComponent {
  protected auth = inject(AuthService);
  protected theme = inject(ThemeService);
  private router = inject(Router);

  readonly DashboardIcon = LayoutDashboard;
  readonly LogOutIcon = LogOut;
  readonly SunIcon = Sun;
  readonly MoonIcon = Moon;
  readonly PackageIcon = Package;
  readonly ActivityIcon = Activity;
  readonly GlobeIcon = Globe;
  readonly HistoryIcon = History;
  readonly SettingsIcon = Settings;
  readonly FilterXIcon = FilterX;
  readonly ExternalLinkIcon = ExternalLink;
  readonly ChevronDownIcon = ChevronDown;
  readonly UsersIcon = Users;
  readonly ShieldIcon = Shield;
  readonly ShieldAlertIcon = ShieldAlert;
  readonly BugIcon = Bug;
  readonly FileTextIcon = FileText;
  readonly TrophyIcon = Trophy;
  readonly MenuIcon = Menu;
  readonly XIcon = X;
  readonly Share2Icon = Share2;

  profileOpen = signal(false);
  sidebarOpen = signal(false);

  constructor() {
    this.router.events.pipe(
      filter(event => event instanceof NavigationEnd),
      takeUntilDestroyed(),
    ).subscribe(() => this.sidebarOpen.set(false));
  }

  toggleSidebar(): void {
    this.sidebarOpen.update(v => !v);
  }

  toggleProfile(): void {
    this.profileOpen.update(v => !v);
  }

  onDocumentClick(event: Event): void {
    const target = event.target;
    if (!(target instanceof Element) || !target.closest('.profile-dropdown')) {
      this.profileOpen.set(false);
    }
  }

  closeMenus(): void {
    this.profileOpen.set(false);
    this.sidebarOpen.set(false);
  }

  logout(): void {
    this.auth.logout();
  }

  toggleTheme(): void {
    this.theme.toggleTheme();
  }

  hasAnyPermission(permissions: string[]): boolean {
    return permissions.some(p => this.auth.hasPermission(p));
  }
}
