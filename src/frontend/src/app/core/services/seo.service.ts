import { Injectable, inject } from '@angular/core';
import { Title, Meta } from '@angular/platform-browser';
import { DOCUMENT } from '@angular/common';
import { PackageMetadata } from '../models/package.models';

export interface SeoConfig {
  title: string;
  description: string;
  url?: string;
  type?: string;
  image?: string;
  author?: string;
}

export interface BreadcrumbItem {
  name: string;
  url: string;
}

type StructuredData = Record<string, unknown>;

const DEFAULT_ROBOTS = 'index, follow, max-snippet:-1, max-image-preview:large, max-video-preview:-1';

@Injectable({ providedIn: 'root' })
export class SeoService {
  private title = inject(Title);
  private meta = inject(Meta);
  private document = inject(DOCUMENT);

  private readonly siteName = 'pkgdiff';
  private readonly baseUrl = 'https://pkgdiff.dev';
  private readonly defaultOgImage = '/og-image/home.png';

  updateMeta(config: SeoConfig): void {
    this.title.setTitle(config.title);

    this.meta.updateTag({ name: 'description', content: config.description });
    // A page that sets its meta exists; only a missing page asks not to be indexed
    this.meta.updateTag({ name: 'robots', content: DEFAULT_ROBOTS });

    if (config.author) {
      this.meta.updateTag({ name: 'author', content: config.author });
    }

    if (config.url) {
      this.updateCanonicalUrl(`${this.baseUrl}${config.url}`);
    }

    this.meta.updateTag({ property: 'og:title', content: config.title });
    this.meta.updateTag({ property: 'og:description', content: config.description });

    if (config.url) {
      this.meta.updateTag({ property: 'og:url', content: `${this.baseUrl}${config.url}` });
      this.meta.updateTag({ name: 'twitter:url', content: `${this.baseUrl}${config.url}` });
    }

    if (config.type) {
      this.meta.updateTag({ property: 'og:type', content: config.type });
    }

    const rawImageUrl = config.image || this.defaultOgImage;
    const imageUrl = rawImageUrl.startsWith('http') ? rawImageUrl : `${this.baseUrl}${rawImageUrl}`;
    this.meta.updateTag({ property: 'og:image', content: imageUrl });
    this.meta.updateTag({ property: 'og:image:secure_url', content: imageUrl });
    this.meta.updateTag({ property: 'og:image:width', content: '1200' });
    this.meta.updateTag({ property: 'og:image:height', content: '630' });
    this.meta.updateTag({ property: 'og:image:alt', content: config.title });

    this.meta.updateTag({ name: 'twitter:card', content: 'summary_large_image' });
    this.meta.updateTag({ name: 'twitter:image', content: imageUrl });
    this.meta.updateTag({ name: 'twitter:image:alt', content: config.title });
    this.meta.updateTag({ name: 'twitter:title', content: config.title });
    this.meta.updateTag({ name: 'twitter:description', content: config.description });

    this.meta.updateTag({ name: 'theme-color', content: '#0a0a0a' });
  }

  setNotFound(title: string, description: string): void {
    this.title.setTitle(title);
    this.meta.updateTag({ name: 'description', content: description });
    this.meta.updateTag({ name: 'robots', content: 'noindex, follow' });
    this.meta.updateTag({ property: 'og:title', content: title });
    this.meta.updateTag({ property: 'og:description', content: description });
    this.clearPageData();
  }

  setStructuredData(data: StructuredData): void {
    this.setJsonLd('page', data);
  }

  removeStructuredData(): void {
    this.removeJsonLd('page');
  }

  setBreadcrumb(items: BreadcrumbItem[]): void {
    this.setJsonLd('breadcrumb', {
      '@context': 'https://schema.org',
      '@type': 'BreadcrumbList',
      itemListElement: items.map((item, index) => ({
        '@type': 'ListItem',
        position: index + 1,
        name: item.name,
        item: `${this.baseUrl}${item.url}`,
      })),
    });
  }

  removeBreadcrumb(): void {
    this.removeJsonLd('breadcrumb');
  }

  clearPageData(): void {
    this.removeStructuredData();
    this.removeBreadcrumb();
  }

