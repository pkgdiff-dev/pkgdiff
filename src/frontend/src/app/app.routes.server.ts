import { RenderMode, ServerRoute } from '@angular/ssr';

// The admin area needs a browser-only session, so it is sent as the client-side shell
export const serverRoutes: ServerRoute[] = [
  { path: '', renderMode: RenderMode.Server },
  { path: 'packages', renderMode: RenderMode.Server },
  { path: 'package/:registry/:packageId', renderMode: RenderMode.Server },
  { path: 'package/:registry/:packageId/:version', renderMode: RenderMode.Server },
  { path: 'package/:registry/:scope/:name/:version', renderMode: RenderMode.Server },
  { path: 'terms', renderMode: RenderMode.Server },
  { path: 'privacy', renderMode: RenderMode.Server },
  { path: 'cookies', renderMode: RenderMode.Server },
  { path: 'blog', renderMode: RenderMode.Server },
  { path: 'blog/:slug', renderMode: RenderMode.Server },
  { path: 'contributors', renderMode: RenderMode.Server },
  { path: 'audit', renderMode: RenderMode.Server },
  // A report is polled until it is done; the browser does that, not the render server
  { path: 'audit/:id', renderMode: RenderMode.Client },
  { path: 'admin/login', renderMode: RenderMode.Client },
  { path: 'admin/**', renderMode: RenderMode.Client },
  { path: '**', renderMode: RenderMode.Server, status: 404 },
];
