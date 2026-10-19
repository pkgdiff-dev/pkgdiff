export interface BlogPostSnippet {
  id: number;
  title: string;
  slug: string;
  packageId: string;
  version: string;
  publishedAt: string;
  excerpt: string;
}

export interface BlogPostDetail {
  id: number;
  title: string;
  slug: string;
  content: string;
  contentHtml: string;
  packageId: string;
  version: string;
  publishedAt: string;
}
