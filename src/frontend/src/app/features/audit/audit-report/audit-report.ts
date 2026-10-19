import { ChangeDetectionStrategy, Component, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { DatePipe, isPlatformBrowser } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Meta } from '@angular/platform-browser';
import { catchError, map, of, switchMap, takeWhile, timer } from 'rxjs';
import { LucideAngularModule, ShieldAlert, ShieldCheck, ShieldX, Link2, Download, RotateCcw } from 'lucide-angular';
import { AuditService } from '../../../core/services/audit.service';
import { SeoService } from '../../../core/services/seo.service';
import { ToastService } from '../../../core/services/toast.service';
import { AuditItem, AuditReport } from '../../../core/models/audit.models';
import { loadFailureOf } from '../../../core/utils/http-error.utils';
import { advisoryLine, severityClass, sortBySeverity, worstSeverity } from '../../../core/utils/trust.utils';
import { AdvisorySeverity } from '../../../core/models/security.models';

type Filter = 'all' | 'vulnerable' | 'clean' | 'not-found';
type Loaded = { report: AuditReport } | { failure: 'not-found' | 'error' };

const POLL_MS = 1500;
const SEVERITY_ORDER: (AdvisorySeverity | null)[] = ['critical', 'high', 'medium', 'low', 'unknown', null];

@Component({
  selector: 'app-audit-report',
  standalone: true,
  imports: [DatePipe, RouterLink, LucideAngularModule],
  templateUrl: './audit-report.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuditReportComponent {
  private audits = inject(AuditService);
  private toast = inject(ToastService);

  readonly AlertIcon = ShieldAlert;
  readonly CheckIcon = ShieldCheck;
  readonly MalwareIcon = ShieldX;
  readonly LinkIcon = Link2;
  readonly DownloadIcon = Download;
  readonly AgainIcon = RotateCcw;
  readonly severityClass = severityClass;
  readonly worstSeverity = worstSeverity;

  report = signal<AuditReport | null>(null);
  failure = signal<'not-found' | 'error' | null>(null);
  filter = signal<Filter>('all');

  done = computed(() => ['completed', 'failed'].includes(this.report()?.status ?? ''));
  percent = computed(() => {
    const report = this.report();
    return report && report.total > 0 ? Math.round((report.progress / report.total) * 100) : 0;
  });

  sortedItems = computed(() => {
    const items = [...(this.report()?.items ?? [])];
    const rank = (item: AuditItem) => {
      if (item.isMalicious) return -1;
      if (item.status === 'vulnerable') return SEVERITY_ORDER.indexOf(worstSeverity(item.advisories));
      if (item.status === 'clean') return 10;
      return 20;
    };
    return items.sort((a, b) => rank(a) - rank(b) || a.name.localeCompare(b.name));
  });

  visibleItems = computed(() => {
    const filter = this.filter();
    return this.sortedItems().filter(item =>
      filter === 'all' ? true
        : filter === 'not-found' ? item.status === 'not-found' || item.status === 'unchecked'
          : item.status === filter);
  });

  constructor() {
    const seo = inject(SeoService);
    seo.updateMeta({
      title: 'Dependency audit report | pkgdiff',
      description: 'Known vulnerabilities of the packages of a dependency file, with the versions that fix them.',
    });
    // Reports are private to whoever has the link
    inject(Meta).updateTag({ name: 'robots', content: 'noindex, nofollow' });

    if (!isPlatformBrowser(inject(PLATFORM_ID))) return;

    const id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';
    timer(0, POLL_MS)
      .pipe(
        switchMap(() => this.audits.get(id).pipe(
          map((report): Loaded => ({ report })),
          catchError((error: unknown) => of<Loaded>({ failure: loadFailureOf(error) })),
        )),
        takeWhile(loaded => 'report' in loaded && !['completed', 'failed'].includes(loaded.report.status), true),
        takeUntilDestroyed(),
      )
      .subscribe(loaded => {
        if ('failure' in loaded) {
          this.failure.set(loaded.failure);
          return;
        }
        this.failure.set(null);
        this.report.set(loaded.report);
      });
  }

  sortedAdvisories(item: AuditItem) {
    return sortBySeverity(item.advisories);
  }

  copyLink(): void {
    navigator.clipboard.writeText(window.location.href).then(
      () => this.toast.success('Report link copied.', 2000),
      () => this.toast.error('The link could not be copied.'),
    );
  }

  exportMarkdown(): void {
    const report = this.report();
    if (!report) return;
    const s = report.summary;
    let md = `# Dependency audit (${report.registry})\n\n`;
    md += `Checked ${new Date(report.completedAt ?? report.createdAt).toISOString().slice(0, 10)} with pkgdiff (https://pkgdiff.dev), vulnerability data from OSV.dev.\n\n`;
    md += `- Packages: ${s.packages}\n- Vulnerable: ${s.vulnerable}\n- Malicious: ${s.malicious}\n- Not found: ${s.notFound}\n`;
    md += `- Advisories: ${s.critical} critical, ${s.high} high, ${s.medium} medium, ${s.low} low, ${s.unknownSeverity} unrated\n\n`;
    for (const item of this.sortedItems().filter(i => i.status === 'vulnerable')) {
      md += `## ${item.name} ${item.version ?? ''}${item.isMalicious ? ' — MALWARE' : ''}\n\n`;
      if (item.safeVersion) md += `Upgrade to **${item.safeVersion}** or later.\n\n`;
      if (item.hasUnfixedAdvisory) md += `At least one advisory has no fixed version yet.\n\n`;
      for (const advisory of sortBySeverity(item.advisories)) md += `- ${advisoryLine(advisory)}\n`;
      md += '\n';
    }
    const notFound = this.sortedItems().filter(i => i.status === 'not-found').map(i => i.name);
    if (notFound.length) md += `## Not found in the registry\n\n${notFound.map(n => `- ${n}`).join('\n')}\n`;

    const blob = new Blob([md], { type: 'text/markdown' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `pkgdiff-audit-${report.registry}-${report.id.slice(0, 8)}.md`;
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
  }
}
