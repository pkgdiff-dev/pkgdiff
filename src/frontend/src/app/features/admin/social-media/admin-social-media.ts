import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { LucideAngularModule, Share2, Plus, Loader2, Linkedin, Twitter, AlertCircle, CheckCircle2, History } from 'lucide-angular';
import { ApiService } from '../../../core/services/api';
import { ToastService } from '../../../core/services/toast.service';
import { apiErrorMessage } from '../../../core/utils/http-error.utils';
import { GridPatternComponent } from '../../../shared/components/grid-pattern/grid-pattern';
import { ButtonComponent } from '../../../shared/components/button/button.component';

interface SocialMediaPost {
    id: number;
    platform: string;
    content: string;
    postUrl: string | null;
    status: string;
    errorMessage: string | null;
    createdAt: string;
    blogPostId: number | null;
    blogTitle: string | null;
}

@Component({
    selector: 'app-admin-social-media',
    standalone: true,
    imports: [
        FormsModule, LucideAngularModule, GridPatternComponent,
        ButtonComponent, DatePipe
    ],
    templateUrl: './admin-social-media.html',
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminSocialMediaComponent {
    private api = inject(ApiService);
    private toast = inject(ToastService);
    private route = inject(ActivatedRoute);

    readonly ShareIcon = Share2;
    readonly PlusIcon = Plus;
    readonly LoaderIcon = Loader2;
    readonly LinkedinIcon = Linkedin;
    readonly TwitterIcon = Twitter;
    readonly AlertIcon = AlertCircle;
    readonly CheckIcon = CheckCircle2;
    readonly HistoryIcon = History;

    posts = signal<SocialMediaPost[]>([]);
    loading = signal(true);
    totalCount = signal(0);

    shareContent = signal('');
    sharePlatform = signal<'Twitter' | 'LinkedIn'>('Twitter');
    sharing = signal(false);
    connectingLinkedIn = signal(false);

    private readonly LINKEDIN_STATE_KEY = 'pkgdiff-linkedin-oauth-state';

    constructor() {
        this.loadHistory();

        // Handle OAuth callback: if ?code= is in the URL, complete the process
        this.route.queryParams.pipe(takeUntilDestroyed()).subscribe(params => {
            const code: string | undefined = params['code'];
            if (!code) return;

            const returnedState: string | undefined = params['state'];
            const expectedState = sessionStorage.getItem(this.LINKEDIN_STATE_KEY);
            sessionStorage.removeItem(this.LINKEDIN_STATE_KEY);

            // Defense in depth only: the backend validates the server-stored state
            if (!expectedState || !returnedState || expectedState !== returnedState) {
                this.toast.error('LinkedIn sign-in could not be verified. Please try again.');
                window.history.replaceState({}, document.title, '/admin/social-media');
                return;
            }

            this.completeLinkedInOAuth(code, returnedState);
        });
    }

    loadHistory(): void {
        this.loading.set(true);
        this.api.get<{ items: SocialMediaPost[], totalCount: number }>('admin/social-media').subscribe({
            next: (res) => {
                this.posts.set(res.items);
                this.totalCount.set(res.totalCount);
                this.loading.set(false);
            },
            error: () => {
                this.loading.set(false);
                this.toast.error('Failed to load social media post history.');
            }
        });
    }

    connectLinkedIn(): void {
        this.connectingLinkedIn.set(true);
        this.api.get<{ authUrl: string; redirectUri: string; state: string }>('admin/social-media/linkedin/auth-url').subscribe({
            next: (res) => {
                this.connectingLinkedIn.set(false);
                sessionStorage.setItem(this.LINKEDIN_STATE_KEY, res.state);
                // Redirect the page to LinkedIn login; LinkedIn redirects back to the SPA
                window.location.href = res.authUrl;
            },
            error: (error: unknown) => {
                this.connectingLinkedIn.set(false);
                this.toast.error(apiErrorMessage(error, 'Could not generate LinkedIn Auth URL. Check ClientId in System Settings.'));
            }
        });
    }

    completeLinkedInOAuth(code: string, state: string): void {
        this.connectingLinkedIn.set(true);
        this.api.post<{ message: string }>('admin/social-media/linkedin/callback', { code, state }).subscribe({
            next: (res) => {
                this.connectingLinkedIn.set(false);
                this.toast.success(res.message || 'LinkedIn connected successfully!');
                window.history.replaceState({}, document.title, '/admin/social-media');
            },
            error: (error: unknown) => {
                this.connectingLinkedIn.set(false);
                this.toast.error(apiErrorMessage(error, 'Failed to complete LinkedIn OAuth flow.'));
                window.history.replaceState({}, document.title, '/admin/social-media');
            }
        });
    }

    sharePost(): void {
        if (!this.shareContent().trim()) {
            this.toast.error('Post content cannot be empty.');
            return;
        }

        // The platform can be switched while the request is under way; the messages name the one posted to
        const platform = this.sharePlatform();
        this.sharing.set(true);
        this.api.post('admin/social-media/share', {
            platform,
            content: this.shareContent()
        }).subscribe({
            next: () => {
                this.toast.success(`Successfully posted to ${platform}`);
                this.shareContent.set('');
                this.sharing.set(false);
                this.loadHistory();
            },
            error: (error: unknown) => {
                this.toast.error(apiErrorMessage(error, `Failed to post to ${platform}.`));
                this.sharing.set(false);
                this.loadHistory();
            }
        });
    }
}
