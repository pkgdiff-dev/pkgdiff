import { TestBed } from '@angular/core/testing';

import { PackageMetadata } from '../models/package.models';
import { SeoService } from './seo.service';

const metadata: PackageMetadata = {
  packageId: 'left-pad',
  registry: 'npm',
  latestVersion: '1.3.0',
  description: 'String left pad',
  authors: 'azer, someone else',
  iconUrl: '',
  projectUrl: 'https://github.com/left-pad/left-pad',
  gitHubRepositoryUrl: 'https://github.com/left-pad/left-pad',
  versions: [
    { version: '1.3.0', publishedDate: '2026-09-15T10:00:00Z', isPrerelease: false, releaseNotes: '', releaseNotesHtml: '', releaseNotesSource: null, entries: [], isEmpty: true },
    { version: '1.2.0', publishedDate: null, isPrerelease: false, releaseNotes: '', releaseNotesHtml: '', releaseNotesSource: null, entries: [], isEmpty: true },
  ],
};

describe('SeoService', () => {
  let seo: SeoService;

  const jsonLd = (key: string) => Array.from(document.head.querySelectorAll(`script[data-seo="${key}"]`));
  const meta = (selector: string) => document.head.querySelector(`meta[${selector}]`)?.getAttribute('content');
  const canonical = () => Array.from(document.head.querySelectorAll('link[rel="canonical"]')).map(link => link.getAttribute('href'));

  beforeEach(() => {
    document.head.querySelectorAll('script[data-seo], link[rel="canonical"], meta').forEach(element => element.remove());
    TestBed.configureTestingModule({});
    seo = TestBed.inject(SeoService);
  });

  it('keeps exactly one breadcrumb however often a page sets it', () => {
    seo.setPackage(metadata, 'npm');
    seo.setPackage(metadata, 'npm', '1.2.0');
    seo.setBreadcrumb([{ name: 'Home', url: '/' }, { name: 'Blog', url: '/blog' }]);

    const breadcrumbs = jsonLd('breadcrumb');
    expect(breadcrumbs).toHaveLength(1);
    expect(JSON.parse(breadcrumbs[0].textContent!).itemListElement).toEqual([
      { '@type': 'ListItem', position: 1, name: 'Home', item: 'https://pkgdiff.dev/' },
      { '@type': 'ListItem', position: 2, name: 'Blog', item: 'https://pkgdiff.dev/blog' },
    ]);
    expect(jsonLd('page')).toHaveLength(1);
  });

  it('describes a package with its title, canonical address, structured data and breadcrumb', () => {
    seo.setPackage(metadata, 'npm');

    expect(document.title).toBe('left-pad v1.3.0 — NPM Release Notes | pkgdiff');
    expect(canonical()).toEqual(['https://pkgdiff.dev/package/npm/left-pad']);
    expect(meta('property="og:url"')).toBe('https://pkgdiff.dev/package/npm/left-pad');
    expect(meta('name="description"')).toBe('left-pad v1.3.0 - String left pad. View 2 versions and compare release notes.');
    expect(meta('name="robots"')).toContain('index, follow');

    const data = JSON.parse(jsonLd('page')[0].textContent!);
    expect(data['@type']).toBe('SoftwareApplication');
    expect(data.softwareVersion).toBe('1.3.0');
    expect(data.datePublished).toBe('2026-09-15T10:00:00Z');
    expect(data.author).toEqual([{ '@type': 'Person', name: 'azer' }, { '@type': 'Person', name: 'someone else' }]);

    const crumbs = JSON.parse(jsonLd('breadcrumb')[0].textContent!).itemListElement;
    expect(crumbs.map((crumb: { name: string }) => crumb.name)).toEqual(['Home', 'Packages', 'NPM', 'left-pad']);
  });

  it('points a version page at its own address and keeps one canonical link', () => {
    seo.setPackage(metadata, 'npm');
    seo.setPackage(metadata, 'npm', '1.2.0');

    expect(document.title).toBe('left-pad v1.2.0 — NPM Release Notes | pkgdiff');
    expect(canonical()).toEqual(['https://pkgdiff.dev/package/npm/left-pad/1.2.0']);
  });

  it('cuts a long description at 160 characters', () => {
    seo.setPackage({ ...metadata, description: 'x'.repeat(400) }, 'npm');

    const description = meta('name="description"')!;
    expect(description).toHaveLength(160);
    expect(description.endsWith('...')).toBe(true);
  });

  it('writes structured data so that text from a registry cannot end the script element', () => {
    seo.setPackage({ ...metadata, description: '</script><script>alert(1)</script>' }, 'npm');

    const script = jsonLd('page')[0];
    expect(script.textContent).not.toContain('</script>');
    expect(JSON.parse(script.textContent!).description).toBe('</script><script>alert(1)</script>');
  });

  it('asks search engines not to index a page that does not exist, and to index the next real one', () => {
    seo.setPackage(metadata, 'npm');

    seo.setNotFound('Not Found | pkgdiff', 'Nothing here.');
    expect(document.title).toBe('Not Found | pkgdiff');
    expect(meta('name="robots"')).toBe('noindex, follow');
    expect(jsonLd('page')).toHaveLength(0);
    expect(jsonLd('breadcrumb')).toHaveLength(0);

    seo.updateMeta({ title: 'Home', description: 'Home page', url: '/' });
    expect(meta('name="robots"')).toContain('index, follow');
  });

  it('removes what a page added for itself when the page is left', () => {
    seo.setPackage(metadata, 'npm');

    seo.clearPageData();

    expect(jsonLd('page')).toHaveLength(0);
    expect(jsonLd('breadcrumb')).toHaveLength(0);
  });

  it('uses an absolute image address and falls back to the site image', () => {
    seo.updateMeta({ title: 'Blog', description: 'Blog', url: '/blog', image: '/og-image/blog.png' });
    expect(meta('property="og:image"')).toBe('https://pkgdiff.dev/og-image/blog.png');
    expect(meta('name="twitter:image"')).toBe('https://pkgdiff.dev/og-image/blog.png');

    seo.updateMeta({ title: 'Home', description: 'Home', url: '/' });
    expect(meta('property="og:image"')).toBe('https://pkgdiff.dev/og-image/home.png');
  });
});
