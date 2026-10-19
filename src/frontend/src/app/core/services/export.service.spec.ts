import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { parse as tomlParse } from '@ltd/j-toml';
import type { MockInstance } from 'vitest';

import {
  AnalysisResult,
  ChangeCategory,
  PackageMetadata,
  VersionInfo,
} from '../models/package.models';
import { environment } from '../../../environments/environment';
import { ExportService } from './export.service';
import { ToastService } from './toast.service';

interface DownloadTarget {
  downloadFile(blob: Blob, filename: string): void;
}

interface TomlEntry {
  category: string;
  text: string;
}

interface TomlVersion {
  version: string;
  release_notes: string;
  entries: TomlEntry[];
}

interface TomlDiffExport {
  diff: { package_id: string; from_version: string; to_version: string };
  versions: TomlVersion[];
}

interface TomlReleaseNotesExport {
  package: { id: string; latest_version: string };
  versions: TomlVersion[];
}

const UNICODE = '→ • 🚀 中文';

const versions: VersionInfo[] = [
  {
    version: '2.0.0',
    publishedDate: '2026-02-03T00:00:00Z',
    releaseNotes: `<b>Breaking</b> release ${UNICODE}`,
    releaseNotesHtml: '<p><b>Breaking</b> release</p>',
    releaseNotesSource: 'GitHub',
    entries: [
      {
        category: ChangeCategory.BreakingChange,
        text: `Renamed Foo to Bar ${UNICODE}`,
        textHtml: '<p>Renamed Foo to Bar</p>',
      },
      {
        category: ChangeCategory.BugFix,
        text: 'Fixed <code>null</code> handling, see "issue #12"',
        textHtml: '<p>Fixed <code>null</code> handling</p>',
      },
    ],
    categories: [ChangeCategory.BreakingChange, ChangeCategory.BugFix],
    isEmpty: false,
    isPrerelease: false,
  },
  {
    version: '1.5.0',
    publishedDate: null,
    releaseNotes: `First line with <i>HTML</i>\nSecond line ${UNICODE}`,
    releaseNotesHtml: '<p>First line with <i>HTML</i></p>',
    releaseNotesSource: null,
    entries: [],
    categories: [],
    isEmpty: false,
    isPrerelease: true,
  },
];

const diff: AnalysisResult = {
  packageId: 'Sample.Package',
  fromVersion: '1.0.0',
  toVersion: '2.0.0',
  totalVersions: 2,
  versions,
  summary: {
    breakingChanges: 1,
    bugFixes: 1,
    newFeatures: 0,
    performanceImprovements: 0,
    securityFixes: 0,
    deprecations: 0,
    otherChanges: 0,
  },
};

const metadata: PackageMetadata = {
  packageId: 'Sample.Package',
  registry: 'nuget',
  latestVersion: '2.0.0',
  description: `Demo package ${UNICODE}`,
  authors: 'pkgdiff',
  iconUrl: '',
  projectUrl: 'https://example.com/sample',
  gitHubRepositoryUrl: null,
  versions: [],
};

function readText(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result));
    reader.onerror = () => reject(reader.error);
    reader.readAsText(blob);
  });
}

