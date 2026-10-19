import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LucideAngularModule, AlertCircle, ShieldAlert, ArrowRight, ExternalLink } from 'lucide-angular';
import { PackageService } from '../../../../core/services/package.service';
import { PackageSuggestion } from '../../../../core/models/package.models';
import { SecurityAdvisory } from '../../../../core/models/security.models';
import { severityClass, sortBySeverity } from '../../../../core/utils/trust.utils';

// A package the registry knows but that has no stable version to compare. Either the registry pulled it
// for security reasons and left only a placeholder (npm's "0.0.1-security" for malware), or it has only
// prereleases so far.
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-no-stable-versions',
  standalone: true,
  imports: [LucideAngularModule, RouterLink],
  templateUrl: './no-stable-versions.html',
})
export class NoStableVersionsComponent implements OnInit {
  readonly packageName = input.required<string>();
  readonly registry = input.required<string>();
  readonly latestVersion = input<string | null>(null);
  readonly advisories = input<SecurityAdvisory[]>([]);

  private packageService = inject(PackageService);

  readonly AlertIcon = AlertCircle;
  readonly ShieldAlertIcon = ShieldAlert;
  readonly ArrowRightIcon = ArrowRight;
  readonly ExternalLinkIcon = ExternalLink;
  readonly severityClass = severityClass;

  readonly removedForSecurity = computed(() =>
    (this.latestVersion() ?? '').endsWith('-security') || this.advisories().some(a => a.isMalicious));

  readonly sortedAdvisories = computed(() => sortBySeverity(this.advisories()));

  // Removed packages are mostly typosquats: the name they imitate is the useful way on
  suggestions = signal<PackageSuggestion[]>([]);

  ngOnInit(): void {
    const name = this.packageName();
    if (name.length < 2) return;

    this.packageService.getSuggestions(this.registry(), name).subscribe({
      next: results => this.suggestions.set(
        results.filter(s => s.packageId.toLowerCase() !== name.toLowerCase()).slice(0, 3)),
      error: () => this.suggestions.set([]),
    });
  }
}
