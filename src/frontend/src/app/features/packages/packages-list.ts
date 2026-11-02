import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal, computed } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Params, Router, RouterModule } from '@angular/router';
import { LucideAngularModule, Search, Box, Download, ExternalLink, Calendar, User, Info, ChevronLeft, ChevronRight, ChevronDown } from 'lucide-angular';

import { PackageService } from '../../core/services/package.service';
import { PackageMetadata, RegistryInfo } from '../../core/models/package.models';
import { SeoService } from '../../core/services/seo.service';
import { HttpStatusService } from '../../core/services/http-status.service';
import { pageWindow } from '../../core/utils/pagination.utils';

const REGISTRY_LABELS: Record<string, string> = {
    nuget: 'NuGet',
    npm: 'npm',
    pypi: 'PyPI',
    maven: 'Maven',
    pub: 'pub.dev',
    packagist: 'Packagist',
};

const REGISTRY_ALIASES: Record<string, string> = {
    'pub.dev': 'pub',
    composer: 'packagist',
};

function registryOf(value: string | null): string | null {
    const registry = value?.toLowerCase() ?? '';
    const canonical = Object.hasOwn(REGISTRY_ALIASES, registry) ? REGISTRY_ALIASES[registry] : registry;
    return Object.hasOwn(REGISTRY_LABELS, canonical) ? canonical : null;
}

@Component({
    selector: 'app-packages-list',
    standalone: true,
    imports: [CommonModule, RouterModule, LucideAngularModule],
    templateUrl: './packages-list.html',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        '(document:click)': 'sortOpen.set(false)',
        '(document:keydown.escape)': 'sortOpen.set(false)',
    },
})
export class PackagesListComponent {
    private packageService = inject(PackageService);
    private seo = inject(SeoService);
    private httpStatus = inject(HttpStatusService);
    private route = inject(ActivatedRoute);
    private router = inject(Router);
    private destroyRef = inject(DestroyRef);

    readonly SearchIcon = Search;
    readonly BoxIcon = Box;
    readonly DownloadIcon = Download;
    readonly ExternalLinkIcon = ExternalLink;
    readonly CalendarIcon = Calendar;
    readonly UserIcon = User;
    readonly InfoIcon = Info;
    readonly ChevronLeftIcon = ChevronLeft;
    readonly ChevronRightIcon = ChevronRight;
    readonly ChevronDownIcon = ChevronDown;
    packages = signal<PackageMetadata[]>([]);
    registries = signal<RegistryInfo[]>([]);
    isLoading = signal(true);
    loadFailed = signal(false);
    searchQuery = signal('');
    selectedRegistry = signal<string | null>(null);
    requestedPage = signal(1);
    sortBy = signal<'latest' | 'alphabetical'>('latest');
    sortOpen = signal(false);

    readonly PAGE_SIZE = 20;

    readonly sortOptions = [
        { value: 'latest', label: 'Latest Release' },
        { value: 'alphabetical', label: 'Alphabetical' },
    ];

    registryPackages = computed(() => {
        const registry = this.selectedRegistry();
        return registry ? this.packages().filter(p => p.registry === registry) : this.packages();
    });

    filteredPackages = computed(() => {
        const query = this.searchQuery().toLowerCase();
        let result = this.registryPackages();

        if (query) {
            result = result.filter(p =>
                p.packageId.toLowerCase().includes(query) ||
                (p.description && p.description.toLowerCase().includes(query)) ||
                (p.authors && p.authors.toLowerCase().includes(query))
            );
        }

        if (this.sortBy() === 'alphabetical') {
            result = [...result].sort((a, b) => a.packageId.localeCompare(b.packageId));
        }

        return result;
    });

    totalPages = computed(() => Math.max(1, Math.ceil(this.filteredPackages().length / this.PAGE_SIZE)));

    currentPage = computed(() => Math.min(this.requestedPage(), this.totalPages()));

    pages = computed(() => pageWindow(this.currentPage(), this.totalPages()));

    visiblePackages = computed(() => {
        const start = (this.currentPage() - 1) * this.PAGE_SIZE;
        return this.filteredPackages().slice(start, start + this.PAGE_SIZE);
    });

    heading = computed(() => {
        const registry = this.selectedRegistry();
        return registry ? `${REGISTRY_LABELS[registry]} packages` : 'Browse packages';
    });

    intro = computed(() => {
        const registry = this.selectedRegistry();
        const scope = registry ? `${REGISTRY_LABELS[registry]} packages` : 'packages across NuGet, npm, PyPI, Maven, pub.dev and Packagist';
        const count = this.registryPackages().length;
        return count > 0
            ? `Release notes and version history for ${count} ${scope}.`
            : `Release notes and version history for ${scope}.`;
    });

    currentSortLabel = computed(() => {
        return this.sortOptions.find(o => o.value === this.sortBy())?.label ?? '';
    });

