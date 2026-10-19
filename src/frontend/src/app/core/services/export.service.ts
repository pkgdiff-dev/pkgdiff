import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PackageMetadata, AnalysisResult, ReleaseNoteEntry, VersionInfo } from '../models/package.models';
import { ApiService } from './api';
import { ToastService } from './toast.service';
import { stringify as tomlStringify } from '@ltd/j-toml';
import { PackageTrust, SecurityAdvisory } from '../models/security.models';
import { advisoriesOf, advisoryLine, fixedBy, introducedBy, maliciousVersions } from '../utils/trust.utils';

export type ExportFormat = 'json' | 'md' | 'toml' | 'pdf';

@Injectable({ providedIn: 'root' })
export class ExportService {
    private api = inject(ApiService);
    private toast = inject(ToastService);

    /**
     * Export package metadata to specified format
     */
    async exportReleaseNotes(
        metadata: PackageMetadata,
        versions: VersionInfo[],
        registry: string,
        format: ExportFormat
    ): Promise<void> {
        try {
            let blob: Blob;
            let filename: string;

            switch (format) {
                case 'json':
                    blob = this.exportToJson(metadata, versions);
                    filename = versions.length === 1 ? `${metadata.packageId}-v${versions[0].version}-release-notes.json` : `${metadata.packageId}-release-notes.json`;
                    break;
                case 'md':
                    blob = this.exportToMarkdown(metadata, versions);
                    filename = versions.length === 1 ? `${metadata.packageId}-v${versions[0].version}-release-notes.md` : `${metadata.packageId}-release-notes.md`;
                    break;
                case 'toml':
                    blob = this.exportToToml(metadata, versions);
                    filename = versions.length === 1 ? `${metadata.packageId}-v${versions[0].version}-release-notes.toml` : `${metadata.packageId}-release-notes.toml`;
                    break;
                case 'pdf':
                    await this.exportToPdfViaBackend(registry, metadata.packageId);
                    return;
            }

            this.downloadFile(blob, filename);
            this.toast.success(`Exported as ${format.toUpperCase()}`, 2000);
        } catch {
            this.toast.error('Export failed. Please try again.', 3000);
        }
    }

    /**
     * Export diff results to specified format
     */
    async exportDiff(
        diff: AnalysisResult,
        registry: string,
        format: ExportFormat
    ): Promise<void> {
        try {
            let blob: Blob;
            let filename: string;

            switch (format) {
                case 'json':
                    blob = this.exportDiffToJson(diff);
                    filename = `${diff.packageId}-diff-${diff.fromVersion}-to-${diff.toVersion}.json`;
                    break;
                case 'md':
                    blob = this.exportDiffToMarkdown(diff);
                    filename = `${diff.packageId}-diff-${diff.fromVersion}-to-${diff.toVersion}.md`;
                    break;
                case 'toml':
                    blob = this.exportDiffToToml(diff);
                    filename = `${diff.packageId}-diff-${diff.fromVersion}-to-${diff.toVersion}.toml`;
                    break;
                case 'pdf':
                    await this.exportDiffToPdfViaBackend(diff, registry);
                    return;
            }

            this.downloadFile(blob, filename);
            this.toast.success(`Exported as ${format.toUpperCase()}`, 2000);
        } catch {
            this.toast.error('Export failed. Please try again.', 3000);
        }
    }

    // ===== JSON Export =====

    private exportToJson(metadata: PackageMetadata, versions: VersionInfo[]): Blob {
        const json = JSON.stringify({
            ...metadata,
            versions: versions.map((v) => this.withoutRenderedHtml(v))
        }, null, 2);
        return new Blob([json], { type: 'application/json' });
    }

    private exportDiffToJson(diff: AnalysisResult): Blob {
        const json = JSON.stringify({
            ...diff,
            versions: diff.versions.map((v) => this.withoutRenderedHtml(v))
        }, null, 2);
        return new Blob([json], { type: 'application/json' });
    }

