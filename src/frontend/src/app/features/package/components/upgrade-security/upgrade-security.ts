import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { LucideAngularModule, ShieldCheck, ShieldAlert, ShieldX } from 'lucide-angular';
import { PackageTrust } from '../../../../core/models/security.models';
import {
  advisoriesOf,
  fixedBy,
  introducedBy,
  isChecked,
  maliciousVersions,
  severityClass,
  sortBySeverity,
  worstSeverity,
} from '../../../../core/utils/trust.utils';

@Component({
  selector: 'app-upgrade-security',
  standalone: true,
  imports: [LucideAngularModule],
  templateUrl: './upgrade-security.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UpgradeSecurityComponent {
  trust = input<PackageTrust | null | undefined>(null);
  from = input.required<string>();
  to = input.required<string>();
  // Both ends of the range included
  versions = input<string[]>([]);
  registry = input<string>('');

  readonly ShieldCheckIcon = ShieldCheck;
  readonly ShieldAlertIcon = ShieldAlert;
  readonly ShieldXIcon = ShieldX;
  readonly severityClass = severityClass;

  status = computed(() => this.trust()?.status ?? 'unavailable');
  checked = computed(() => isChecked(this.trust(), this.from()) && isChecked(this.trust(), this.to()));
  fixed = computed(() => sortBySeverity(fixedBy(this.trust(), this.from(), this.to())));
  introduced = computed(() => sortBySeverity(introducedBy(this.trust(), this.from(), this.to())));
  malicious = computed(() => maliciousVersions(this.trust(), this.allVersions()));
  perVersion = computed(() =>
    this.allVersions()
      .filter(version => isChecked(this.trust(), version))
      .map(version => {
        const advisories = advisoriesOf(this.trust(), version);
        return { version, count: advisories.length, worst: worstSeverity(advisories) };
      }));

  // null while the Socket.dev provider is switched off
  behavior = computed(() => this.trust()?.behavior ?? null);
  isPub = computed(() => this.registry().toLowerCase().startsWith('pub'));
  behaviorFrom = computed(() => this.behavior()?.[this.from()] ?? null);
  behaviorTo = computed(() => this.behavior()?.[this.to()] ?? null);
  newAlerts = computed(() => {
    const before = new Set((this.behaviorFrom()?.alerts ?? []).map(a => a.type));
    return (this.behaviorTo()?.alerts ?? []).filter(a => !before.has(a.type));
  });

  private allVersions = computed(() => {
    const all = new Set([this.from(), ...this.versions(), this.to()]);
    return [...all];
  });
}
