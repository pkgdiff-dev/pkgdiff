import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { guestGuard } from './core/guards/guest.guard';
import { permissionGuard } from './core/guards/permission.guard';

const loadPublicLayout = () => import('./shared/layouts/public-layout/public-layout').then(m => m.PublicLayoutComponent);

export const routes: Routes = [
  {
    path: '',
    loadComponent: loadPublicLayout,
    children: [
      {
        path: '',
        loadComponent: () => import('./features/home/home').then(m => m.HomeComponent)
      },
      {
        path: 'packages',
        loadComponent: () => import('./features/packages/packages-list').then(m => m.PackagesListComponent)
      },
      {
        path: 'package/:registry/:packageId',
        loadComponent: () => import('./features/package/package').then(m => m.PackageComponent)
      },
      {
        path: 'package/:registry/:packageId/:version',
        loadComponent: () => import('./features/package/package').then(m => m.PackageComponent)
      },
      {
        // A scoped npm name typed with its slash, with a version: /package/npm/@scope/name/1.0.0
        path: 'package/:registry/:scope/:name/:version',
        loadComponent: () => import('./features/package/package').then(m => m.PackageComponent)
      },
      {
        path: 'terms',
        loadComponent: () => import('./features/terms/terms').then(m => m.TermsComponent)
      },
      {
        path: 'privacy',
        loadComponent: () => import('./features/privacy/privacy').then(m => m.PrivacyComponent)
      },
      {
        path: 'cookies',
        loadComponent: () => import('./features/cookies/cookies').then(m => m.CookiesComponent)
      },
      {
        path: 'blog',
        loadComponent: () => import('./features/blog/blog-list/blog-list').then(m => m.BlogListComponent)
      },
      {
        path: 'blog/:slug',
        loadComponent: () => import('./features/blog/blog-detail/blog-detail').then(m => m.BlogDetailComponent)
      },
      {
        path: 'contributors',
        loadComponent: () => import('./features/contributors/contributors').then(m => m.ContributorsComponent)
      },
      {
        path: 'audit',
        loadComponent: () => import('./features/audit/audit-wizard/audit-wizard').then(m => m.AuditWizardComponent)
      },
      {
        path: 'audit/:id',
        loadComponent: () => import('./features/audit/audit-report/audit-report').then(m => m.AuditReportComponent)
      },
    ],
  },
  {
    path: 'admin/login',
    loadComponent: () => import('./features/admin/login/admin-login').then(m => m.AdminLoginComponent),
    canActivate: [guestGuard],
  },
  {
    path: 'admin',
    loadComponent: () => import('./shared/layouts/admin-layout/admin-layout').then(m => m.AdminLayoutComponent),
    canActivate: [authGuard],
    children: [
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
      {
        path: 'dashboard',
        loadComponent: () => import('./features/admin/dashboard/admin-dashboard').then(m => m.AdminDashboardComponent)
      },
      {
        path: 'health-monitoring',
        canActivate: [permissionGuard],
        data: { permission: 'Permissions.Health.View' },
        loadComponent: () => import('./features/admin/health-monitoring/health-monitoring.component').then(m => m.HealthMonitoringComponent)
      },
      {
        path: 'packages',
        canActivate: [permissionGuard],
        data: { permission: 'Permissions.Packages.View' },
        loadComponent: () => import('./features/admin/packages/admin-packages').then(m => m.AdminPackagesComponent)
      },
      {
        path: 'jobs',
        canActivate: [permissionGuard],
        data: { permission: 'Permissions.Jobs.View' },
        loadComponent: () => import('./features/admin/jobs/admin-jobs').then(m => m.AdminJobsComponent)
      },
      {
        path: 'registries',
        canActivate: [permissionGuard],
        data: { permission: 'Permissions.Registries.View' },
        loadComponent: () => import('./features/admin/registries/admin-registries').then(m => m.AdminRegistriesComponent)
      },
      {
        path: 'search-history',
        canActivate: [permissionGuard],
        data: { permission: 'Permissions.Security.ViewLogs' },
        loadComponent: () => import('./features/admin/search-history/admin-search-history').then(m => m.AdminSearchHistoryComponent)
      },
      {
        path: 'settings',
        canActivate: [permissionGuard],
        data: { permission: 'Permissions.Settings.View' },
        loadComponent: () => import('./features/admin/settings/admin-settings').then(m => m.AdminSettingsComponent)
      },
      {
        path: 'version-exclude-patterns',
        canActivate: [permissionGuard],
        data: { permission: 'Permissions.VersionExcludePatterns.View' },
        loadComponent: () => import('./features/admin/version-exclude-patterns/version-exclude-patterns.component').then(m => m.VersionExcludePatternsComponent)
      },
      {
        path: 'users',
        canActivate: [permissionGuard],
        data: { permission: 'Permissions.Users.View' },
        loadComponent: () => import('./features/admin/users/admin-users').then(m => m.AdminUsersComponent)
      },
      {
        path: 'roles',
        canActivate: [permissionGuard],
        data: { permission: 'Permissions.Roles.View' },
        loadComponent: () => import('./features/admin/roles/admin-roles').then(m => m.AdminRolesComponent)
      },
      {
        path: 'security',
        canActivate: [permissionGuard],
        data: { permission: 'Permissions.Security.ManageIps' },
        loadComponent: () => import('./features/admin/security/admin-security').then(m => m.AdminSecurityComponent)
      },
      {
        path: 'scrape-logs',
        canActivate: [permissionGuard],
        data: { permission: 'Permissions.ScrapeLogs.View' },
        loadComponent: () => import('./features/admin/scrape-logs/admin-scrape-logs').then(m => m.AdminScrapeLogsComponent)
      },
      {
        path: 'blogs',
        canActivate: [permissionGuard],
        data: { permission: 'Permissions.Blog.View' },
        loadComponent: () => import('./features/admin/blogs/admin-blogs').then(m => m.AdminBlogsComponent)
      },
      {
        path: 'social-media',
        canActivate: [permissionGuard],
        data: { permission: 'Permissions.SocialMedia.View' },
        loadComponent: () => import('./features/admin/social-media/admin-social-media').then(m => m.AdminSocialMediaComponent)
      },
      {
        // LinkedIn OAuth redirect target - uses the same component, reads ?code= in ngOnInit
        path: 'social-media/linkedin/callback',
        canActivate: [permissionGuard],
        data: { permission: 'Permissions.SocialMedia.Manage' },
        loadComponent: () => import('./features/admin/social-media/admin-social-media').then(m => m.AdminSocialMediaComponent)
      },
      {
        path: 'contributors',
        canActivate: [permissionGuard],
        data: { permission: 'Permissions.Contributors.Manage' },
        loadComponent: () => import('./features/admin/contributors/admin-contributors').then(m => m.AdminContributorsComponent)
      },
    ],
  },
  {
    path: '**',
    loadComponent: loadPublicLayout,
    children: [
      {
        path: '',
        loadComponent: () => import('./features/not-found/not-found').then(m => m.NotFoundComponent)
      },
    ],
  },
];
