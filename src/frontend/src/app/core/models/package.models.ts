import { PackageTrust } from './security.models';

export interface AnalyzeRequest {
  packageId: string;
  registry: string; // Selected registry (nuget, npm, etc.)
  currentVersion: string;
  targetVersion: string;
  includePrerelease: boolean;
  connectionId?: string;
  progressToken?: string;
}

export interface RegistryInfo {
  value: string;
  label: string;
  icon: string;
  disabled: boolean;
}

export interface AnalysisResult {
  packageId: string;
  fromVersion: string;
  toVersion: string;
  totalVersions: number;
  versions: VersionInfo[];
  summary: Summary;
  trust?: PackageTrust | null;
}

export interface PackageMetadata {
  packageId: string;
  registry: string;
  latestVersion: string;
  description: string;
  authors: string;
  iconUrl: string;
  projectUrl: string;
  gitHubRepositoryUrl: string | null;
  sourceDirectory?: string | null;
  versions: VersionSummary[];
  trust?: PackageTrust | null;
}

export interface VersionSummary {
  version: string;
  publishedDate: string | null;
  isPrerelease: boolean;
  releaseNotes: string;
  releaseNotesHtml: string;
  releaseNotesSource: string | null;
  compiledFrom?: CompiledNotesSource | null;
  entries: ReleaseNoteEntry[];
  isEmpty: boolean;
}

export interface VersionInfo {
  version: string;
  publishedDate: string | null;
  releaseNotes: string;
  releaseNotesHtml: string;
  releaseNotesSource: string | null;
  compiledFrom?: CompiledNotesSource | null;
  entries: ReleaseNoteEntry[];
  categories: ChangeCategory[];
  isEmpty: boolean;
  isPrerelease: boolean;
}

// Where notes pkgdiff compiled itself come from (a GitHub milestone): the maintainers did not write them
export interface CompiledNotesSource {
  label: string;
  url: string | null;
}

export interface ReleaseNoteEntry {
  category: ChangeCategory;
  text: string;
  textHtml?: string;
}

export interface Summary {
  breakingChanges: number;
  bugFixes: number;
  newFeatures: number;
  performanceImprovements: number;
  securityFixes: number;
  deprecations: number;
  otherChanges: number;
}

export enum ChangeCategory {
  BreakingChange,
  BugFix,
  NewFeature,
  Performance,
  Security,
  Deprecation,
  Other,
}

export interface LiveUpdate {
  packageId: string;
  registry: string;
  version: string;
  previousVersion: string | null;
  updatedAt: string;
}

export interface PaginatedResult<T> {
  data: T[];
  page: number;
  pageSize: number;
  total: number;
  totalPages: number;
}

export interface PackageSuggestion {
  packageId: string;
  registry: string;
  latestVersion: string | null;
  description: string | null;
  iconUrl: string | null;
}

