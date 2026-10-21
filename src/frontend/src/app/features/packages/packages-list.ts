import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal, computed } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { LucideAngularModule, Search, Box, Download, ExternalLink, Calendar, User, Info, ChevronLeft, ChevronRight, ChevronDown } from 'lucide-angular';

import { PackageService } from '../../core/services/package.service';
import { PackageMetadata, RegistryInfo } from '../../core/models/package.models';
import { SeoService } from '../../core/services/seo.service';

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
    selectedRegistries = signal<Set<string>>(new Set());
    sortBy = signal<'latest' | 'alphabetical'>('latest');
    sortOpen = signal(false);
    visibleCount = signal(20);

    readonly PAGE_SIZE = 20;

    readonly sortOptions = [
        { value: 'latest', label: 'Latest Release' },
        { value: 'alphabetical', label: 'Alphabetical' },
    ];

    filteredPackages = computed(() => {
        const query = this.searchQuery().toLowerCase();
        const activeRegistries = this.selectedRegistries();
        let result = this.packages();

        if (activeRegistries.size > 0) {
            result = result.filter(p => activeRegistries.has(p.registry));
        }

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

    visiblePackages = computed(() => {
        return this.filteredPackages().slice(0, this.visibleCount());
    });

    hasMore = computed(() => {
        return this.visibleCount() < this.filteredPackages().length;
    });

    remainingCount = computed(() => {
        return this.filteredPackages().length - this.visibleCount();
    });

    currentSortLabel = computed(() => {
        return this.sortOptions.find(o => o.value === this.sortBy())?.label ?? '';
    });

    constructor() {
        this.seo.updateMeta({
            title: 'Browse Packages — Compare Versions Across NuGet, npm, PyPI, Maven, Pub & Packagist | pkgdiff',
            description: 'Browse and compare packages from NuGet, npm, PyPI, Maven, Pub and Packagist registries. Search through thousands of packages, view detailed version history, track breaking changes, and analyze comprehensive release notes.',
            url: '/packages',
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
            },
            error: () => {
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
    }

    toggleRegistry(value: string) {
        const current = new Set(this.selectedRegistries());
        if (current.has(value)) {
            current.delete(value);
        } else {
            current.add(value);
        }
        this.selectedRegistries.set(current);
    }

    isRegistrySelected(value: string): boolean {
        return this.selectedRegistries().has(value);
    }

    onSortChange(value: string) {
        this.sortBy.set(value as 'latest' | 'alphabetical');
        this.sortOpen.set(false);
    }

    toggleSortDropdown(event: Event) {
        event.stopPropagation();
        this.sortOpen.update(v => !v);
    }

    loadMore() {
        this.visibleCount.update(c => c + this.PAGE_SIZE);
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
}
