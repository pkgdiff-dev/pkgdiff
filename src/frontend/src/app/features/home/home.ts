import { ChangeDetectionStrategy, Component, DestroyRef, afterNextRender, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';

import { LucideAngularModule, Search, Zap, GitCompare, Sparkles, ChevronDown } from 'lucide-angular';
import { LiveTickerComponent } from '../../shared/components/live-ticker/live-ticker';
import { PackageService } from '../../core/services/package.service';
import { SeoService } from '../../core/services/seo.service';
import { BrowserStorage } from '../../core/services/browser-storage.service';
import { RegistryInfo } from '../../core/models/package.models';

const LAST_REGISTRY_KEY = 'lastSelectedRegistry';
const RECENT_REGISTRIES_KEY = 'recentRegistries';
const MAX_RECENT = 4;

const EXAMPLES: Record<string, string[]> = {
  nuget: ['Newtonsoft.Json', 'Serilog', 'AutoMapper'],
  npm: ['express', 'react', 'lodash'],
  pypi: ['requests', 'numpy', 'django'],
  pub: ['riverpod', 'lints', 'code_builder'],
  'pub.dev': ['riverpod', 'lints', 'code_builder'],
};

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [FormsModule, RouterLink, LucideAngularModule, LiveTickerComponent],
  templateUrl: './home.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(document:click)': 'closeDropdown($event)',
    '(document:keydown.escape)': 'open.set(false)',
  },
})
export class HomeComponent {
  private router = inject(Router);
  private packageService = inject(PackageService);
  private seo = inject(SeoService);
  private storage = inject(BrowserStorage);
  private destroyRef = inject(DestroyRef);

  readonly SearchIcon = Search;
  readonly ZapIcon = Zap;
  readonly GitCompareIcon = GitCompare;
  readonly SparklesIcon = Sparkles;
  readonly ChevronDownIcon = ChevronDown;

  searchTerm = signal('');
  open = signal(false);
  loading = signal(true);

  registries = signal<RegistryInfo[]>([]);
  recentSearches = signal<RegistryInfo[]>([]);
  selectedRegistry = signal<RegistryInfo | null>(null);
  exampleSearches = computed(() => EXAMPLES[this.selectedRegistry()?.value ?? ''] ?? []);

  constructor() {
    this.seo.updateMeta({
      title: 'pkgdiff — Compare NuGet, npm, PyPI & Pub Packages',
      description: 'Free tool to compare package versions across NuGet, npm, PyPI & Pub. Track breaking changes, view release notes, and analyze version differences between any two versions.',
      url: '/',
    });

    // The selection depends on browser storage, so the server renders only the placeholder
    afterNextRender(() => this.loadRegistries());
  }

  toggleDropdown(event: Event): void {
    event.stopPropagation();
    this.open.update(open => !open);
  }

  closeDropdown(event: Event): void {
    const target = event.target;
    if (!(target instanceof Element) || !target.closest('.registry-dropdown')) {
      this.open.set(false);
    }
  }

  selectRegistry(registry: RegistryInfo): void {
    if (registry.disabled) return;
    this.selectedRegistry.set(registry);
    this.rememberRegistry();
    this.open.set(false);
  }

  handleSearch(): void {
    const registry = this.selectedRegistry();
    const term = this.searchTerm().trim();
    if (this.loading() || !registry || !term) return;

    this.rememberRegistry();
    this.router.navigate(['/package', registry.value, term]);
  }

  rememberRegistry(): void {
    const registry = this.selectedRegistry();
    if (!registry) return;

    this.storage.set(LAST_REGISTRY_KEY, registry.value);
    const recent = [registry.value, ...this.storedRecent().filter(value => value !== registry.value)].slice(0, MAX_RECENT);
    this.storage.set(RECENT_REGISTRIES_KEY, JSON.stringify(recent));
    this.showRecent(recent);
  }

  private loadRegistries(): void {
    this.loading.set(true);
    this.packageService.getRegistries()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (registries) => {
          this.registries.set(registries);

          const usable = (value: string | null | undefined) =>
            registries.find(registry => registry.value === value && !registry.disabled);
          const recent = this.storedRecent();
          this.selectedRegistry.set(
            usable(this.storage.get(LAST_REGISTRY_KEY))
            ?? usable(recent[0])
            ?? usable('nuget')
            ?? registries.find(registry => !registry.disabled)
            ?? null);

          this.showRecent(recent);
          this.loading.set(false);
        },
        error: () => {
          // The page still explains what the site does; only the search is unavailable
          this.loading.set(false);
        },
      });
  }

  private storedRecent(): string[] {
    const stored = this.storage.getJson<unknown>(RECENT_REGISTRIES_KEY);
    return Array.isArray(stored) ? stored.filter((value): value is string => typeof value === 'string') : [];
  }

  private showRecent(values: string[]): void {
    const registries = this.registries();
    this.recentSearches.set(values
      .map(value => registries.find(registry => registry.value === value))
      .filter((registry): registry is RegistryInfo => registry !== undefined)
      .slice(0, MAX_RECENT));
  }
}
