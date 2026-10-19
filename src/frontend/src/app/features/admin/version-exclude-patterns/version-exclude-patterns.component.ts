import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Subject, catchError, map, of, switchMap, tap, timer } from 'rxjs';
import { LucideAngularModule, Plus, X, Check, Loader, FilterX, Eye, AlertCircle, Pencil, Trash, Pause, Play } from 'lucide-angular';
import { ApiService } from '../../../core/services/api';
import { ToastService } from '../../../core/services/toast.service';
import { ConfirmService } from '../../../core/services/confirm.service';
import { apiErrorMessage } from '../../../core/utils/http-error.utils';
import { GridPatternComponent } from '../../../shared/components/grid-pattern/grid-pattern';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';

interface VersionExcludePattern {
    id: number;
    pattern: string;
    description: string;
    isActive: boolean;
}

interface PreviewResponse {
    pattern: string;
    matchedVersions: string[];
    matchCount: number;
    message: string;
}

interface Preview {
    versions: string[];
    message: string;
}

const PATTERNS_PATH = 'admin/version-exclude-patterns';
const PREVIEW_DELAY_MS = 300;
const NO_PREVIEW: Preview = { versions: [], message: '' };

@Component({
    selector: 'app-version-exclude-patterns',
    standalone: true,
    imports: [FormsModule, LucideAngularModule, GridPatternComponent, HasPermissionDirective],
    templateUrl: './version-exclude-patterns.component.html',
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class VersionExcludePatternsComponent {
    private api = inject(ApiService);
    private toast = inject(ToastService);
    private confirmService = inject(ConfirmService);

    readonly PlusIcon = Plus;
    readonly XIcon = X;
    readonly CheckIcon = Check;
    readonly LoaderIcon = Loader;
    readonly FilterXIcon = FilterX;
    readonly EyeIcon = Eye;
    readonly AlertCircleIcon = AlertCircle;
    readonly PencilIcon = Pencil;
    readonly TrashIcon = Trash;
    readonly PauseIcon = Pause;
    readonly PlayIcon = Play;

    patterns = signal<VersionExcludePattern[]>([]);
    loading = signal(false);
    saving = signal(false);

    showForm = signal(false);
    editingId = signal<number | null>(null);
    formPattern = signal('');
    formDescription = signal('');
    formIsActive = signal(true);

    previewLoading = signal(false);
    previewResults = signal<string[]>([]);
    previewMessage = signal('');

    private previewRequests = new Subject<string>();

    constructor() {
        this.previewRequests.pipe(
            switchMap(pattern => {
                if (!pattern) return of(NO_PREVIEW);

                // Debounced; switchMap keeps an older answer from replacing a newer one
                return timer(PREVIEW_DELAY_MS).pipe(
                    tap(() => this.previewLoading.set(true)),
                    switchMap(() => this.api.post<PreviewResponse>(`${PATTERNS_PATH}/preview`, { pattern })),
                    map((response): Preview => ({ versions: response.matchedVersions, message: response.message })),
                    catchError(() => of<Preview>({ versions: [], message: 'Failed to preview pattern' })),
                );
            }),
            takeUntilDestroyed(),
        ).subscribe(preview => {
            this.previewResults.set(preview.versions);
            this.previewMessage.set(preview.message);
            this.previewLoading.set(false);
        });

        this.loadPatterns();
    }

    loadPatterns(): void {
        this.loading.set(true);
        this.api.get<VersionExcludePattern[]>(PATTERNS_PATH).subscribe({
            next: (patterns) => {
                this.patterns.set(patterns);
                this.loading.set(false);
            },
            error: (error: unknown) => {
                this.loading.set(false);
                this.toast.error(apiErrorMessage(error, 'Failed to load patterns.'));
            },
        });
    }

    openAddForm(): void {
        this.openForm(null, '', '', true);
    }

    openEditForm(pattern: VersionExcludePattern): void {
        this.openForm(pattern.id, pattern.pattern, pattern.description, pattern.isActive);
    }

    closeForm(): void {
        this.showForm.set(false);
        this.editingId.set(null);
        this.previewRequests.next('');
    }

    onPatternChange(pattern: string): void {
        this.formPattern.set(pattern);
        this.previewRequests.next(pattern.trim());
    }

    savePattern(): void {
        const pattern = this.formPattern().trim();
        if (!pattern) {
            this.toast.warning('Pattern is required');
            return;
        }

        const body = {
            pattern,
            description: this.formDescription().trim(),
            isActive: this.formIsActive(),
        };
        const editingId = this.editingId();
        const request = editingId !== null
            ? this.api.put(`${PATTERNS_PATH}/${editingId}`, body)
            : this.api.post(PATTERNS_PATH, body);

        this.saving.set(true);
        request.subscribe({
            next: () => {
                this.saving.set(false);
                this.toast.success(editingId !== null ? 'Pattern updated successfully.' : 'Pattern created successfully.');
                this.closeForm();
                this.loadPatterns();
            },
            error: (error: unknown) => {
                this.saving.set(false);
                this.toast.error(apiErrorMessage(error, 'Failed to save pattern.'));
            },
        });
    }

    toggleActive(pattern: VersionExcludePattern): void {
        this.api.patch(`${PATTERNS_PATH}/${pattern.id}/toggle`, {}).subscribe({
            next: () => {
                this.toast.success(`Pattern ${pattern.isActive ? 'disabled' : 'enabled'}.`);
                this.loadPatterns();
            },
            error: (error: unknown) => this.toast.error(apiErrorMessage(error, 'Failed to toggle pattern.')),
        });
    }

    async confirmDelete(id: number): Promise<void> {
        if (await this.confirmService.confirm('Are you sure you want to delete this pattern?', 'Delete Pattern')) {
            this.deletePattern(id);
        }
    }

    private deletePattern(id: number): void {
        this.loading.set(true);
        this.api.delete(`${PATTERNS_PATH}/${id}`).subscribe({
            next: () => {
                this.toast.success('Pattern deleted successfully.');
                this.loadPatterns();
            },
            error: (error: unknown) => {
                this.loading.set(false);
                this.toast.error(apiErrorMessage(error, 'Failed to delete pattern.'));
            },
        });
    }

    private openForm(id: number | null, pattern: string, description: string, isActive: boolean): void {
        this.editingId.set(id);
        this.formPattern.set(pattern);
        this.formDescription.set(description);
        this.formIsActive.set(isActive);
        this.showForm.set(true);
        this.previewRequests.next('');
    }
}
