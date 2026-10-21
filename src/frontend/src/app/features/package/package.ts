import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { Subject, catchError, distinctUntilChanged, map, of, startWith, switchMap, tap } from 'rxjs';
import { LucideAngularModule, Clock, Link, Scale, Github, Info, Share2, Download } from 'lucide-angular';
import { ReleasesTabComponent } from './components/releases-tab/releases-tab';
import { DiffTabComponent } from './components/diff-tab/diff-tab';
import { VersionsTabComponent } from './components/versions-tab/versions-tab';
import { PackageNotFoundComponent } from './components/package-not-found/package-not-found';
import { NoStableVersionsComponent } from './components/no-stable-versions/no-stable-versions';
import { NoReleasesComponent } from './components/no-releases/no-releases';
import { ServiceUnavailableComponent } from './components/service-unavailable/service-unavailable';
import { ExportModalComponent } from '../../shared/components/export-modal/export-modal';
import { ShareModalComponent } from '../../shared/components/share-modal/share-modal';
import { TrustBadgeComponent } from './components/trust-badge/trust-badge';
import { TrustPanelComponent } from './components/trust-panel/trust-panel';
import { maliciousVersions } from '../../core/utils/trust.utils';
import { PackageService } from '../../core/services/package.service';
import { SignalrService, PackageEvent } from '../../core/services/signalr.service';
import { ToastService } from '../../core/services/toast.service';
import { SeoService } from '../../core/services/seo.service';
import { HttpStatusService } from '../../core/services/http-status.service';
import { AnalysisResult, AnalyzeRequest, PackageMetadata, VersionInfo } from '../../core/models/package.models';
import { apiErrorMessage, loadFailureOf } from '../../core/utils/http-error.utils';

type PageState = 'loading' | 'loaded' | 'not-found' | 'no-releases' | 'no-versions' | 'error';
type TabType = 'releases' | 'diff' | 'versions' | 'metadata';

interface PackageTarget {
  packageId: string;
  registry: string;
  version: string | undefined;
}

function isPackagist(registry: string): boolean {
  return ['packagist', 'composer'].includes(registry.toLowerCase());
}

// A scoped npm or a Packagist vendor/name can arrive split across route params; a lone scope takes the next segment
function targetOf(params: ParamMap): PackageTarget {
  const registry = params.get('registry') ?? 'nuget';
  const version = params.get('version') ?? undefined;
  const scope = params.get('scope');
  if (scope) {
    return { registry, packageId: `${scope}/${params.get('name') ?? ''}`, version };
  }

  const packageId = params.get('packageId') ?? '';
  const namespaced = packageId.startsWith('@') || isPackagist(registry);
  if (namespaced && !packageId.includes('/') && version) {
    return { registry, packageId: `${packageId}/${version}`, version: undefined };
  }
  return { registry, packageId, version };
}

type Loaded<T> = { value: T } | { failure: unknown };

const TABS: readonly TabType[] = ['releases', 'diff', 'versions', 'metadata'];