describe('ExportService', () => {
  let service: ExportService;
  let download: MockInstance<DownloadTarget['downloadFile']>;
  let toastError: MockInstance<ToastService['error']>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ExportService);

    const toast = TestBed.inject(ToastService);
    vi.spyOn(toast, 'success').mockImplementation(() => {});
    toastError = vi.spyOn(toast, 'error').mockImplementation(() => {});
    download = vi
      .spyOn(ExportService.prototype as unknown as DownloadTarget, 'downloadFile')
      .mockImplementation(() => {});
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  async function exported(): Promise<{ text: string; filename: string }> {
    expect(toastError).not.toHaveBeenCalled();
    expect(download).toHaveBeenCalledTimes(1);
    const [blob, filename] = download.mock.calls[0];
    return { text: await readText(blob), filename };
  }

  it('exportDiff writes the comparison as Markdown', async () => {
    await service.exportDiff(diff, 'nuget', 'md');
    const { text, filename } = await exported();

    expect(filename).toBe('Sample.Package-diff-1.0.0-to-2.0.0.md');
    expect(text).toContain('# Sample.Package - Version Comparison');
    expect(text).toContain('**From:** 1.0.0');
    expect(text).toContain('**To:** 2.0.0');
    expect(text).toContain('| 🔴 Breaking Changes | 1 |');
    expect(text).toContain('## Changes by Version');
    expect(text).toContain('### Version 2.0.0');
    expect(text).toContain('### Version 1.5.0');
    expect(text).toContain('#### 🔴 Breaking Changes');
    expect(text).toContain('#### 🐛 Bug Fixes');
    for (const entry of versions.flatMap((version) => version.entries)) {
      expect(text).toContain(`- ${entry.text}`);
    }
    expect(text).toContain(versions[1].releaseNotes);
  });

  it('exportDiff writes JSON without the display-only rendered HTML', async () => {
    await service.exportDiff(diff, 'nuget', 'json');
    const { text, filename } = await exported();
    const parsed = JSON.parse(text) as AnalysisResult;

    expect(filename).toBe('Sample.Package-diff-1.0.0-to-2.0.0.json');
    expect(parsed.packageId).toBe('Sample.Package');
    expect(parsed.fromVersion).toBe('1.0.0');
    expect(parsed.toVersion).toBe('2.0.0');
    expect(parsed.versions).toHaveLength(2);
    for (const version of parsed.versions) {
      expect(version).not.toHaveProperty('releaseNotesHtml');
      for (const entry of version.entries) {
        expect(entry).not.toHaveProperty('textHtml');
      }
    }
    expect(parsed.versions[0].entries.map((entry) => entry.text)).toEqual(
      versions[0].entries.map((entry) => entry.text),
    );
    expect(parsed.versions[1].releaseNotes).toBe(versions[1].releaseNotes);
  });

  it('exportDiff writes TOML that parses back to the same content', async () => {
    await service.exportDiff(diff, 'nuget', 'toml');
    const { text, filename } = await exported();
    const parsed = tomlParse(text, { joiner: '\n' }) as unknown as TomlDiffExport;

    expect(filename).toBe('Sample.Package-diff-1.0.0-to-2.0.0.toml');
    expect(text).toContain('Sample.Package');
    expect(parsed.diff.package_id).toBe('Sample.Package');
    expect(parsed.diff.from_version).toBe('1.0.0');
    expect(parsed.diff.to_version).toBe('2.0.0');
    expect(parsed.versions.map((version) => version.version)).toEqual(['2.0.0', '1.5.0']);
    expect(parsed.versions[0].entries).toEqual([
      { category: 'Breaking Changes', text: versions[0].entries[0].text },
      { category: 'Bug Fixes', text: versions[0].entries[1].text },
    ]);
    expect(parsed.versions[1].release_notes).toBe(versions[1].releaseNotes);
    // TOML has no null: a version without a publish date or source omits those keys
    expect(parsed.versions[1]).not.toHaveProperty('published_date');
    expect(parsed.versions[1]).not.toHaveProperty('release_notes_source');
    // Counts are TOML integers, not floats (2, never 2.0)
    expect(text).toContain('diff.total_versions = 2\n');
    expect(text).toContain('summary.breaking_changes = 1\n');
    expect(text).toContain('summary.new_features = 0\n');
    expect(text).not.toMatch(/= \d+\.0\n/);
  });

  it('exportReleaseNotes writes Markdown named after the package', async () => {
    await service.exportReleaseNotes(metadata, versions, 'nuget', 'md');
    const { text, filename } = await exported();

    expect(filename).toBe('Sample.Package-release-notes.md');
    expect(text).toContain('# Sample.Package');
    expect(text).toContain('**Latest Version:** 2.0.0');
    expect(text).toContain(`**Description:** ${metadata.description}`);
    expect(text).toContain('## Release Notes');
    expect(text).toContain('#### Breaking Changes');
    for (const entry of versions.flatMap((version) => version.entries)) {
      expect(text).toContain(`- ${entry.text}`);
    }
    expect(text).toContain(versions[1].releaseNotes);
  });

  it('exportReleaseNotes writes JSON and puts a single version in the filename', async () => {
    await service.exportReleaseNotes(metadata, [versions[0]], 'nuget', 'json');
    const { text, filename } = await exported();
    const parsed = JSON.parse(text) as PackageMetadata;

    expect(filename).toBe('Sample.Package-v2.0.0-release-notes.json');
    expect(parsed.packageId).toBe('Sample.Package');
    expect(parsed.description).toBe(metadata.description);
    expect(parsed.versions).toHaveLength(1);
    expect(parsed.versions[0]).not.toHaveProperty('releaseNotesHtml');
    expect(parsed.versions[0].entries[0].text).toBe(versions[0].entries[0].text);
  });

  it('exportReleaseNotes writes TOML that parses back to the same content', async () => {
    await service.exportReleaseNotes(metadata, versions, 'nuget', 'toml');
    const { text, filename } = await exported();
    const parsed = tomlParse(text, { joiner: '\n' }) as unknown as TomlReleaseNotesExport;

    expect(filename).toBe('Sample.Package-release-notes.toml');
    expect(parsed.package.id).toBe('Sample.Package');
    expect(parsed.package.latest_version).toBe('2.0.0');
    // gitHubRepositoryUrl is null in the fixture
    expect(parsed.package).not.toHaveProperty('github_url');
    expect(parsed.versions[0].entries[0].text).toBe(versions[0].entries[0].text);
    expect(parsed.versions[1].release_notes).toBe(versions[1].releaseNotes);
  });

  it('asks the API for the comparison PDF on the registry the package is on', async () => {
    const http = TestBed.inject(HttpTestingController);

    const done = service.exportDiff(diff, 'npm', 'pdf');
    const request = http.expectOne(`${environment.apiUrl}/Package/export/diff/pdf`);
    request.flush(new Blob(['%PDF']));
    await done;

    expect(request.request.body).toEqual({
      packageId: diff.packageId,
      registry: 'npm',
      currentVersion: diff.fromVersion,
      targetVersion: diff.toVersion,
      includePrerelease: false,
    });
    expect((await exported()).filename).toBe(`${diff.packageId}-diff-${diff.fromVersion}-to-${diff.toVersion}.pdf`);
  });

  it('asks the API for the release notes PDF and says so when it fails', async () => {
    const http = TestBed.inject(HttpTestingController);

    const done = service.exportReleaseNotes(metadata, versions, 'pypi', 'pdf');
    http
      .expectOne(`${environment.apiUrl}/Package/pypi/${metadata.packageId}/export/pdf`)
      .flush(new Blob(), { status: 500, statusText: 'Server Error' });
    await done;

    expect(download).not.toHaveBeenCalled();
    expect(toastError).toHaveBeenCalledTimes(1);
    expect(toastError).toHaveBeenCalledWith('PDF export failed. Please try again.', 3000);
  });

  it('leaves a refused PDF request to the message the error interceptor shows', async () => {
    const http = TestBed.inject(HttpTestingController);

    const done = service.exportDiff(diff, 'nuget', 'pdf');
    http
      .expectOne(`${environment.apiUrl}/Package/export/diff/pdf`)
      .flush(new Blob(), { status: 429, statusText: 'Too Many Requests' });
    await done;

    expect(download).not.toHaveBeenCalled();
    expect(toastError).not.toHaveBeenCalled();
  });
});
