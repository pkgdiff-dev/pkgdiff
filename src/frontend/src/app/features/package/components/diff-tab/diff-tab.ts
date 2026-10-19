import { ChangeDetectionStrategy, Component, computed, input, output, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule, PlusCircle, MinusCircle, Info, ArrowLeftRight, ExternalLink, ChevronDown, AlertCircle, RefreshCw } from 'lucide-angular';
import { VersionInfo, AnalysisResult, AnalyzeRequest } from '../../../../core/models/package.models';
import { getCategoryStyle } from '../../../../core/utils/category.utils';
import { getReleaseLink, getSourceLabel } from '../../../../core/utils/release-link.utils';
import { PackageTrust } from '../../../../core/models/security.models';
import { UpgradeSecurityComponent } from '../upgrade-security/upgrade-security';
import { CompiledNotesNoticeComponent } from '../../../../shared/components/compiled-notes-notice/compiled-notes-notice';

@Component({
  selector: 'app-diff-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule, UpgradeSecurityComponent, CompiledNotesNoticeComponent],
  templateUrl: './diff-tab.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(document:click)': 'closeDropdowns()',
    '(document:keydown.escape)': 'closeDropdowns()',
  },
})
export class DiffTabComponent implements OnInit {
  versions = input.required<VersionInfo[]>();
  packageId = input.required<string>();
  registry = input.required<string>();
  gitHubUrl = input<string | null>(null);
  analysisData = input<AnalysisResult | null>(null);
  // Updates live, unlike the copy carried by the analysis
  trust = input<PackageTrust | null | undefined>(null);
  loading = input(false);
  error = input<string | null>(null);
  // Without these the two newest versions are picked
  initialFrom = input<string | undefined>(undefined);
  initialTo = input<string | undefined>(undefined);

  onAnalyze = output<AnalyzeRequest>();

  readonly PlusCircleIcon = PlusCircle;
  readonly MinusCircleIcon = MinusCircle;
  readonly InfoIcon = Info;
  readonly ArrowIcon = ArrowLeftRight;
  readonly ExternalLinkIcon = ExternalLink;
  readonly ChevronDownIcon = ChevronDown;
  readonly AlertIcon = AlertCircle;
  readonly RefreshIcon = RefreshCw;
  readonly getCategoryStyle = getCategoryStyle;
  readonly getReleaseLink = getReleaseLink;
  readonly getSourceLabel = getSourceLabel;

  fromVersion = signal('');
  toVersion = signal('');
  fromOpen = signal(false);
  toOpen = signal(false);
  expandedVersions = signal<Record<string, boolean>>({});

  diffResult = computed(() => this.analysisData());
  summary = computed(() => this.diffResult()?.summary);
  diffVersions = computed(() => this.diffResult()?.versions ?? []);
  diffTrust = computed(() => this.trust() ?? this.diffResult()?.trust ?? null);
  diffVersionList = computed(() => this.diffVersions().map(v => v.version));
  versionList = computed(() => this.versions().map((v) => v.version));

  ngOnInit(): void {
    const from = this.initialFrom();
    const to = this.initialTo();
    if (from && to) {
      this.fromVersion.set(from);
      this.toVersion.set(to);
      return;
    }

    const list = this.versionList();
    if (list.length >= 2) {
      this.fromVersion.set(list[1]);
      this.toVersion.set(list[0]);
    }
  }

  toggleFromDropdown(event: Event) {
    event.stopPropagation();
    this.fromOpen.update(v => !v);
    this.toOpen.set(false);
  }

  toggleToDropdown(event: Event) {
    event.stopPropagation();
    this.toOpen.update(v => !v);
    this.fromOpen.set(false);
  }

  selectFromVersion(v: string) {
    this.fromVersion.set(v);
    this.fromOpen.set(false);
    this.onVersionChange();
  }

  selectToVersion(v: string) {
    this.toVersion.set(v);
    this.toOpen.set(false);
    this.onVersionChange();
  }

  closeDropdowns(): void {
    this.fromOpen.set(false);
    this.toOpen.set(false);
  }

  onVersionChange(): void {
    if (!this.fromVersion() || !this.toVersion()) return;

    this.onAnalyze.emit({
      packageId: this.packageId(),
      registry: this.registry(),
      currentVersion: this.fromVersion(),
      targetVersion: this.toVersion(),
      includePrerelease: false
    });
  }

  toggleExpand(version: string): void {
    this.expandedVersions.update(prev => ({
      ...prev,
      [version]: !prev[version]
    }));
  }

  isExpanded(version: string): boolean {
    return !!this.expandedVersions()[version];
  }
}