@Component({
  selector: 'app-package',
  standalone: true,
  imports: [
    LucideAngularModule,
    NoStableVersionsComponent,
    ReleasesTabComponent,
    DiffTabComponent,
    VersionsTabComponent,
    PackageNotFoundComponent,
    NoReleasesComponent,
    ServiceUnavailableComponent,
    ExportModalComponent,
    ShareModalComponent,
    TrustBadgeComponent,
    TrustPanelComponent,
  ],
  templateUrl: './package.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PackageComponent {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private packageService = inject(PackageService);
  private signalrService = inject(SignalrService);
  private toastService = inject(ToastService);
  private seo = inject(SeoService);
  private httpStatus = inject(HttpStatusService);

  readonly ClockIcon = Clock;
  readonly LinkIcon = Link;
  readonly ScaleIcon = Scale;
  readonly GithubIcon = Github;
  readonly InfoIcon = Info;
  readonly ShareIcon = Share2;
  readonly DownloadIcon = Download;

  packageId = signal('');
  registry = signal('nuget');
  version = signal<string | undefined>(undefined);
  state = signal<PageState>('loading');
  activeTab = signal<TabType>('releases');
  compareRange = signal<{ from: string; to: string } | null>(null);
  shareOpen = signal(false);
  exportOpen = signal(false);

  metadata = signal<PackageMetadata | null>(null);
  analysisData = signal<AnalysisResult | null>(null);
  analysisLoading = signal(false);
  analysisError = signal<string | null>(null);
  isUpdating = signal(false);

  trust = computed(() => this.metadata()?.trust ?? null);

  canExport = computed(() => this.activeTab() === 'releases' || (this.activeTab() === 'diff' && !!this.analysisData()));

  maliciousVersions = computed(() => maliciousVersions(this.trust(), this.metadata()?.versions.map(v => v.version) ?? []));

  private reload$ = new Subject<void>();
  private analyze$ = new Subject<AnalyzeRequest>();
  // Events older than this page belong to an earlier visit
  private seenSequence = this.signalrService.lastSequence;

  progressMessage = computed(() => {
    const sessionProgress = this.signalrService.progressMessage();
    const background = this.signalrService.backgroundProgress();

    if (sessionProgress) return sessionProgress;

    if (background && background.packageId === this.packageId()) {
      return background.message;
    }

    return '';
  });

  progressInfo = computed(() => {
    const message = this.progressMessage();
    if (!message) return null;

    const match = message.match(/\((\d+)\/(\d+)\)/);
    if (match) {
      const current = parseInt(match[1], 10);
      const total = parseInt(match[2], 10);
      const percentage = total > 0 ? Math.round((current / total) * 100) : 0;
      return { current, total, percentage, message };
    }
    return { current: 0, total: 0, percentage: 0, message };
  });

  registryBadgeClass = computed(() => {
    switch (this.registry().toLowerCase()) {
      case 'npm':
        return 'border-red-300 dark:border-red-700 text-red-600 dark:text-red-400 bg-red-50 dark:bg-red-900/20';
      case 'nuget':
        return 'border-blue-300 dark:border-blue-700 text-blue-600 dark:text-blue-400 bg-blue-50 dark:bg-blue-900/20';
      case 'pypi':
        return 'border-yellow-300 dark:border-yellow-700 text-yellow-600 dark:text-yellow-400 bg-yellow-50 dark:bg-yellow-900/20';
      case 'pub':
      case 'pub.dev':
        return 'border-purple-300 dark:border-purple-700 text-purple-600 dark:text-purple-400 bg-purple-50 dark:bg-purple-900/20';
      case 'packagist':
      case 'composer':
        return 'border-orange-300 dark:border-orange-700 text-orange-600 dark:text-orange-400 bg-orange-50 dark:bg-orange-900/20';
      default:
        return 'border-gray-200 dark:border-gray-700 text-gray-600 dark:text-gray-400';
    }
  });

  allVersions = computed(() => {
    const meta = this.metadata();
    if (!meta) return [];

    return meta.versions.map((v): VersionInfo => ({
      version: v.version,
      publishedDate: v.publishedDate,
      releaseNotes: v.releaseNotes ?? '',
      releaseNotesHtml: v.releaseNotesHtml ?? '',
      releaseNotesSource: v.releaseNotesSource ?? null,
      compiledFrom: v.compiledFrom ?? null,
      entries: v.entries ?? [],
      categories: (v.entries ?? []).map(e => e.category).filter((c, i, a) => a.indexOf(c) === i),
      isEmpty: v.isEmpty,
      isPrerelease: v.isPrerelease,
    }));
  });

  mappedVersions = computed(() => {
    const selectedVersion = this.version();
    const all = this.allVersions();
    return selectedVersion ? all.filter(v => v.version === selectedVersion) : all;
  });

  constructor() {
    this.followRoute();
    this.followFragment();
    this.followAnalyses();
    this.followPackageEvents();

    inject(DestroyRef).onDestroy(() => {
      this.seo.clearPageData();
      this.signalrService.resetProgress();
    });

    // The connection is app-wide; this only ensures it is up before an analysis needs it
    this.signalrService.startConnection().catch(() => undefined);
  }

  setTab(tab: TabType): void {
    this.activeTab.set(tab);
    this.router.navigate([], {
      fragment: tab,
      replaceUrl: true,
      preserveFragment: false
    });
  }

  retry(): void {
    this.state.set('loading');
    this.reload$.next();
  }

  onStartAnalysis(request: AnalyzeRequest): void {
    this.analyze$.next(request);
  }

  compareWithLatest(version: string): void {
    const latest = this.metadata()?.latestVersion;
    if (!latest || latest === version) return;

    this.compareRange.set({ from: version, to: latest });
    this.setTab('diff');
    this.onStartAnalysis({
      packageId: this.metadata()!.packageId,
      registry: this.registry(),
      currentVersion: version,
      targetVersion: latest,
      includePrerelease: false,
    });
  }

  private followRoute(): void {
    const target$ = this.route.paramMap.pipe(
      map(params => targetOf(params)),
      tap(target => this.showTarget(target)),
    );

    target$.pipe(
      // Another version of the same package is the same data
      distinctUntilChanged((a, b) => a.packageId === b.packageId && a.registry === b.registry),
      tap(() => this.resetForNewPackage()),
      switchMap(target => this.reload$.pipe(
        startWith(undefined),
        switchMap(() => this.packageService.getPackageMetadata(target.registry, target.packageId).pipe(
          map((value): Loaded<PackageMetadata> => ({ value })),
          catchError((failure: unknown) => of<Loaded<PackageMetadata>>({ failure })),
        )),
      )),
      takeUntilDestroyed(),
    ).subscribe(loaded => this.showMetadata(loaded));
  }

  private followFragment(): void {
    this.route.fragment.pipe(takeUntilDestroyed()).subscribe(fragment => {
      if (TABS.includes(fragment as TabType)) {
        this.activeTab.set(fragment as TabType);
      }
    });
  }

  private showTarget(target: PackageTarget): void {
    this.packageId.set(target.packageId);
    this.registry.set(target.registry);
    this.version.set(target.version);

    const fragment = this.route.snapshot.fragment;
    if (TABS.includes(fragment as TabType)) {
      this.activeTab.set(fragment as TabType);
    }

    const metadata = this.metadata();
    if (metadata && metadata.packageId.toLowerCase() === target.packageId.toLowerCase()) {
      this.seo.setPackage(metadata, target.registry, target.version);
    } else {
      this.seo.setPackagePlaceholder(target.packageId, target.registry, target.version);
    }
  }

  private resetForNewPackage(): void {
    this.state.set('loading');
    this.metadata.set(null);
    this.analysisData.set(null);
    this.analysisLoading.set(false);
    this.analysisError.set(null);
    this.isUpdating.set(false);
    this.compareRange.set(null);
    this.signalrService.resetProgress();
    this.seo.clearPageData();
  }

  private showMetadata(loaded: Loaded<PackageMetadata>): void {
    if ('failure' in loaded) {
      const failure = loadFailureOf(loaded.failure);
      if (failure === 'not-found') {
        this.httpStatus.notFound();
        this.seo.setNotFound(
          `${this.packageId()} — Package Not Found | pkgdiff`,
          `No package named ${this.packageId()} was found on ${this.registry()}.`);
      }
      this.state.set(failure);
      return;
    }

    const metadata = loaded.value;
    this.metadata.set(metadata);
    // Known package without a stable version (pulled or prerelease-only), not a service failure
    if (!metadata.versions || metadata.versions.length === 0) {
      this.state.set('no-versions');
      return;
    }

    this.state.set('loaded');
    this.seo.setPackage(metadata, this.registry(), this.version());
  }

  private followAnalyses(): void {
    this.analyze$.pipe(
      tap(() => {
        this.analysisLoading.set(true);
        this.analysisError.set(null);
      }),
      switchMap(request => this.packageService.analyze({
        ...request,
        connectionId: this.signalrService.connectionId() || undefined,
        progressToken: this.signalrService.progressToken() || undefined,
      }).pipe(
        map((value): Loaded<AnalysisResult> => ({ value })),
        catchError((failure: unknown) => of<Loaded<AnalysisResult>>({ failure })),
      )),
      takeUntilDestroyed(),
    ).subscribe(loaded => {
      this.analysisLoading.set(false);
      if ('failure' in loaded) {
        this.analysisData.set(null);
        this.analysisError.set(apiErrorMessage(loaded.failure, 'The comparison could not be completed. Please try again.'));
        return;
      }
      this.analysisData.set(loaded.value);
    });
  }

  private followPackageEvents(): void {
    effect(() => {
      const event = this.signalrService.packageUpdateStarted();
      untracked(() => {
        if (!this.isNewEventForThisPackage(event)) return;
        this.isUpdating.set(true);
        this.toastService.info(`Updating release notes for ${event.packageId}...`, 3000);
      });
    });

    effect(() => {
      const event = this.signalrService.packageUpdated();
      untracked(() => {
        if (!this.isNewEventForThisPackage(event)) return;
        this.isUpdating.set(false);
        this.toastService.success('Release notes updated successfully!', 3000);
        this.reload$.next();
      });
    });

    effect(() => {
      const event = this.signalrService.securityUpdated();
      untracked(() => {
        if (!this.isNewEventForThisPackage(event)) return;
        this.reload$.next();
      });
    });

    // Pushes sent while the connection was down are lost, an update's end included
    let reconnects = this.signalrService.reconnected();
    effect(() => {
      const count = this.signalrService.reconnected();
      untracked(() => {
        if (count === reconnects) return;
        reconnects = count;
        if (this.isUpdating()) {
          this.isUpdating.set(false);
          this.reload$.next();
        }
      });
    });
  }

  private isNewEventForThisPackage(event: PackageEvent | null): event is PackageEvent {
    if (!event || event.sequence <= this.seenSequence) return false;
    if (event.packageId !== this.packageId()) return false;
    this.seenSequence = event.sequence;
    return true;
  }
}
