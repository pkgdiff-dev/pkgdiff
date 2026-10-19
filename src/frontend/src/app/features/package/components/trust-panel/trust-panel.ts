import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { PackageTrust } from '../../../../core/models/security.models';
import { advisoriesOf, isChecked, severityClass, sortBySeverity } from '../../../../core/utils/trust.utils';

// Security and health of a package, for the metadata tab
@Component({
  selector: 'app-trust-panel',
  standalone: true,
  imports: [DatePipe, DecimalPipe],
  templateUrl: './trust-panel.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TrustPanelComponent {
  trust = input<PackageTrust | null | undefined>(null);
  version = input<string | null | undefined>(null);
  registry = input<string>('');

  readonly severityClass = severityClass;

  status = computed(() => this.trust()?.status ?? 'unavailable');
  health = computed(() => this.trust()?.health ?? null);
  checked = computed(() => {
    const version = this.version();
    return !!version && isChecked(this.trust(), version);
  });
  advisories = computed(() => {
    const version = this.version();
    return version ? sortBySeverity(advisoriesOf(this.trust(), version)) : [];
  });
  scoreClass = computed(() => {
    const score = this.health()?.scorecardScore;
    if (score == null) return 'text-gray-500';
    if (score >= 7) return 'text-green-600 dark:text-green-400';
    if (score >= 4) return 'text-yellow-600 dark:text-yellow-400';
    return 'text-red-600 dark:text-red-400';
  });
  isPub = computed(() => this.registry().toLowerCase().startsWith('pub'));
}
