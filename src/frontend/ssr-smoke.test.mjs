// Starts the built render server (dist/Frontend/server/server.mjs) against a stand-in API and checks
// what a visitor and a crawler get. Run after `npm run build`: `npm run test:ssr`.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { createServer } from 'node:http';
import { dirname, join } from 'node:path';
import { after, before, test } from 'node:test';
import { fileURLToPath } from 'node:url';

const root = dirname(fileURLToPath(import.meta.url));
const serverEntry = join(root, 'dist', 'Frontend', 'server', 'server.mjs');

const version = (value, publishedDate = null) => ({
  version: value, publishedDate, isPrerelease: false, releaseNotes: `Notes of ${value}`,
  releaseNotesHtml: `<p>Notes of ${value}</p>`, releaseNotesSource: 'GitHub', entries: [], isEmpty: false,
});

const packages = {
  'npm/left-pad': {
    packageId: 'left-pad', registry: 'npm', latestVersion: '1.3.0',
    // Text from a registry is not trusted: it must not be able to end a script element
    description: 'Pads </script><script>window.pwned = 1</script> strings',
    authors: 'azer', iconUrl: '', projectUrl: '', gitHubRepositoryUrl: 'https://github.com/left-pad/left-pad',
    versions: [version('1.3.0', '2026-09-15T10:00:00Z'), version('1.2.0')],
  },
  'npm/chart.js': {
    packageId: 'chart.js', registry: 'npm', latestVersion: '4.5.0', description: 'Charts', authors: '',
    iconUrl: '', projectUrl: '', gitHubRepositoryUrl: null, versions: [version('4.5.0'), version('4.4.0')],
  },
};

const post = {
  id: 1, title: 'What changed in left-pad 1.3.0', slug: 'left-pad-1-3-0', packageId: 'left-pad', version: '1.3.0',
  content: '## Overview\n\nleft-pad 1.3.0 adds **padEnd**.', contentHtml: '<h2>Overview</h2><p>left-pad 1.3.0 adds <strong>padEnd</strong>.</p>',
  publishedAt: '2026-09-20T08:00:00Z',
};

let api;
let apiRequests = [];
let ssr;
let ssrOutput = '';
let base;

function answerApi(req, res) {
  const url = new URL(req.url, 'http://api');
  apiRequests.push({ path: url.pathname + url.search, forwardedFor: req.headers['x-forwarded-for'] ?? null });

  const json = (status, body) => {
    res.writeHead(status, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify(body));
  };

  const packageMatch = url.pathname.match(/^\/api\/Package\/([^/]+)\/([^/]+)$/);
  if (url.pathname === '/api/Package/registries') return json(200, [{ value: 'npm', label: 'npm', icon: '/icons/npm.svg', disabled: false }]);
  if (url.pathname === '/api/Package/cached') return json(200, Object.values(packages));
  if (url.pathname.endsWith('/suggest')) return json(200, []);
  if (packageMatch) {
    const found = packages[`${packageMatch[1]}/${decodeURIComponent(packageMatch[2])}`];
    return found ? json(200, found) : json(404, { error: 'Package not found' });
  }
  if (url.pathname === '/api/blog') {
    return json(200, { data: [{ ...post, excerpt: post.content }], page: 1, pageSize: 10, total: 1, totalPages: 1 });
  }
  if (url.pathname === `/api/blog/${post.slug}`) return json(200, post);
  if (url.pathname.startsWith('/api/blog/')) return json(404, { error: 'Not found' });
  if (url.pathname === '/api/contributors') return json(200, [{ gitHubUsername: 'octocat', gitHubUrl: 'https://github.com/octocat', mentionCount: 3, rank: 1 }]);
  return json(500, { error: `the stand-in API has no answer for ${url.pathname}` });
}

const listen = server => new Promise(resolve => server.listen(0, '127.0.0.1', () => resolve(server.address().port)));