  setPackagePlaceholder(packageId: string, registry: string, version?: string): void {
    const registryName = registry.toUpperCase();
    this.updateMeta({
      title: version
        ? `${packageId} v${version} — ${registryName} Package Release Notes | ${this.siteName}`
        : `${packageId} — ${registryName} Package Release Notes | ${this.siteName}`,
      description: `View complete release notes, version history, and changelog for ${packageId} ${registryName} package. Compare versions and analyze changes with pkgdiff.`,
      url: this.packageUrl(registry, packageId, version),
      type: 'article',
      image: '/og-image/package.png',
    });
  }

  setPackage(metadata: PackageMetadata, registry: string, version?: string): void {
    const versionCount = metadata.versions.length;
    const latestVersion = metadata.latestVersion;

    let description = metadata.packageId;
    if (latestVersion) {
      description += ` v${latestVersion}`;
    }
    if (metadata.description) {
      description += ` - ${metadata.description}`;
    }
    description += `. View ${versionCount} version${versionCount !== 1 ? 's' : ''} and compare release notes.`;

    if (description.length > 160) {
      description = description.substring(0, 157) + '...';
    }

    this.updateMeta({
      title: `${metadata.packageId} v${version ?? latestVersion} — ${registry.toUpperCase()} Release Notes | ${this.siteName}`,
      description,
      url: this.packageUrl(registry, metadata.packageId, version),
      type: 'article',
      author: metadata.authors || undefined,
      image: '/og-image/package.png',
    });

    const structuredData: StructuredData = {
      '@context': 'https://schema.org',
      '@type': 'SoftwareApplication',
      name: metadata.packageId,
      softwareVersion: latestVersion,
      applicationCategory: 'DeveloperApplication',
      operatingSystem: 'Cross-platform',
      offers: { '@type': 'Offer', price: '0', priceCurrency: 'USD' },
    };

    if (metadata.description) {
      structuredData['description'] = metadata.description;
    }

    if (metadata.authors) {
      const authors = metadata.authors.split(',').map(author => author.trim()).filter(Boolean);
      structuredData['author'] = authors.length === 1
        ? { '@type': 'Person', name: authors[0] }
        : authors.map(name => ({ '@type': 'Person', name }));
    }

    if (metadata.iconUrl) {
      structuredData['image'] = metadata.iconUrl;
    }

    if (metadata.projectUrl) {
      structuredData['url'] = metadata.projectUrl;
    }

    const latestRelease = metadata.versions.find(v => v.version === latestVersion);
    if (latestRelease?.publishedDate) {
      structuredData['datePublished'] = latestRelease.publishedDate;
    }

    this.setStructuredData(structuredData);

    this.setBreadcrumb([
      { name: 'Home', url: '/' },
      { name: 'Packages', url: '/packages' },
      { name: registry.toUpperCase(), url: `/packages?registry=${registry}` },
      { name: metadata.packageId, url: this.packageUrl(registry, metadata.packageId) },
    ]);
  }

  private packageUrl(registry: string, packageId: string, version?: string): string {
    const base = `/package/${registry}/${packageId}`;
    return version ? `${base}/${version}` : base;
  }

  private updateCanonicalUrl(url: string): void {
    let link = this.document.head.querySelector<HTMLLinkElement>('link[rel="canonical"]');
    if (!link) {
      link = this.document.createElement('link');
      link.setAttribute('rel', 'canonical');
      this.document.head.appendChild(link);
    }
    link.setAttribute('href', url);
  }

  private setJsonLd(key: string, data: StructuredData): void {
    let script = this.findJsonLd(key);
    if (!script) {
      script = this.document.createElement('script');
      script.setAttribute('type', 'application/ld+json');
      script.setAttribute('data-seo', key);
      this.document.head.appendChild(script);
    }
    // "<" cannot end the script element early when it is written as an escape
    script.textContent = JSON.stringify(data).replace(/</g, '\\u003c');
  }

  private removeJsonLd(key: string): void {
    this.findJsonLd(key)?.remove();
  }

  private findJsonLd(key: string): HTMLScriptElement | null {
    return this.document.head.querySelector<HTMLScriptElement>(`script[type="application/ld+json"][data-seo="${key}"]`);
  }
}
