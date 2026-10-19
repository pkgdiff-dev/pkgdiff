import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subject, catchError, distinctUntilChanged, map, of, startWith, switchMap, tap } from 'rxjs';
import { LucideAngularModule, BookOpen, AlertCircle, ChevronLeft, ChevronRight } from 'lucide-angular';
import { ApiService } from '../../../core/services/api';
import { SeoService } from '../../../core/services/seo.service';
import { BlogPostSnippet } from '../../../core/models/blog.models';
import { PaginatedResult } from '../../../core/models/package.models';
import { stripMarkdown } from '../../../core/utils/markdown.utils';

type PageState = 'loading' | 'loaded' | 'error';
type Loaded = { page: PaginatedResult<BlogPostSnippet> } | { failure: unknown };

type PageLink = { kind: 'page'; page: number } | { kind: 'gap'; key: string };

const PAGE_SIZE = 10;
const SIBLINGS = 1;

function pageWindow(current: number, total: number): PageLink[] {
  let start = Math.max(2, current - SIBLINGS);
  let end = Math.min(total - 1, current + SIBLINGS);
  if (start === 3) start = 2;
  if (end === total - 2) end = total - 1;

  const links: PageLink[] = [{ kind: 'page', page: 1 }];
  if (start > 2) links.push({ kind: 'gap', key: 'gap-start' });
  for (let page = start; page <= end; page++) links.push({ kind: 'page', page });
  if (end < total - 1) links.push({ kind: 'gap', key: 'gap-end' });
  if (total > 1) links.push({ kind: 'page', page: total });
  return links;
}

@Component({
  selector: 'app-blog-list',
  standalone: true,
  imports: [RouterLink, LucideAngularModule, DatePipe],
  templateUrl: './blog-list.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BlogListComponent {
  private api = inject(ApiService);
  private route = inject(ActivatedRoute);
  private seo = inject(SeoService);

  readonly BookOpenIcon = BookOpen;
  readonly AlertIcon = AlertCircle;
  readonly PrevIcon = ChevronLeft;
  readonly NextIcon = ChevronRight;
  readonly skeletonRows = [1, 2, 3];
  readonly stripMarkdown = stripMarkdown;

  posts = signal<BlogPostSnippet[]>([]);
  state = signal<PageState>('loading');
  currentPage = signal(1);
  totalPages = signal(0);
  pages = computed(() => pageWindow(this.currentPage(), this.totalPages()));

  private reload$ = new Subject<void>();

  constructor() {
    // Page number in the URL so every page of the list can be linked and crawled
    this.route.queryParamMap.pipe(
      map(params => Math.max(1, Number.parseInt(params.get('page') ?? '1', 10) || 1)),
      distinctUntilChanged(),
      tap(page => {
        this.currentPage.set(page);
        this.setMeta(page);
      }),
      switchMap(page => this.reload$.pipe(
        startWith(undefined),
        tap(() => this.state.set('loading')),
        switchMap(() => this.api.get<PaginatedResult<BlogPostSnippet>>('blog', { page, pageSize: PAGE_SIZE }).pipe(
          map((result): Loaded => ({ page: result })),
          catchError((failure: unknown) => of<Loaded>({ failure })),
        )),
      )),
      takeUntilDestroyed(),
    ).subscribe(loaded => {
      if ('failure' in loaded) {
        this.state.set('error');
        return;
      }
      this.posts.set(loaded.page.data);
      this.totalPages.set(loaded.page.totalPages);
      this.state.set('loaded');
    });

    inject(DestroyRef).onDestroy(() => this.seo.clearPageData());
  }

  retry(): void {
    this.reload$.next();
  }

  private setMeta(page: number): void {
    this.seo.updateMeta({
      title: page > 1 ? `Engineering Blog — Page ${page} — pkgdiff` : 'Engineering Blog — pkgdiff',
      description: 'Read deep dives into package analysis, dependency security, software versioning trends, and developer infrastructure.',
      url: page > 1 ? `/blog?page=${page}` : '/blog',
      image: '/og-image/blog.png'
    });
  }
}
