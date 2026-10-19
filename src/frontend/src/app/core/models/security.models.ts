// Mirrors PkgDiff.Shared/DTOs/SecurityModels.cs
export type TrustStatus = 'pending' | 'ready' | 'unavailable';
export type AdvisorySeverity = 'critical' | 'high' | 'medium' | 'low' | 'unknown';

export interface PackageTrust {
  status: TrustStatus;
  fetchedAt: string | null;
  advisories: SecurityAdvisory[];
  // Version -> ids of the advisories that affect it; a version that was not checked is missing
  versionAdvisories: Record<string, string[]>;
  health: PackageHealth | null;
  behavior: Record<string, BehaviorReport> | null;
}

export interface SecurityAdvisory {
  id: string;
  aliases: string[];
  summary: string;
  severity: AdvisorySeverity;
  fixedIn: string[];
  url: string;
  isMalicious: boolean;
  published: string | null;
}

export interface PackageHealth {
  source: string;
  sourceUrl: string | null;
  attribution: string | null;
  repositoryUrl: string | null;
  scorecardScore: number | null;
  scorecardDate: string | null;
  checks: ScorecardCheck[];
  licenses: string[];
  hasProvenance: boolean | null;
  checkedVersion: string | null;
}

export interface ScorecardCheck {
  name: string;
  score: number | null;
  reason: string | null;
}

export interface BehaviorReport {
  overallScore: number | null;
  supplyChainScore: number | null;
  alerts: BehaviorAlert[];
  url: string | null;
}

export interface BehaviorAlert {
  type: string;
  severity: AdvisorySeverity;
}
