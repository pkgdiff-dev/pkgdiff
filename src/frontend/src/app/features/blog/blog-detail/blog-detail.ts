import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, map, of, switchMap, tap } from 'rxjs';
import { LucideAngularModule, ChevronRight, Bot } from 'lucide-angular';
import { ApiService } from '../../../core/services/api';
import { SeoService } from '../../../core/services/seo.service';
import { HttpStatusService } from '../../../core/services/http-status.service';
import { BlogPostDetail } from '../../../core/models/blog.models';
import { loadFailureOf } from '../../../core/utils/http-error.utils';
import { excerptOf } from '../../../core/utils/markdown.utils';

type PageState = 'loading' | 'loaded' | 'not-found' | 'error';
type Loaded = { post: BlogPostDetail } | { failure: unknown };

@Component({
  selector: 'app-blog-detail',
  standalone: true,
  imports: [RouterLink, LucideAngularModule, DatePipe],
  templateUrl: './blog-detail.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BlogDetailComponent {
  private api = inject(ApiService);
  private route = inject(ActivatedRoute);
  private seo = inject(SeoService);
  private httpStatus = inject(HttpStatusService);
  private destroyRef = inject(DestroyRef);

  readonly ChevronRightIcon = ChevronRight;
  readonly BotIcon = Bot;

  post = signal<BlogPostDetail | null>(null);
  state = signal<PageState>('loading');
  private slug = '';

  constructor() {
    this.route.paramMap.pipe(
      map(params => params.get('slug') ?? ''),
      tap(slug => {
        this.slug = slug;
        this.state.set('loading');
        this.post.set(null);
        this.seo.clearPageData();
      }),
      // A post the visitor has already left never overwrites the one on screen
      switchMap(slug => this.load(slug)),
      takeUntilDestroyed(),
    ).subscribe(loaded => this.show(loaded));

    this.destroyRef.onDestroy(() => this.seo.clearPageData());
  }

  retry(): void {
    this.state.set('loading');
    this.load(this.slug)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(loaded => this.show(loaded));
  }

  private load(slug: string) {
    return this.api.get<BlogPostDetail>(`blog/${encodeURIComponent(slug)}`).pipe(
      map((post): Loaded => ({ post })),
      catchError((failure: unknown) => of<Loaded>({ failure })),
    );
  }

  private show(loaded: Loaded): void {
    if ('failure' in loaded) {
      const failure = loadFailureOf(loaded.failure);
      if (failure === 'not-found') {
        this.httpStatus.notFound();
        this.seo.setNotFound('Article Not Found — pkgdiff Blog', 'This blog post does not exist or has not been published yet.');
      }
      this.state.set(failure);
      return;
    }

    const post = loaded.post;
    this.post.set(post);
    this.state.set('loaded');

    const excerpt = excerptOf(post.content);
    this.seo.updateMeta({
      title: `${post.title} — pkgdiff Blog`,
      description: excerpt,
      url: `/blog/${post.slug}`,
      type: 'article',
      image: '/og-image/blog.png'
    });

    this.seo.setStructuredData({
      '@context': 'https://schema.org',
      '@type': 'BlogPosting',
      headline: post.title,
      description: excerpt,
      datePublished: post.publishedAt,
      url: `https://pkgdiff.dev/blog/${post.slug}`,
      author: {
        '@type': 'Organization',
        name: 'pkgdiff'
      }
    });

    this.seo.setBreadcrumb([
      { name: 'Home', url: '/' },
      { name: 'Blog', url: '/blog' },
      { name: post.title, url: `/blog/${post.slug}` },
    ]);
  }
}
