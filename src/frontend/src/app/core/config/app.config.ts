import { environment } from '../../../environments/environment';

export const APP_CONFIG = {
  // App Information
  name: 'pkgdiff',
  shortName: 'pkgdiff',
  description: 'Compare and analyze package versions across NuGet, npm, PyPI and Pub registries. Track breaking changes, view detailed release notes, and understand version differences with our free online diff tool.',
  version: '1.0.0',

  // Contact & Social
  contact: {
    email: 'info@pkgdiff.dev',
    github: 'https://github.com/pkgdiff-dev/pkgdiff',
    twitter: 'https://twitter.com/pkgdiff',
  },

  // Logo & Branding
  logo: {
    light: '/logo/logo-light.svg',
    dark: '/logo/logo-dark.svg',
    favicon: '/favicon/favicon.ico',
  },

  // Theme Configuration
  theme: {
    primary: '#3b82f6',
    secondary: '#8b5cf6',
    background: '#ffffff',
    dark: '#1a1a1a',
  },

  // Footer Links
  footer: {
    sections: {
      product: {
        title: 'Product',
        links: [
          { label: 'Features', url: '/features' },
          { label: 'Pricing', url: '/pricing' },
          { label: 'API', url: '/api' },
        ],
      },
      company: {
        title: 'Company',
        links: [
          { label: 'About', url: '/about' },
          { label: 'Blog', url: '/blog' },
          { label: 'Careers', url: '/careers' },
          { label: 'Contact', url: '/contact' },
        ],
      },
      legal: {
        title: 'Legal',
        links: [
          { label: 'Privacy', url: '/privacy' },
          { label: 'Terms', url: '/terms' },
          { label: 'License', url: '/license' },
        ],
      },
    },
    copyright: `© ${new Date().getFullYear()} pkgdiff. All rights reserved.`,
  },

  // Feature Flags
  features: {
    signalr: true,
    hangfire: true,
    analytics: false,
  },

  // API Configuration
  api: {
    baseUrl: environment.apiUrl,
    timeout: 30000,
  },
} as const;

export type AppConfig = typeof APP_CONFIG;
