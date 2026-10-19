// Mirrors PkgDiff.Shared/DTOs/AuditModels.cs
import { SecurityAdvisory } from './security.models';

export type AuditStatus = 'queued' | 'running' | 'completed' | 'failed';
export type AuditItemStatus = 'vulnerable' | 'clean' | 'not-found' | 'unchecked';

export interface AuditDependency {
  name: string;
  // null: the latest version is checked
  version: string | null;
  requirement: string | null;
  isExact: boolean;
  scope: string | null;
}

export interface ManifestParseResult {
  registry: string;
  format: string;
  dependencies: AuditDependency[];
  warnings: string[];
}

export interface StartAuditResponse {
  id: string;
}

export interface AuditSummary {
  packages: number;
  vulnerable: number;
  malicious: number;
  notFound: number;
  critical: number;
  high: number;
  medium: number;
  low: number;
  unknownSeverity: number;
}

export interface AuditItem {
  name: string;
  requirement: string | null;
  scope: string | null;
  isExact: boolean;
  version: string | null;
  versionAssumed: boolean;
  status: AuditItemStatus;
  latestVersion: string | null;
  advisories: SecurityAdvisory[];
  safeVersion: string | null;
  hasUnfixedAdvisory: boolean;
  isMalicious: boolean;
}

export interface AuditReport {
  id: string;
  registry: string;
  format: string | null;
  status: AuditStatus;
  progress: number;
  total: number;
  createdAt: string;
  completedAt: string | null;
  expiresAt: string;
  error: string | null;
  summary: AuditSummary;
  items: AuditItem[];
}
