import { AngularNodeAppEngine, createNodeRequestHandler, isMainModule, writeResponseToNodeResponse } from '@angular/ssr/node';
import express from 'express';
import { join } from 'node:path';

const port = Number(process.env['PORT'] ?? 4000);
const browserDistFolder = join(import.meta.dirname, '../browser');

// Relative API calls resolve against this origin, so it must answer /api from inside the network
const internalOrigin = new URL(process.env['SSR_INTERNAL_ORIGIN'] ?? `http://localhost:${port}`);

// Only these visitor headers reach the render
const FORWARDED_HEADERS = ['accept', 'accept-language', 'user-agent', 'x-forwarded-for'];

const app = express();
app.disable('x-powered-by');

// Angular serves the unrendered page for an untrusted host or proxy header; only these are passed
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

export const reqHandler = createNodeRequestHandler(app);
