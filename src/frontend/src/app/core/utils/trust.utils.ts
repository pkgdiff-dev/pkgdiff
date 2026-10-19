import { AdvisorySeverity, PackageTrust, SecurityAdvisory } from '../models/security.models';

const SEVERITY_ORDER: AdvisorySeverity[] = ['critical', 'high', 'medium', 'low', 'unknown'];

export function advisoriesOf(trust: PackageTrust | null | undefined, version: string): SecurityAdvisory[] {
  const ids = trust?.versionAdvisories[version];
  if (!trust || !ids) return [];
  const byId = new Map(trust.advisories.map(a => [a.id, a]));
  return ids.map(id => byId.get(id) ?? unknownAdvisory(id));
}

// Whether OSV was asked about this version at all
export function isChecked(trust: PackageTrust | null | undefined, version: string): boolean {
  return !!trust && trust.status === 'ready' && version in trust.versionAdvisories;
}

export function fixedBy(trust: PackageTrust | null | undefined, from: string, to: string): SecurityAdvisory[] {
  const after = new Set(advisoriesOf(trust, to).map(a => a.id));
  return advisoriesOf(trust, from).filter(a => !after.has(a.id));
}

export function introducedBy(trust: PackageTrust | null | undefined, from: string, to: string): SecurityAdvisory[] {
  const before = new Set(advisoriesOf(trust, from).map(a => a.id));
  return advisoriesOf(trust, to).filter(a => !before.has(a.id));
}

export function maliciousVersions(trust: PackageTrust | null | undefined, versions: readonly string[]): string[] {
  if (!trust) return [];
  const malicious = new Set(trust.advisories.filter(a => a.isMalicious).map(a => a.id));
  if (malicious.size === 0) return [];
  return versions.filter(v => (trust.versionAdvisories[v] ?? []).some(id => malicious.has(id)));
}

export function worstSeverity(advisories: readonly SecurityAdvisory[]): AdvisorySeverity | null {
  if (advisories.length === 0) return null;
  return SEVERITY_ORDER.find(s => advisories.some(a => a.severity === s)) ?? 'unknown';
}

export function sortBySeverity(advisories: readonly SecurityAdvisory[]): SecurityAdvisory[] {
  return [...advisories].sort((a, b) => SEVERITY_ORDER.indexOf(a.severity) - SEVERITY_ORDER.indexOf(b.severity));
}

export function severityClass(severity: AdvisorySeverity | null): string {
  switch (severity) {
    case 'critical':
      return 'bg-red-100 text-red-700 border-red-200 dark:bg-red-900/30 dark:text-red-300 dark:border-red-800';
    case 'high':
      return 'bg-orange-100 text-orange-700 border-orange-200 dark:bg-orange-900/30 dark:text-orange-300 dark:border-orange-800';
    case 'medium':
      return 'bg-yellow-100 text-yellow-800 border-yellow-200 dark:bg-yellow-900/30 dark:text-yellow-300 dark:border-yellow-800';
    case 'low':
      return 'bg-blue-100 text-blue-700 border-blue-200 dark:bg-blue-900/30 dark:text-blue-300 dark:border-blue-800';
    default:
      return 'bg-gray-100 text-gray-700 border-gray-200 dark:bg-gray-800 dark:text-gray-300 dark:border-gray-700';
  }
}

export function advisoryLine(advisory: SecurityAdvisory): string {
  const aliases = advisory.aliases.length ? ` (${advisory.aliases.join(', ')})` : '';
  const fixedIn = advisory.fixedIn.length ? `, fixed in ${advisory.fixedIn.join(', ')}` : '';
  return `${advisory.id}${aliases} [${advisory.severity}${fixedIn}]: ${advisory.summary || 'no summary'} ${advisory.url}`.trim();
}

function unknownAdvisory(id: string): SecurityAdvisory {
  return {
    id,
    aliases: [],
    summary: '',
    severity: id.startsWith('MAL-') ? 'critical' : 'unknown',
    fixedIn: [],
    url: `https://osv.dev/vulnerability/${encodeURIComponent(id)}`,
    isMalicious: id.startsWith('MAL-'),
    published: null,
  };
}
