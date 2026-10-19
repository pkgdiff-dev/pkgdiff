import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, ParamMap, Router, convertToParamMap } from '@angular/router';
import { BehaviorSubject, Observable, Subject, of } from 'rxjs';

import { AnalysisResult, AnalyzeRequest, PackageMetadata } from '../../core/models/package.models';
import { HttpStatusService } from '../../core/services/http-status.service';
import { PackageService } from '../../core/services/package.service';
import { SeoService } from '../../core/services/seo.service';
import { BackgroundProgress, PackageEvent, SignalrService } from '../../core/services/signalr.service';
import { ToastService } from '../../core/services/toast.service';
import { PackageComponent } from './package';

function metadataOf(packageId: string, registry = 'npm'): PackageMetadata {
  return {
    packageId,
    registry,
    latestVersion: '1.1.0',
    description: '',
    authors: '',
    iconUrl: '',
    projectUrl: '',
    gitHubRepositoryUrl: null,
    versions: ['1.1.0', '1.0.0'].map(version => ({
      version, publishedDate: null, isPrerelease: false, releaseNotes: '', releaseNotesHtml: '',
      releaseNotesSource: null, entries: [], isEmpty: true,
    })),
  };
}

const request = (from: string, to: string): AnalyzeRequest =>
  ({ packageId: 'left-pad', registry: 'npm', currentVersion: from, targetVersion: to, includePrerelease: false });

