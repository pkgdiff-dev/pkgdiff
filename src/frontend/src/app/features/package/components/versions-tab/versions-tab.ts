import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, inject, input, output, signal, viewChild } from '@angular/core';
import {
  LucideAngularModule, Search, X, Copy, Check, Terminal, GitCompare, ExternalLink, Link, ChevronLeft, ChevronRight,
  ChevronDown, Calendar, FileText,
} from 'lucide-angular';
import { VersionInfo } from '../../../../core/models/package.models';
import { AdvisorySeverity, PackageTrust } from '../../../../core/models/security.models';
import { advisoriesOf, isChecked, severityClass, sortBySeverity, worstSeverity } from '../../../../core/utils/trust.utils';
import { getReleaseLink, getSourceLabel } from '../../../../core/utils/release-link.utils';
import { installSnippets } from '../../../../core/utils/install-command.utils';
import { ToastService } from '../../../../core/services/toast.service';
import { ModalComponent } from '../../../../shared/components/modal/modal';
import { CompiledNotesNoticeComponent } from '../../../../shared/components/compiled-notes-notice/compiled-notes-notice';

type StabilityFilter = 'all' | 'stable' | 'prerelease';
type SecurityFilter = 'any' | 'vulnerable' | 'clean';
type SortOrder = 'version-desc' | 'version-asc' | 'date-desc';

const SORT_ORDERS: readonly SortOrder[] = ['version-desc', 'version-asc', 'date-desc'];
const PAGE_SIZE = 60;
const DAY_MS = 86_400_000;
const AGE_UNITS: readonly [Intl.RelativeTimeFormatUnit, number][] = [['year', 365], ['month', 30], ['week', 7], ['day', 1]];
const RELATIVE = new Intl.RelativeTimeFormat('en', { numeric: 'auto' });

interface VersionSecurity {
  count: number;
  worst: AdvisorySeverity | null;
  malicious: boolean;
}

interface VersionRow {
  info: VersionInfo;
  major: string;
  date: string | null;
  age: string;
  security: VersionSecurity | null;
  clean: boolean;
  isLatest: boolean;
  // Only versions older than the latest one can be upgraded from
  canCompare: boolean;
}

interface VersionGroup {
  major: string | null;
  rows: VersionRow[];
}

@Component({
  selector: 'app-versions-tab',
  standalone: true,
  imports: [LucideAngularModule, ModalComponent, CompiledNotesNoticeComponent],
  templateUrl: './versions-tab.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown)': 'onKeydown($event)' },
})
export class VersionsTabComponent {
  private toast = inject(ToastService);

  versions = input.required<VersionInfo[]>();
  packageId = input.required<string>();
  registry = input.required<string>();
  latestVersion = input<string | null>(null);
  gitHubUrl = input<string | null>(null);
  trust = input<PackageTrust | null>(null);
  // The version the page URL points at, if any
  selectedVersion = input<string | undefined>(undefined);

  compare = output<string>();

  readonly SearchIcon = Search;
  readonly XIcon = X;
  readonly CopyIcon = Copy;
  readonly CheckIcon = Check;
  readonly TerminalIcon = Terminal;
  readonly GitCompareIcon = GitCompare;
  readonly ExternalLinkIcon = ExternalLink;
  readonly LinkIcon = Link;
  readonly ChevronLeftIcon = ChevronLeft;
  readonly ChevronRightIcon = ChevronRight;
  readonly ChevronDownIcon = ChevronDown;
  readonly CalendarIcon = Calendar;
  readonly FileTextIcon = FileText;
  readonly severityClass = severityClass;

  private searchInput = viewChild<ElementRef<HTMLInputElement>>('searchInput');
  private modalTop = viewChild<ElementRef<HTMLElement>>('modalTop');

  query = signal('');
  stability = signal<StabilityFilter>('all');
  security = signal<SecurityFilter>('any');
  major = signal<string | null>(null);
  notesOnly = signal(false);
  sort = signal<SortOrder>('version-desc');
  displayCount = signal(PAGE_SIZE);
  openVersion = signal<string | null>(null);
  copied = signal<string | null>(null);

  private copiedTimer: ReturnType<typeof setTimeout> | undefined;

  trustReady = computed(() => this.trust()?.status === 'ready');

