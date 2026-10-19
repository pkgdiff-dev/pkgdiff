import { AngularNodeAppEngine, createNodeRequestHandler, isMainModule, writeResponseToNodeResponse } from '@angular/ssr/node';
import express from 'express';
import { join } from 'node:path';

const port = Number(process.env['PORT'] ?? 4000);
const browserDistFolder = join(import.meta.dirname, '../browser');

// The application asks its API through relative addresses ("/api/..."), which a server render
// resolves against the origin of the request it is rendering. That origin has to be one that
// answers "/api" from inside the network, not the public one the visitor used: in production it
// is the nginx container in front of this server.
const internalOrigin = new URL(process.env['SSR_INTERNAL_ORIGIN'] ?? `http://localhost:${port}`);

// Request headers a render may read. Everything else a visitor sends stays out of the render.
const FORWARDED_HEADERS = ['accept', 'accept-language', 'user-agent', 'x-forwarded-for'];

const app = express();
app.disable('x-powered-by');

// The request handed to Angular is built below from the internal origin and the headers listed above,
// so the only host it ever sees is that origin and the only proxy header is the visitor's address,
// which nginx sets. Angular answers a request with a proxy header it was not told to trust with the
// unrendered page.
const angularApp = new AngularNodeAppEngine({
  allowedHosts: [internalOrigin.hostname],
  trustProxyHeaders: ['x-forwarded-for'],
});

app.get('/healthz', (_req, res) => {
  res.type('text/plain').send('ok');
});

// nginx serves these files itself in production; this is for running the server on its own
app.use(express.static(browserDistFolder, { maxAge: '1y', index: false, redirect: false }));

app.use((req, res, next) => {
  // Only pages are rendered, and a page is asked for with GET or HEAD
  if (req.method !== 'GET' && req.method !== 'HEAD') {
    res.status(405).set('Allow', 'GET, HEAD').end();
    return;
  }

  const headers = new Headers();
  for (const name of FORWARDED_HEADERS) {
    const value = req.headers[name];
    if (typeof value === 'string') headers.set(name, value);
  }

  let request: Request;
  try {
    request = new Request(new URL(req.originalUrl, internalOrigin), { method: req.method, headers });
  } catch {
    res.status(400).end();
    return;
  }

  angularApp
    .handle(request)
    .then(response => (response ? writeResponseToNodeResponse(response, res) : next()))
    .catch(next);
});

if (isMainModule(import.meta.url) || process.env['pm_id']) {
  app.listen(port, error => {
    if (error) {
      throw error;
    }
    console.log(`pkgdiff SSR server listening on port ${port}, API through ${internalOrigin.origin}`);
  });
}

// Used by the Angular CLI dev server and during the build
export const reqHandler = createNodeRequestHandler(app);