    // Exports carry the release notes as markdown; the server-rendered HTML is display-only
    private withoutRenderedHtml(version: VersionInfo): Omit<VersionInfo, 'releaseNotesHtml'> {
        const { releaseNotesHtml: _html, ...rest } = version;
        return {
            ...rest,
            entries: (rest.entries ?? []).map(({ textHtml: _textHtml, ...entry }) => entry),
        };
    }

    // ===== Markdown Export =====

    private exportToMarkdown(metadata: PackageMetadata, versions: VersionInfo[]): Blob {
        let md = `# ${metadata.packageId}\n\n`;
        md += `**Latest Version:** ${metadata.latestVersion}\n`;
        if (metadata.description) md += `**Description:** ${metadata.description}\n`;
        if (metadata.authors) md += `**Authors:** ${metadata.authors}\n`;
        if (metadata.projectUrl) md += `**Project URL:** ${metadata.projectUrl}\n`;
        if (metadata.gitHubRepositoryUrl) md += `**GitHub:** ${metadata.gitHubRepositoryUrl}\n`;
        md += this.packageSecurityMarkdown(metadata.trust, metadata.latestVersion, versions.map((v) => v.version));
        md += `\n---\n\n## Release Notes\n\n`;

        for (const version of versions) {
            md += `### Version ${version.version}\n`;
            if (version.publishedDate) md += `*Published: ${version.publishedDate}*\n`;
            if (version.isPrerelease) md += `**[Prerelease]**\n`;
            md += `\n`;

            if (version.isEmpty) {
                md += `*No release notes available*\n`;
            } else if (version.entries && version.entries.length > 0) {
                const grouped = this.groupByCategory(version.entries);
                for (const [category, entries] of Object.entries(grouped)) {
                    md += `#### ${this.getCategoryName(category)}\n`;
                    for (const entry of entries) {
                        md += `- ${entry.text}\n`;
                    }
                    md += `\n`;
                }
            } else if (version.releaseNotes) {
                md += `${version.releaseNotes}\n`;
            }
            md += `\n`;
        }

        return new Blob([md], { type: 'text/markdown' });
    }

    private exportDiffToMarkdown(diff: AnalysisResult): Blob {
        let md = `# ${diff.packageId} - Version Comparison\n\n`;
        md += `**From:** ${diff.fromVersion}\n`;
        md += `**To:** ${diff.toVersion}\n`;
        md += `**Total Versions:** ${diff.totalVersions}\n\n`;
        md += this.upgradeSecurityMarkdown(diff);
        md += `---\n\n## Summary\n\n`;
        md += `| Category | Count |\n`;
        md += `|----------|-------|\n`;
        md += `| 🔴 Breaking Changes | ${diff.summary.breakingChanges} |\n`;
        md += `| 🐛 Bug Fixes | ${diff.summary.bugFixes} |\n`;
        md += `| ✨ New Features | ${diff.summary.newFeatures} |\n`;
        md += `| ⚡ Performance | ${diff.summary.performanceImprovements} |\n`;
        md += `| 🔒 Security | ${diff.summary.securityFixes} |\n`;
        md += `| ⚠️ Deprecations | ${diff.summary.deprecations} |\n\n`;
        md += `---\n\n## Changes by Version\n\n`;

        for (const version of diff.versions) {
            md += `### Version ${version.version}\n`;
            if (version.publishedDate) md += `*Published: ${version.publishedDate}*\n`;
            if (version.isPrerelease) md += `**[Prerelease]**\n`;
            md += `\n`;

            if (version.isEmpty) {
                md += `*No release notes available*\n`;
            } else if (version.entries && version.entries.length > 0) {
                const grouped = this.groupByCategory(version.entries);
                for (const [category, entries] of Object.entries(grouped)) {
                    md += `#### ${this.getCategoryIcon(category)} ${this.getCategoryName(category)}\n`;
                    for (const entry of entries) {
                        md += `- ${entry.text}\n`;
                    }
                    md += `\n`;
                }
            } else if (version.releaseNotes) {
                md += `${version.releaseNotes}\n`;
            }
            md += `\n`;
        }

        return new Blob([md], { type: 'text/markdown' });
    }

    // ===== TOML Export =====