  rows = computed((): VersionRow[] => {
    const trust = this.trust();
    const ready = this.trustReady();
    const latest = this.latestVersion();
    const versions = this.versions();
    const latestIndex = latest ? versions.findIndex(v => v.version === latest) : -1;
    const now = Date.now();

    return versions.map((info, index): VersionRow => {
      const advisories = ready ? advisoriesOf(trust, info.version) : [];
      return {
        info,
        major: majorOf(info.version),
        date: info.publishedDate ? info.publishedDate.slice(0, 10) : null,
        age: relativeAge(info.publishedDate, now),
        security: advisories.length
          ? { count: advisories.length, worst: worstSeverity(advisories), malicious: advisories.some(a => a.isMalicious) }
          : null,
        clean: ready && advisories.length === 0 && isChecked(trust, info.version),
        isLatest: info.version === latest,
        canCompare: latestIndex !== -1 && index > latestIndex,
      };
    });
  });

  prereleaseCount = computed(() => this.rows().filter(r => r.info.isPrerelease).length);
  vulnerableCount = computed(() => this.rows().filter(r => r.security).length);
  cleanCount = computed(() => this.rows().filter(r => r.clean).length);
  withoutNotesCount = computed(() => this.rows().filter(r => r.info.isEmpty).length);

  majors = computed(() => {
    const counts = new Map<string, number>();
    for (const row of this.rows()) {
      counts.set(row.major, (counts.get(row.major) ?? 0) + 1);
    }
    return [...counts]
      .map(([major, count]) => ({ major, count }))
      .sort((a, b) => majorRank(b.major) - majorRank(a.major));
  });

  filtered = computed((): VersionRow[] => {
    const query = this.query().trim().toLowerCase().replace(/^v/, '');
    const stability = this.stability();
    const security = this.security();
    const major = this.major();
    const notesOnly = this.notesOnly();

    const matches = this.rows().filter(row =>
      (!query || row.info.version.toLowerCase().includes(query)) &&
      (stability === 'all' || (stability === 'prerelease') === row.info.isPrerelease) &&
      (security === 'any' || (security === 'vulnerable' ? !!row.security : row.clean)) &&
      (!major || row.major === major) &&
      (!notesOnly || !row.info.isEmpty));

    switch (this.sort()) {
      case 'version-asc':
        return matches.reverse();
      case 'date-desc':
        return matches.sort((a, b) => (b.info.publishedDate ?? '').localeCompare(a.info.publishedDate ?? ''));
      default:
        return matches;
    }
  });

  filteredMajorCounts = computed(() => {
    const counts = new Map<string, number>();
    for (const row of this.filtered()) {
      counts.set(row.major, (counts.get(row.major) ?? 0) + 1);
    }
    return counts;
  });

  // Version order keeps a major's versions together, so they get a heading; release-date order does not
  groups = computed((): VersionGroup[] => {
    const visible = this.filtered().slice(0, this.displayCount());
    if (this.sort() === 'date-desc') return visible.length ? [{ major: null, rows: visible }] : [];

    const groups: VersionGroup[] = [];
    for (const row of visible) {
      const last = groups[groups.length - 1];
      if (last && last.major === row.major) {
        last.rows.push(row);
      } else {
        groups.push({ major: row.major, rows: [row] });
      }
    }
    return groups;
  });

  remaining = computed(() => Math.max(0, this.filtered().length - this.displayCount()));

  hasFilters = computed(() =>
    !!this.query().trim() || this.stability() !== 'all' || this.security() !== 'any' || !!this.major() || this.notesOnly());

  openRow = computed(() => {
    const version = this.openVersion();
    return version ? this.rows().find(r => r.info.version === version) ?? null : null;
  });

  // Previous and next follow the list as filtered and sorted, so stepping through stays inside what is shown
  private openIndex = computed(() => {
    const version = this.openVersion();
    return version ? this.filtered().findIndex(r => r.info.version === version) : -1;
  });
  previous = computed(() => {
    const index = this.openIndex();
    return index > 0 ? this.filtered()[index - 1] : null;
  });
  next = computed(() => {
    const index = this.openIndex();
    return index !== -1 && index < this.filtered().length - 1 ? this.filtered()[index + 1] : null;
  });
  position = computed(() => this.openIndex() + 1);

  openAdvisories = computed(() => {
    const row = this.openRow();
    return row ? sortBySeverity(advisoriesOf(this.trust(), row.info.version)) : [];
  });

  openSnippets = computed(() => {
    const row = this.openRow();
    return row ? installSnippets(this.registry(), this.packageId(), row.info.version) : [];
  });