describe('PackageComponent', () => {
  let params: BehaviorSubject<ParamMap>;
  let metadataRequests: { registry: string; packageId: string; response: Subject<PackageMetadata> }[];
  let analyses: { request: AnalyzeRequest; response: Subject<AnalysisResult> }[];
  let hub: {
    progressMessage: ReturnType<typeof signal<string>>;
    backgroundProgress: ReturnType<typeof signal<BackgroundProgress | null>>;
    packageUpdated: ReturnType<typeof signal<PackageEvent | null>>;
    packageUpdateStarted: ReturnType<typeof signal<PackageEvent | null>>;
    securityUpdated: ReturnType<typeof signal<PackageEvent | null>>;
    reconnected: ReturnType<typeof signal<number>>;
    connectionId: ReturnType<typeof signal<string | null>>;
    progressToken: ReturnType<typeof signal<string | null>>;
    lastSequence: number;
    startConnection: () => Promise<void>;
    resetProgress: ReturnType<typeof vi.fn>;
  };
  let httpStatus: { notFound: ReturnType<typeof vi.fn> };
  let fixture: ComponentFixture<PackageComponent>;
  let page: PackageComponent;

  const open = (registry: string, packageId: string, version?: string) =>
    params.next(convertToParamMap(version ? { registry, packageId, version } : { registry, packageId }));

  const emit = (target: 'packageUpdated' | 'packageUpdateStarted', packageId: string) => {
    hub.lastSequence++;
    hub[target].set({ packageId, sequence: hub.lastSequence });
    TestBed.tick();
  };

  beforeEach(() => {
    params = new BehaviorSubject(convertToParamMap({ registry: 'npm', packageId: 'left-pad' }));
    metadataRequests = [];
    analyses = [];
    hub = {
      progressMessage: signal(''),
      backgroundProgress: signal<BackgroundProgress | null>(null),
      packageUpdated: signal<PackageEvent | null>(null),
      packageUpdateStarted: signal<PackageEvent | null>(null),
      securityUpdated: signal<PackageEvent | null>(null),
      reconnected: signal(0),
      connectionId: signal<string | null>('connection-1'),
      progressToken: signal<string | null>('token-1'),
      lastSequence: 0,
      startConnection: () => Promise.resolve(),
      resetProgress: vi.fn(),
    };
    httpStatus = { notFound: vi.fn() };

    const packages: Partial<PackageService> = {
      getPackageMetadata: (registry: string, packageId: string): Observable<PackageMetadata> => {
        const response = new Subject<PackageMetadata>();
        metadataRequests.push({ registry, packageId, response });
        return response;
      },
      analyze: (analyzeRequest: AnalyzeRequest): Observable<AnalysisResult> => {
        const response = new Subject<AnalysisResult>();
        analyses.push({ request: analyzeRequest, response });
        return response;
      },
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: ActivatedRoute, useValue: { paramMap: params, fragment: of(null), snapshot: { fragment: null } } },
        { provide: Router, useValue: { navigate: vi.fn() } },
        { provide: PackageService, useValue: packages },
        { provide: SignalrService, useValue: hub },
        { provide: HttpStatusService, useValue: httpStatus },
      ],
    });
    // The page's logic is under test; its template and child components are not
    TestBed.overrideComponent(PackageComponent, { set: { template: '', imports: [] } });
    fixture = TestBed.createComponent(PackageComponent);
    page = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('never shows the answer of a package the visitor has already left', () => {
    open('npm', 'react');
    expect(metadataRequests.map(r => r.packageId)).toEqual(['left-pad', 'react']);

    // The first request was abandoned, so nothing listens for its late answer
    expect(metadataRequests[0].response.observed).toBe(false);
    metadataRequests[1].response.next(metadataOf('react'));

    expect(page.metadata()?.packageId).toBe('react');
    expect(page.state()).toBe('loaded');
  });

  it('treats the same name on another registry as another package', () => {
    metadataRequests[0].response.next(metadataOf('left-pad'));

    open('pypi', 'left-pad');

    expect(metadataRequests.map(r => r.registry)).toEqual(['npm', 'pypi']);
    expect(page.state()).toBe('loading');
    expect(page.metadata()).toBeNull();
  });

  it('does not load again when only the version in the address changes', () => {
    metadataRequests[0].response.next(metadataOf('left-pad'));

    open('npm', 'left-pad', '1.0.0');

    expect(metadataRequests).toHaveLength(1);
    expect(page.version()).toBe('1.0.0');
    expect(page.mappedVersions().map(v => v.version)).toEqual(['1.0.0']);
    expect(document.title).toContain('left-pad v1.0.0');
  });

  it('refreshes for every finished update, also when the same package is updated twice in a row', () => {
    metadataRequests[0].response.next(metadataOf('left-pad'));

    emit('packageUpdateStarted', 'left-pad');
    expect(page.isUpdating()).toBe(true);
    emit('packageUpdated', 'left-pad');
    expect(page.isUpdating()).toBe(false);
    expect(metadataRequests).toHaveLength(2);

    emit('packageUpdateStarted', 'left-pad');
    expect(page.isUpdating()).toBe(true);
    emit('packageUpdated', 'left-pad');

    expect(page.isUpdating()).toBe(false);
    expect(metadataRequests).toHaveLength(3);
  });

  it('ignores the updates of other packages and events that are older than the page', () => {
    TestBed.resetTestingModule();
    // An event from an earlier visit is still the signal's value when the page opens
    hub.lastSequence = 7;
    hub.packageUpdated.set({ packageId: 'left-pad', sequence: 7 });
    hub.packageUpdateStarted.set({ packageId: 'left-pad', sequence: 6 });
    metadataRequests = [];
    TestBed.configureTestingModule({
      providers: [
        { provide: ActivatedRoute, useValue: { paramMap: params, fragment: of(null), snapshot: { fragment: null } } },
        { provide: Router, useValue: { navigate: vi.fn() } },
        { provide: PackageService, useValue: {
          getPackageMetadata: (registry: string, packageId: string) => {
            const response = new Subject<PackageMetadata>();
            metadataRequests.push({ registry, packageId, response });
            return response;
          },
          analyze: () => new Subject<AnalysisResult>(),
        } },
        { provide: SignalrService, useValue: hub },
        { provide: HttpStatusService, useValue: httpStatus },
      ],
    });
    TestBed.overrideComponent(PackageComponent, { set: { template: '', imports: [] } });
    const reopened = TestBed.createComponent(PackageComponent);
    reopened.detectChanges();
    TestBed.tick();

    expect(reopened.componentInstance.isUpdating()).toBe(false);
    expect(metadataRequests).toHaveLength(1);
    expect(TestBed.inject(ToastService).toasts()).toEqual([]);

    emit('packageUpdateStarted', 'react');
    emit('packageUpdated', 'react');
    expect(reopened.componentInstance.isUpdating()).toBe(false);
    expect(metadataRequests).toHaveLength(1);
  });

  it('stops showing an update as running when the visitor moves on or the connection was lost meanwhile', () => {
    metadataRequests[0].response.next(metadataOf('left-pad'));
    emit('packageUpdateStarted', 'left-pad');
    expect(page.isUpdating()).toBe(true);

    hub.reconnected.set(1);
    TestBed.tick();
    expect(page.isUpdating()).toBe(false);
    expect(metadataRequests).toHaveLength(2);

    emit('packageUpdateStarted', 'left-pad');
    open('npm', 'react');
    expect(page.isUpdating()).toBe(false);
    expect(hub.resetProgress).toHaveBeenCalled();
  });

  it('shows a missing package as not found, with a 404 for the server render, and other failures as an error', () => {
    metadataRequests[0].response.error(new HttpErrorResponse({ status: 404 }));
    expect(page.state()).toBe('not-found');
    expect(httpStatus.notFound).toHaveBeenCalledTimes(1);
    expect(document.head.querySelector('meta[name="robots"]')?.getAttribute('content')).toBe('noindex, follow');

    page.retry();
    expect(page.state()).toBe('loading');
    metadataRequests[1].response.error(new HttpErrorResponse({ status: 500 }));
    expect(page.state()).toBe('error');
    expect(httpStatus.notFound).toHaveBeenCalledTimes(1);

    page.retry();
    metadataRequests[2].response.next(metadataOf('left-pad'));
    expect(page.state()).toBe('loaded');
    expect(document.head.querySelector('meta[name="robots"]')?.getAttribute('content')).toContain('index, follow');
  });

  it('shows why a comparison failed and lets a newer comparison replace the one under way', () => {
    metadataRequests[0].response.next(metadataOf('left-pad'));

    page.onStartAnalysis(request('1.0.0', '1.1.0'));
    expect(page.analysisLoading()).toBe(true);
    expect(analyses[0].request).toMatchObject({ connectionId: 'connection-1', progressToken: 'token-1' });

    analyses[0].response.error(new HttpErrorResponse({ status: 400, error: { error: 'Invalid version format: abc' } }));
    expect(page.analysisLoading()).toBe(false);
    expect(page.analysisError()).toBe('Invalid version format: abc');
    expect(page.analysisData()).toBeNull();

    page.onStartAnalysis(request('1.0.0', '1.1.0'));
    expect(page.analysisError()).toBeNull();
    page.onStartAnalysis(request('1.0.0', '1.0.0'));
    expect(analyses[1].response.observed).toBe(false);

    const result = { packageId: 'left-pad', fromVersion: '1.0.0', toVersion: '1.0.0', totalVersions: 0, versions: [] } as unknown as AnalysisResult;
    analyses[2].response.next(result);
    expect(page.analysisData()).toBe(result);
    expect(page.analysisLoading()).toBe(false);

    page.onStartAnalysis(request('1.0.0', '1.1.0'));
    analyses[3].response.error(new HttpErrorResponse({ status: 500 }));
    expect(page.analysisError()).toBe('The comparison could not be completed. Please try again.');
  });

  it('removes its structured data and progress when the page is closed', () => {
    metadataRequests[0].response.next(metadataOf('left-pad'));
    expect(document.head.querySelectorAll('script[data-seo]')).toHaveLength(2);
    const seo = TestBed.inject(SeoService);
    const cleared = vi.spyOn(seo, 'clearPageData');

    fixture.destroy();

    expect(cleared).toHaveBeenCalled();
    expect(document.head.querySelectorAll('script[data-seo]')).toHaveLength(0);
    expect(metadataRequests[0].response.observed).toBe(false);
  });
});
