import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { LucideAngularModule, Pencil, Trash2, Eye, EyeOff, Rocket, Save } from 'lucide-angular';
import { ApiService } from '../../../core/services/api';
import { ToastService } from '../../../core/services/toast.service';
import { ConfirmService } from '../../../core/services/confirm.service';
import { PaginatedResult } from '../../../core/models/package.models';
import { GridPatternComponent } from '../../../shared/components/grid-pattern/grid-pattern';
import { ModalComponent } from '../../../shared/components/modal/modal';
import { PaginationComponent } from '../../../shared/components/pagination/pagination';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';

interface BlogPost {
    id: number;
    title: string;
    slug: string;
    content: string;
    packageId: string;
    version: string;
    registry: string;
    isPublished: boolean;
    createdAt: string;
    publishedAt: string | null;
    llmModelUsed: string;
}

@Component({
    selector: 'app-admin-blogs',
    standalone: true,
    imports: [FormsModule, RouterLink, LucideAngularModule, GridPatternComponent, ModalComponent, PaginationComponent, HasPermissionDirective],
    templateUrl: './admin-blogs.html',
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminBlogsComponent {
    private api = inject(ApiService);
    private toast = inject(ToastService);
    private confirmService = inject(ConfirmService);

    readonly PencilIcon = Pencil;
    readonly Trash2Icon = Trash2;
    readonly EyeIcon = Eye;
    readonly EyeOffIcon = EyeOff;
    readonly RocketIcon = Rocket;
    readonly SaveIcon = Save;

    result = signal<PaginatedResult<BlogPost> | null>(null);
    loading = signal(true);
    triggering = signal(false);
    saving = signal(false);
    currentPage = signal(1);

    editingBlog = signal<BlogPost | null>(null);
    editTitle = signal('');
    editContent = signal('');
    previewMode = signal(false);
    previewHtml = signal('');
    previewLoading = signal(false);

    // Escape or a click beside the dialog must not throw away an edited post; Cancel still closes it
    editDirty = computed(() => {
        const blog = this.editingBlog();
        return blog !== null && (this.editTitle() !== blog.title || this.editContent() !== blog.content);
    });

    constructor() {
        this.load();
    }

    load(): void {
        this.loading.set(true);
        this.api.get<PaginatedResult<BlogPost>>('admin/blog', { page: this.currentPage(), pageSize: 20 }).subscribe({
            next: (res) => { this.result.set(res); this.loading.set(false); },
            error: () => { this.toast.error('Failed to load blog posts.'); this.loading.set(false); }
        });
    }

    goToPage(page: number): void {
        this.currentPage.set(page);
        this.load();
    }

    openEdit(blog: BlogPost): void {
        this.editingBlog.set(blog);
        this.editTitle.set(blog.title);
        this.editContent.set(blog.content);
        this.previewMode.set(false);
        this.previewLoading.set(false);
    }

    closeEdit(): void {
        this.editingBlog.set(null);
    }

    // The preview is rendered by the same server-side renderer as the public blog page
    togglePreview(): void {
        if (this.previewMode()) {
            this.previewMode.set(false);
            return;
        }
        const blog = this.editingBlog();
        // An answer that arrives after the dialog moved on to another post belongs to nothing on screen
        const stillOpen = () => this.editingBlog() === blog;

        this.previewLoading.set(true);
        this.api.post<{ html: string }>('admin/blog/preview', { content: this.editContent() }).subscribe({
            next: (res) => {
                if (!stillOpen()) return;
                this.previewHtml.set(res.html);
                this.previewMode.set(true);
                this.previewLoading.set(false);
            },
            error: () => {
                if (!stillOpen()) return;
                this.toast.error('Failed to render the preview.');
                this.previewLoading.set(false);
            }
        });
    }

    saveEdit(): void {
        const blog = this.editingBlog();
        if (!blog) return;
        this.saving.set(true);
        this.api.put(`admin/blog/${blog.id}`, { title: this.editTitle(), content: this.editContent() }).subscribe({
            next: () => {
                this.toast.success('Blog post updated successfully.');
                this.saving.set(false);
                this.closeEdit();
                this.load();
            },
            error: () => { this.toast.error('Failed to save blog post.'); this.saving.set(false); }
        });
    }

    async deleteBlog(blog: BlogPost): Promise<void> {
        if (!await this.confirmService.confirm('This action cannot be undone.', `Delete blog "${blog.title}"?`)) return;
        this.api.delete(`admin/blog/${blog.id}`).subscribe({
            next: () => { this.toast.success('Blog deleted.'); this.load(); },
            error: () => this.toast.error('Failed to delete blog.')
        });
    }

    togglePublish(blog: BlogPost): void {
        this.api.put<{ isPublished: boolean; socialPostQueued: boolean }>(`admin/blog/${blog.id}/toggle-publish`, {}).subscribe({
            next: (res) => {
                const social = res.socialPostQueued ? ' Social posts are queued.' : '';
                this.toast.success(`Blog is now ${res.isPublished ? 'published' : 'drafted'}.${social}`);
                this.load();
            },
            error: () => this.toast.error('Failed to toggle publish state.')
        });
    }

    triggerAutoPilot(): void {
        this.triggering.set(true);
        this.api.post('admin/blog/trigger', {}).subscribe({
            next: () => { this.toast.success('AutoPilot job queued!'); this.triggering.set(false); },
            error: () => { this.toast.error('Failed to queue AutoPilot job.'); this.triggering.set(false); }
        });
    }

    formatDate(dateStr: string): string {
        return new Date(dateStr).toLocaleString('tr-TR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
    }
}
