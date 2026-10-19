import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { LucideAngularModule, FileJson, FileText, FileCode, FileType, type LucideIconData } from 'lucide-angular';
import { ModalComponent } from '../modal/modal';
import { ExportService, ExportFormat } from '../../../core/services/export.service';
import { PackageMetadata, AnalysisResult, VersionInfo } from '../../../core/models/package.models';

type ExportType = 'releases' | 'diff';

interface FormatOption {
    format: ExportFormat;
    label: string;
    description: string;
    icon: LucideIconData;
    tint: string;
}

@Component({
    selector: 'app-export-modal',
    standalone: true,
    imports: [LucideAngularModule, ModalComponent],
    templateUrl: './export-modal.html',
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ExportModalComponent {
    exportType = input.required<ExportType>();
    packageMetadata = input<PackageMetadata | null>(null);
    versions = input<VersionInfo[]>([]);
    diffResult = input<AnalysisResult | null>(null);
    registry = input<string>('nuget');

    closed = output<void>();

    readonly formats: readonly FormatOption[] = [
        { format: 'json', label: 'JSON', description: 'Standard data exchange format', icon: FileJson, tint: 'bg-blue-50 dark:bg-blue-900/20 text-blue-600 dark:text-blue-400' },
        { format: 'md', label: 'Markdown', description: 'Documentation-ready text', icon: FileText, tint: 'bg-emerald-50 dark:bg-emerald-900/20 text-emerald-600 dark:text-emerald-400' },
        { format: 'toml', label: 'TOML', description: 'Configuration file format', icon: FileCode, tint: 'bg-purple-50 dark:bg-purple-900/20 text-purple-600 dark:text-purple-400' },
        { format: 'pdf', label: 'PDF', description: 'Printable document', icon: FileType, tint: 'bg-rose-50 dark:bg-rose-900/20 text-rose-600 dark:text-rose-400' },
    ];

    // The format being written; the dialog stays open, and cannot be closed, until the file is ready
    exporting = signal<ExportFormat | null>(null);

    private exportService = inject(ExportService);

    subject = computed(() => {
        if (this.exportType() === 'diff') {
            const diff = this.diffResult();
            return diff ? `Comparison of ${diff.fromVersion} → ${diff.toVersion}` : 'Comparison';
        }
        const count = this.versions().length;
        return `Release notes of ${count} ${count === 1 ? 'version' : 'versions'}`;
    });

    async export(format: ExportFormat): Promise<void> {
        if (this.exporting()) return;
        this.exporting.set(format);

        try {
            if (this.exportType() === 'releases') {
                const metadata = this.packageMetadata();
                if (metadata) {
                    await this.exportService.exportReleaseNotes(metadata, this.versions(), this.registry(), format);
                }
            } else {
                const diff = this.diffResult();
                if (diff) {
                    await this.exportService.exportDiff(diff, this.registry(), format);
                }
            }
        } finally {
            this.exporting.set(null);
        }
        this.closed.emit();
    }
}