async function freePort() {
  const probe = createServer();
  const port = await listen(probe);
  await new Promise(resolve => probe.close(resolve));
  return port;
}

async function page(path, init = {}) {
  const response = await fetch(base + path, { redirect: 'manual', ...init });
  return { status: response.status, html: await response.text(), headers: response.headers };
}

const count = (html, pattern) => html.match(pattern)?.length ?? 0;
const jsonLd = (html, key) =>
  [...html.matchAll(new RegExp(`<script[^>]*data-seo="${key}"[^>]*>(.*?)</script>`, 'gs'))].map(match => JSON.parse(match[1]));

before(async () => {
  assert.ok(existsSync(serverEntry), `${serverEntry} is missing: run "npm run build" first`);

  api = createServer(answerApi);
  const apiPort = await listen(api);
  const port = await freePort();
  base = `http://127.0.0.1:${port}`;

  ssr = spawn(process.execPath, [serverEntry], {
    env: { ...process.env, PORT: String(port), SSR_INTERNAL_ORIGIN: `http://127.0.0.1:${apiPort}` },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  ssr.stdout.on('data', chunk => (ssrOutput += chunk));
  ssr.stderr.on('data', chunk => (ssrOutput += chunk));

  for (let attempt = 0; ; attempt++) {
    try {
      if ((await fetch(`${base}/healthz`)).ok) break;
    } catch {
      // not listening yet
    }
    assert.ok(attempt < 100 && ssr.exitCode === null, `the render server did not start:\n${ssrOutput}`);
    await new Promise(resolve => setTimeout(resolve, 100));
  }
});

after(async () => {
  ssr?.kill();
  await new Promise(resolve => api.close(resolve));
});

test('the home page arrives rendered, with its meta and ready to be hydrated', async () => {
  const { status, html } = await page('/');

  assert.equal(status, 200);
  assert.match(html, /<h1[^>]*>\s*Compare package versions across NuGet, npm, PyPI, Maven, Pub &amp; Packagist/);
  assert.match(html, /What you can do with pkgdiff/);
  assert.match(html, /<title>pkgdiff — Compare NuGet, npm, PyPI, Maven, Pub &amp; Packagist Packages<\/title>/);
  assert.equal(count(html, /<link rel="canonical"/g), 1);
  assert.match(html, /<link rel="canonical" href="https:\/\/pkgdiff\.dev\/"/);
  assert.match(html, /ng-server-context="ssr"/);
});

test('a package page carries the package, its structured data once, and the visitor address reaches the API', async () => {
  apiRequests = [];
  const { status, html } = await page('/package/npm/left-pad', { headers: { 'X-Forwarded-For': '203.0.113.9' } });

  assert.equal(status, 200);
  assert.match(html, /<title>left-pad v1\.3\.0 — NPM Release Notes \| pkgdiff<\/title>/);
  assert.match(html, /<link rel="canonical" href="https:\/\/pkgdiff\.dev\/package\/npm\/left-pad"/);
  assert.match(html, /property="og:url" content="https:\/\/pkgdiff\.dev\/package\/npm\/left-pad"/);
  assert.match(html, /Notes of 1\.3\.0/);

  const [application, ...moreApplications] = jsonLd(html, 'page');
  assert.equal(moreApplications.length, 0);
  assert.equal(application['@type'], 'SoftwareApplication');
  assert.equal(application.softwareVersion, '1.3.0');
  const breadcrumbs = jsonLd(html, 'breadcrumb');
  assert.equal(breadcrumbs.length, 1);
  assert.deepEqual(breadcrumbs[0].itemListElement.map(item => item.name), ['Home', 'Packages', 'NPM', 'left-pad']);

  // The description from the registry is data everywhere it appears, never markup
  assert.equal(application.description, 'Pads </script><script>window.pwned = 1</script> strings');
  assert.doesNotMatch(html, /<script>window\.pwned/);

  const metadataCall = apiRequests.find(request => request.path === '/api/Package/npm/left-pad');
  assert.ok(metadataCall, `the API was not asked for the package: ${JSON.stringify(apiRequests)}`);
  assert.equal(metadataCall.forwardedFor, '203.0.113.9');

  // The answer travels with the page, so the browser does not ask again
  assert.match(html, /<script id="ng-state" type="application\/json">/);
});

test('a version page is its own address', async () => {
  const { status, html } = await page('/package/npm/left-pad/1.2.0');

  assert.equal(status, 200);
  assert.match(html, /<title>left-pad v1\.2\.0 — NPM Release Notes \| pkgdiff<\/title>/);
  assert.match(html, /<link rel="canonical" href="https:\/\/pkgdiff\.dev\/package\/npm\/left-pad\/1\.2\.0"/);
});

test('a package whose name ends like a file is a page, not a missing file', async () => {
  const { status, html } = await page('/package/npm/chart.js');

  assert.equal(status, 200);
  assert.match(html, /<title>chart\.js v4\.5\.0 — NPM Release Notes \| pkgdiff<\/title>/);
});

test('an unknown package is a real 404 that asks not to be indexed', async () => {
  const { status, html } = await page('/package/npm/no-such-package');

  assert.equal(status, 404);
  assert.match(html, /<meta name="robots" content="noindex, follow">/);
  assert.equal(jsonLd(html, 'page').length, 0);
});

test('the blog list and a post are rendered; an unknown post is a 404', async () => {
  const list = await page('/blog');
  assert.equal(list.status, 200);
  assert.match(list.html, /<a[^>]*href="\/blog\/left-pad-1-3-0"[^>]*>\s*What changed in left-pad 1\.3\.0/);

  const article = await page('/blog/left-pad-1-3-0');
  assert.equal(article.status, 200);
  assert.match(article.html, /<title>What changed in left-pad 1\.3\.0 — pkgdiff Blog<\/title>/);
  assert.match(article.html, /<h2>Overview<\/h2>/);
  assert.equal(jsonLd(article.html, 'page')[0]['@type'], 'BlogPosting');

  const missing = await page('/blog/no-such-post');
  assert.equal(missing.status, 404);
  assert.match(missing.html, /Article Not Found/);
});

test('the other public pages are rendered', async () => {
  for (const [path, text] of [
    ['/packages', /left-pad/],
    ['/contributors', /octocat/],
    ['/terms', /Terms/],
    ['/privacy', /Privacy/],
    ['/cookies', /Cookie/],
  ]) {
    const { status, html } = await page(path);
    assert.equal(status, 200, path);
    assert.match(html, text, path);
    assert.match(html, /ng-server-context="ssr"/, path);
  }
});

test('an address that is no page of the application is a 404 page', async () => {
  const { status, html } = await page('/no/such/page');

  assert.equal(status, 404);
  assert.match(html, /<title>404 - Page Not Found \| pkgdiff<\/title>/);
  assert.match(html, /<meta name="robots" content="noindex, follow">/);
});

test('the admin area is sent as the client-side shell and nothing is fetched for it', async () => {
  apiRequests = [];
  const { status, html } = await page('/admin/users');

  assert.equal(status, 200);
  assert.doesNotMatch(html, /ng-server-context="ssr"/);
  assert.match(html, /<app-root>/);
  assert.deepEqual(apiRequests, []);
});

test('only page requests are rendered', async () => {
  const response = await fetch(`${base}/`, { method: 'POST', body: 'x' });

  assert.equal(response.status, 405);
  assert.equal(response.headers.get('allow'), 'GET, HEAD');
  assert.equal(response.headers.get('x-powered-by'), null);
});

test('a render logs no error', () => {
  assert.doesNotMatch(ssrOutput, /ERROR|NG0\d+|ReferenceError|TypeError/, ssrOutput);
});