    constructor() {
        // Registry and page live in the URL so every list page can be linked and crawled
        this.route.queryParamMap.pipe(takeUntilDestroyed()).subscribe(params => {
            this.selectedRegistry.set(registryOf(params.get('registry')));
            this.requestedPage.set(Math.max(1, Number.parseInt(params.get('page') ?? '1', 10) || 1));
            this.updateSeo();
        });

        this.loadPackages();
        this.loadRegistries();
    }

    hideBrokenIcon(event: Event) {
        (event.target as HTMLElement).hidden = true;
    }

    loadPackages() {
        this.isLoading.set(true);
        this.loadFailed.set(false);
        this.packageService.getCachedPackages().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
            next: (data) => {
                this.packages.set(data);
                this.isLoading.set(false);
                this.updateSeo();
            },
            error: () => {
                this.httpStatus.serviceUnavailable();
                this.seo.setUnavailable();
                this.loadFailed.set(true);
                this.isLoading.set(false);
            }
        });
    }

    loadRegistries() {
        this.packageService.getRegistries().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
            next: (data) => this.registries.set(data),
            // Without the registries the list still works; only the registry filter is missing
            error: () => this.registries.set([]),
        });
    }

    onSearch(event: Event) {
        const input = event.target as HTMLInputElement;
        this.searchQuery.set(input.value);
        this.returnToFirstPage();
    }

    isRegistrySelected(value: string): boolean {
        return this.selectedRegistry() === value;
    }

    registryParams(value: string): Params {
        return this.isRegistrySelected(value) ? {} : { registry: value };
    }

    pageParams(page: number): Params {
        const params: Params = {};
        const registry = this.selectedRegistry();
        if (registry) params['registry'] = registry;
        if (page > 1) params['page'] = page;
        return params;
    }

    onSortChange(value: string) {
        this.sortBy.set(value as 'latest' | 'alphabetical');
        this.sortOpen.set(false);
        this.returnToFirstPage();
    }

    toggleSortDropdown(event: Event) {
        event.stopPropagation();
        this.sortOpen.update(v => !v);
    }

    registryBadgeClass(registry: string): string {
        switch (registry.toLowerCase()) {
            case 'npm':
                return 'border-red-300 dark:border-red-700 text-red-600 dark:text-red-400 bg-red-50 dark:bg-red-900/20';
            case 'nuget':
                return 'border-blue-300 dark:border-blue-700 text-blue-600 dark:text-blue-400 bg-blue-50 dark:bg-blue-900/20';
            case 'pypi':
                return 'border-yellow-300 dark:border-yellow-700 text-yellow-600 dark:text-yellow-400 bg-yellow-50 dark:bg-yellow-900/20';
            case 'pub':
            case 'pub.dev':
                return 'border-purple-300 dark:border-purple-700 text-purple-600 dark:text-purple-400 bg-purple-50 dark:bg-purple-900/20';
            case 'maven':
                return 'border-rose-300 dark:border-rose-700 text-rose-600 dark:text-rose-400 bg-rose-50 dark:bg-rose-900/20';
            case 'packagist':
            case 'composer':
                return 'border-orange-300 dark:border-orange-700 text-orange-600 dark:text-orange-400 bg-orange-50 dark:bg-orange-900/20';
            default:
                return 'border-gray-200 dark:border-gray-700 text-gray-600 dark:text-gray-400';
        }
    }

    private returnToFirstPage() {
        if (this.requestedPage() > 1) {
            this.router.navigate([], { relativeTo: this.route, queryParams: this.pageParams(1), replaceUrl: true });
        }
    }

    private updateSeo() {
        if (this.loadFailed()) {
            return;
        }

        const page = this.requestedPage();
        const lastPage = Math.max(1, Math.ceil(this.registryPackages().length / this.PAGE_SIZE));

        if (!this.isLoading() && page > lastPage) {
            this.httpStatus.notFound();
            this.seo.setNotFound('Page Not Found | pkgdiff', 'This page of the package list does not exist.');
            return;
        }

        const registry = this.selectedRegistry();
        const label = registry ? REGISTRY_LABELS[registry] : null;
        const pageSuffix = page > 1 ? ` — Page ${page}` : '';
        const query = new URLSearchParams(this.pageParams(page)).toString();

        this.seo.updateMeta({
            title: label
                ? `${label} Packages: Release Notes and Versions${pageSuffix} | pkgdiff`
                : `Browse Packages: Release Notes and Versions${pageSuffix} | pkgdiff`,
            description: label
                ? `Browse ${label} packages on pkgdiff. See version history, breaking changes and categorized release notes, and compare any two versions.`
                : 'Browse packages from NuGet, npm, PyPI, Maven, pub.dev and Packagist. See version history, breaking changes and categorized release notes.',
            url: query ? `/packages?${query}` : '/packages',
        });

        if (!this.isLoading() && this.registryPackages().length === 0) {
            this.seo.setNoIndex();
        }
    }
}