  openReleaseLink = computed(() => {
    const row = this.openRow();
    return row
      ? getReleaseLink(this.packageId(), row.info.version, row.info.releaseNotesSource, this.gitHubUrl(), this.registry())
      : null;
  });

  openSourceLabel = computed(() => getSourceLabel(this.openRow()?.info.releaseNotesSource ?? null, this.registry()));

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.copiedTimer));
  }

  setQuery(value: string): void {
    this.query.set(value);
    this.displayCount.set(PAGE_SIZE);
  }

  setStability(value: StabilityFilter): void {
    this.stability.set(value);
    this.displayCount.set(PAGE_SIZE);
  }

  setSecurity(value: SecurityFilter): void {
    this.security.set(this.security() === value ? 'any' : value);
    this.displayCount.set(PAGE_SIZE);
  }

  setMajor(value: string | null): void {
    this.major.set(value);
    this.displayCount.set(PAGE_SIZE);
  }

  toggleNotesOnly(): void {
    this.notesOnly.update(v => !v);
    this.displayCount.set(PAGE_SIZE);
  }

  onSortChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value as SortOrder;
    if (SORT_ORDERS.includes(value)) {
      this.sort.set(value);
      this.displayCount.set(PAGE_SIZE);
    }
  }

  clearFilters(): void {
    this.query.set('');
    this.stability.set('all');
    this.security.set('any');
    this.major.set(null);
    this.notesOnly.set(false);
    this.displayCount.set(PAGE_SIZE);
  }

  showMore(): void {
    this.displayCount.update(c => c + PAGE_SIZE);
  }

  open(version: string): void {
    this.openVersion.set(version);
    this.modalTop()?.nativeElement.scrollIntoView({ block: 'start' });
  }

  openPrevious(): void {
    const row = this.previous();
    if (row) this.open(row.info.version);
  }

  openNext(): void {
    const row = this.next();
    if (row) this.open(row.info.version);
  }

  close(): void {
    this.openVersion.set(null);
  }

  compareWithLatest(version: string): void {
    this.openVersion.set(null);
    this.compare.emit(version);
  }

  installCommand(version: string): string {
    return installSnippets(this.registry(), this.packageId(), version)[0]?.code ?? version;
  }

  versionUrl(version: string): string {
    return `https://pkgdiff.dev/package/${this.registry()}/${this.packageId()}/${version}`;
  }

  async copy(text: string, key: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(text);
    } catch {
      this.toast.error('Could not copy to the clipboard.');
      return;
    }
    this.copied.set(key);
    clearTimeout(this.copiedTimer);
    this.copiedTimer = setTimeout(() => this.copied.set(null), 1500);
  }

  segmentClass(active: boolean): string {
    return active
      ? 'bg-white dark:bg-gray-800 text-gray-900 dark:text-white shadow-sm'
      : 'text-gray-500 dark:text-gray-400 hover:text-gray-800 dark:hover:text-gray-200';
  }

  chipClass(active: boolean): string {
    return active
      ? 'border-primary/60 bg-primary/10 text-primary'
      : 'border-gray-200 dark:border-gray-800 text-gray-600 dark:text-gray-400 hover:border-gray-300 dark:hover:border-gray-600 hover:text-gray-900 dark:hover:text-gray-200';
  }

  // "/" jumps to the filter box; with release notes open, the arrow keys step through the list
  onKeydown(event: KeyboardEvent): void {
    const target = event.target as HTMLElement | null;
    const typing = !!target && (['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName) || target.isContentEditable);
    if (typing || event.ctrlKey || event.metaKey || event.altKey) return;

    if (this.openVersion()) {
      if (event.key === 'ArrowLeft') this.openPrevious();
      if (event.key === 'ArrowRight') this.openNext();
      return;
    }

    if (event.key === '/') {
      event.preventDefault();
      this.searchInput()?.nativeElement.focus();
    }
  }
}

function majorOf(version: string): string {
  return /^v?(\d+)/i.exec(version)?.[1] ?? 'other';
}

function majorRank(major: string): number {
  return major === 'other' ? -1 : Number(major);
}

function relativeAge(date: string | null, now: number): string {
  const time = date ? Date.parse(date) : NaN;
  if (Number.isNaN(time)) return '';
  const days = Math.floor((now - time) / DAY_MS);
  for (const [unit, size] of AGE_UNITS) {
    if (days >= size) return RELATIVE.format(-Math.floor(days / size), unit);
  }
  return 'today';
}
