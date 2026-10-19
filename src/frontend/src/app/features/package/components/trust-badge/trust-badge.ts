import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { LucideAngularModule, ShieldCheck, ShieldAlert, Loader2 } from 'lucide-angular';
import { PackageTrust } from '../../../../core/models/security.models';
import { advisoriesOf, isChecked, severityClass, worstSeverity } from '../../../../core/utils/trust.utils';

// Compact vulnerability status of one version, for the package header
@Component({
  selector: 'app-trust-badge',
  standalone: true,
  imports: [LucideAngularModule],
  templateUrl: './trust-badge.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TrustBadgeComponent {
  trust = input<PackageTrust | null | undefined>(null);
  version = input<string | null | undefined>(null);

  readonly ShieldCheckIcon = ShieldCheck;
  readonly ShieldAlertIcon = ShieldAlert;
  readonly LoaderIcon = Loader2;

  advisories = computed(() => {
    const version = this.version();
    return version ? advisoriesOf(this.trust(), version) : [];
  });

  state = computed(() => {
    const trust = this.trust();
    const version = this.version();
    if (!trust || trust.status === 'unavailable') return 'unavailable';
    if (trust.status === 'pending') return 'pending';
    if (!version || !isChecked(trust, version)) return 'unavailable';
    return this.advisories().length > 0 ? 'vulnerable' : 'clean';
  });

  badgeClass = computed(() => severityClass(worstSeverity(this.advisories())));
}
