import { ChangeDetectionStrategy, Component, input, signal } from '@angular/core';
import { SlicePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { LucideAngularModule, Github, ExternalLink, ArrowLeft } from 'lucide-angular';
import { VersionInfo, ChangeCategory } from '../../../../core/models/package.models';
import { getCategoryStyle } from '../../../../core/utils/category.utils';
import { getReleaseLink, getSourceLabel } from '../../../../core/utils/release-link.utils';
import { CompiledNotesNoticeComponent } from '../../../../shared/components/compiled-notes-notice/compiled-notes-notice';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-releases-tab',
  standalone: true,
  imports: [SlicePipe, LucideAngularModule, RouterLink, CompiledNotesNoticeComponent],
  templateUrl: './releases-tab.html',
})
export class ReleasesTabComponent {
  versions = input.required<VersionInfo[]>();
  packageId = input.required<string>();
  registry = input<string>('nuget');
  gitHubUrl = input<string | null>(null);
  selectedVersion = input<string | undefined>(undefined);

  readonly GithubIcon = Github;
  readonly ExternalLinkIcon = ExternalLink;
  readonly ArrowLeftIcon = ArrowLeft;
  readonly getCategoryStyle = getCategoryStyle;
  readonly getReleaseLink = getReleaseLink;
  readonly getSourceLabel = getSourceLabel;

  displayCount = signal(10);

  get visibleVersions(): VersionInfo[] {
    return this.versions().slice(0, this.displayCount());
  }

  expandedVersions = signal<Record<string, boolean>>({});

  toggleExpand(version: string): void {
    this.expandedVersions.update(prev => ({
      ...prev,
      [version]: !prev[version]
    }));
  }

  isExpanded(version: string): boolean {
    return !!this.expandedVersions()[version];
  }

  get hasMore(): boolean {
    return this.versions().length > this.displayCount();
  }

  loadMore(): void {
    this.displayCount.update((c) => c + 10);
  }
}