    private exportToToml(metadata: PackageMetadata, versions: VersionInfo[]): Blob {
        const tomlObject = {
            package: {
                id: metadata.packageId,
                latest_version: metadata.latestVersion,
                description: metadata.description,
                authors: metadata.authors,
                icon_url: metadata.iconUrl,
                project_url: metadata.projectUrl,
                github_url: metadata.gitHubRepositoryUrl,
            },
            versions: versions.map((v) => ({
                version: v.version,
                published_date: v.publishedDate,
                is_prerelease: v.isPrerelease,
                release_notes: v.releaseNotes,
                release_notes_source: v.releaseNotesSource,
                is_empty: v.isEmpty,
                entries: v.entries ? v.entries.map((e) => ({
                    category: this.getCategoryName(e.category.toString()),
                    text: e.text,
                })) : [],
            })),
        };

        return new Blob([this.toToml(tomlObject)], { type: 'application/toml' });
    }

    private exportDiffToToml(diff: AnalysisResult): Blob {
        const tomlObject = {
            diff: {
                package_id: diff.packageId,
                from_version: diff.fromVersion,
                to_version: diff.toVersion,
                total_versions: diff.totalVersions,
            },
            summary: {
                breaking_changes: diff.summary.breakingChanges,
                bug_fixes: diff.summary.bugFixes,
                new_features: diff.summary.newFeatures,
                performance_improvements: diff.summary.performanceImprovements,
                security_fixes: diff.summary.securityFixes,
                deprecations: diff.summary.deprecations,
            },
            versions: diff.versions.map((v) => ({
                version: v.version,
                published_date: v.publishedDate,
                is_prerelease: v.isPrerelease,
                release_notes: v.releaseNotes,
                release_notes_source: v.releaseNotesSource,
                is_empty: v.isEmpty,
                entries: v.entries.map((e) => ({
                    category: this.getCategoryName(e.category.toString()),
                    text: e.text,
                })),
            })),
        };

        return new Blob([this.toToml(tomlObject)], { type: 'application/toml' });
    }

    // Without the newline option j-toml returns an array of lines, which a Blob joins with commas
    private toToml(table: object): string {
        const rootTable = this.toTomlValue(table) as Parameters<typeof tomlStringify>[0];
        return tomlStringify(rootTable, { newline: '\n' });
    }

    // TOML has no null: j-toml throws on null/undefined, so those keys are left out.
    // j-toml writes a JS number as a float (2 becomes 2.0) and a bigint as an integer; the counts are integers.
    private toTomlValue(value: unknown): unknown {
        if (typeof value === 'number' && Number.isInteger(value)) {
            return BigInt(value);
        }
        if (Array.isArray(value)) {
            return value.map((item) => this.toTomlValue(item));
        }
        if (value !== null && typeof value === 'object') {
            return Object.fromEntries(
                Object.entries(value)
                    .filter(([, entry]) => entry !== null && entry !== undefined)
                    .map(([key, entry]) => [key, this.toTomlValue(entry)])
            );
        }
        return value;
    }

    // ===== PDF Export (via Backend) =====

    private async exportToPdfViaBackend(registry: string, packageId: string): Promise<void> {
        try {
            const blob = await firstValueFrom(this.api.getBlob(`Package/${registry}/${encodeURIComponent(packageId)}/export/pdf`));
            this.downloadFile(blob, `${packageId}-release-notes.pdf`);
            this.toast.success('Exported as PDF', 2000);
        } catch (error) {
            this.reportPdfFailure(error);
        }
    }

    private async exportDiffToPdfViaBackend(diff: AnalysisResult, registry: string): Promise<void> {
        try {
            const blob = await firstValueFrom(this.api.postBlob('Package/export/diff/pdf', {
                packageId: diff.packageId,
                registry,
                currentVersion: diff.fromVersion,
                targetVersion: diff.toVersion,
                includePrerelease: false,
            }));
            this.downloadFile(blob, `${diff.packageId}-diff-${diff.fromVersion}-to-${diff.toVersion}.pdf`);
            this.toast.success('Exported as PDF', 2000);
        } catch (error) {
            this.reportPdfFailure(error);
        }
    }

