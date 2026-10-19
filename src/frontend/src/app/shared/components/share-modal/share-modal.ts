import { ChangeDetectionStrategy, Component, PLATFORM_ID, computed, inject, input, output, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { LucideAngularModule, Twitter, Linkedin, Facebook, Link, Check, Share2 } from 'lucide-angular';
import { ModalComponent } from '../modal/modal';
import { ToastService } from '../../../core/services/toast.service';

@Component({
    selector: 'app-share-modal',
    standalone: true,
    imports: [LucideAngularModule, ModalComponent],
    templateUrl: './share-modal.html',
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ShareModalComponent {
    packageId = input.required<string>();
    registry = input.required<string>();
    version = input<string>();

    closed = output<void>();

    readonly TwitterIcon = Twitter;
    readonly LinkedinIcon = Linkedin;
    readonly FacebookIcon = Facebook;
    readonly LinkIcon = Link;
    readonly CheckIcon = Check;
    readonly ShareIcon = Share2;

    isCopied = signal(false);

    private toastService = inject(ToastService);

    readonly canShareNatively =
        isPlatformBrowser(inject(PLATFORM_ID)) && typeof navigator !== 'undefined' && typeof navigator.share === 'function';

    shareUrl = computed(() => {
        const ver = this.version();
        const base = `https://pkgdiff.dev/package/${this.registry()}/${this.packageId()}`;
        return ver ? `${base}/${ver}` : base;
    });

    shareText = computed(() => `Check out ${this.packageId()} on pkgdiff!`);

    shareToTwitter(): void {
        this.openWindow(`https://twitter.com/intent/tweet?url=${encodeURIComponent(this.shareUrl())}&text=${encodeURIComponent(this.shareText())}`);
    }

    shareToLinkedIn(): void {
        this.openWindow(`https://www.linkedin.com/shareArticle?mini=true&url=${encodeURIComponent(this.shareUrl())}&title=${encodeURIComponent(this.shareText())}&summary=${encodeURIComponent(this.shareText())}`);
    }

    shareToFacebook(): void {
        this.openWindow(`https://www.facebook.com/sharer/sharer.php?u=${encodeURIComponent(this.shareUrl())}&quote=${encodeURIComponent(this.shareText())}`);
    }

    async shareNatively(): Promise<void> {
        try {
            await navigator.share({ title: this.packageId(), text: this.shareText(), url: this.shareUrl() });
            this.closed.emit();
        } catch {
            // The visitor closed the share sheet: the dialog stays open for another choice
        }
    }

    async copyLink(): Promise<void> {
        try {
            await navigator.clipboard.writeText(this.shareUrl());
            this.isCopied.set(true);
            this.toastService.success('Link copied to clipboard!', 2000);
            setTimeout(() => this.closed.emit(), 1000);
        } catch {
            this.toastService.error('Could not copy the link.');
        }
    }

    private openWindow(url: string): void {
        window.open(url, '_blank', 'width=600,height=400');
        this.closed.emit();
    }
}