    private reportPdfFailure(error: unknown): void {
        // The error interceptor has already said these two
        if (error instanceof HttpErrorResponse && (error.status === 0 || error.status === 429)) return;
        this.toast.error('PDF export failed. Please try again.', 3000);
    }

    // ===== Helper Methods =====

    private packageSecurityMarkdown(trust: PackageTrust | null | undefined, latestVersion: string, versions: string[]): string {
        if (!trust || trust.status !== 'ready') return '';
        let md = `\n## Security\n\n`;
        md += this.maliciousMarkdown(trust, [...versions, latestVersion]);
        md += this.advisoryListMarkdown(`Known vulnerabilities in ${latestVersion}`, advisoriesOf(trust, latestVersion));
        md += this.healthMarkdown(trust);
        return md;
    }

    private upgradeSecurityMarkdown(diff: AnalysisResult): string {
        const trust = diff.trust;
        if (!trust || trust.status !== 'ready') return '';
        let md = `## Security of this upgrade\n\n`;
        md += this.maliciousMarkdown(trust, [diff.fromVersion, ...diff.versions.map((v) => v.version), diff.toVersion]);
        md += this.advisoryListMarkdown(`Fixed by upgrading to ${diff.toVersion}`, fixedBy(trust, diff.fromVersion, diff.toVersion));
        md += this.advisoryListMarkdown(`Introduced by upgrading to ${diff.toVersion}`, introducedBy(trust, diff.fromVersion, diff.toVersion));
        md += this.healthMarkdown(trust);
        return md;
    }

    private maliciousMarkdown(trust: PackageTrust, versions: string[]): string {
        const malicious = maliciousVersions(trust, [...new Set(versions)]);
        return malicious.length ? `> **Malicious package warning:** ${malicious.join(', ')} matched a malicious-package record. Do not install.\n\n` : '';
    }

    private advisoryListMarkdown(title: string, advisories: SecurityAdvisory[]): string {
        let md = `**${title}:** ${advisories.length}\n\n`;
        for (const advisory of advisories) md += `- ${advisoryLine(advisory)}\n`;
        return advisories.length ? md + '\n' : md;
    }

    private healthMarkdown(trust: PackageTrust): string {
        const health = trust.health;
        if (!health) return '';
        let md = `**OpenSSF Scorecard:** ${health.scorecardScore ?? 'not available'}${health.scorecardScore !== null ? ' / 10' : ''}\n`;
        if (health.licenses.length) md += `**Licenses:** ${health.licenses.join(', ')}\n`;
        if (health.hasProvenance !== null) md += `**Provenance:** ${health.hasProvenance ? 'attestation found' : 'no attestation found'}\n`;
        if (health.attribution) md += `\n_${health.attribution}${health.sourceUrl ? ` (${health.sourceUrl})` : ''}_\n`;
        return md + '\n';
    }

    private downloadFile(blob: Blob, filename: string): void {
        const url = window.URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = filename;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        window.URL.revokeObjectURL(url);
    }

    private groupByCategory(entries: ReleaseNoteEntry[]): Record<string, ReleaseNoteEntry[]> {
        return entries.reduce((acc, entry) => {
            const cat = entry.category.toString();
            if (!acc[cat]) acc[cat] = [];
            acc[cat].push(entry);
            return acc;
        }, {} as Record<string, ReleaseNoteEntry[]>);
    }

    private getCategoryName(category: string): string {
        const cat = parseInt(category, 10);
        switch (cat) {
            case 0:
                return 'Breaking Changes';
            case 1:
                return 'Bug Fixes';
            case 2:
                return 'New Features';
            case 3:
                return 'Performance Improvements';
            case 4:
                return 'Security Fixes';
            case 5:
                return 'Deprecations';
            default:
                return 'Other';
        }
    }

    private getCategoryIcon(category: string): string {
        const cat = parseInt(category, 10);
        switch (cat) {
            case 0:
                return '🔴';
            case 1:
                return '🐛';
            case 2:
                return '✨';
            case 3:
                return '⚡';
            case 4:
                return '🔒';
            case 5:
                return '⚠️';
            default:
                return '📝';
        }
    }
}
