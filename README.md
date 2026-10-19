# pkgdiff

> Every release note between two versions of a package, in one timeline: NuGet, npm, PyPI and pub.dev.

**pkgdiff** collects the release notes between any two versions of a package and shows them as one
categorized timeline, so you can decide whether an upgrade is safe without jumping between registry
pages, GitHub releases and changelog files.

🌐 **Live:** [pkgdiff.dev](https://pkgdiff.dev)

---

## What it does

- Lists every version between your current and your target version
- Finds the release notes of each one: GitHub Releases, the registry page or the changelog
- Sorts the changes into breaking changes, features, fixes, deprecations and security fixes
- Shows the result as one timeline you can export as Markdown, JSON, TOML or PDF

## Supported registries

| Registry | Status |
|----------|--------|
| NuGet | ✅ |
| npm | ✅ |
| PyPI | ✅ |
| pub.dev | ✅ |

## Features

- **Version diff**: pick a "from" and a "to" version and read everything that changed in between
- **Several sources per registry**: GitHub-first, registry-first and pub.dev strategies, chosen per package
- **Categorized changes** with a summary of breaking changes, features and fixes
- **Cache**: analysed packages are stored for 30 days and refreshed by background jobs
- **Live progress** over SignalR while an analysis runs, and a live feed of new versions on the home page
- **Server-side rendering**: every public page arrives as complete HTML, with real 404s for unknown packages
- **Package trust**: known vulnerabilities of every version (OSV), what an upgrade fixes and what it introduces, a warning for versions listed as malware, and project health (OpenSSF Scorecard, licenses, provenance)
- **Dependency audit**: paste a package.json, package-lock.json, .csproj, Directory.Packages.props, packages.config, packages.lock.json, requirements.txt, pyproject.toml, poetry.lock, Pipfile(.lock), pubspec.yaml or pubspec.lock and get a shareable report of every package's known vulnerabilities and the version that fixes them (https://pkgdiff.dev/audit)
- **Export** as Markdown, JSON, TOML or PDF
- **Admin panel** with roles and permissions, request and scrape logs, IP blocking, jobs and settings
- **Monitoring**: Prometheus metrics and a provisioned Grafana dashboard

## Security data and attribution

Security data is collected by background jobs and never while a visitor waits:

- **Vulnerabilities**: [OSV.dev](https://osv.dev) for NuGet, npm, PyPI and pub.dev, including the
  [OpenSSF malicious-packages](https://github.com/ossf/malicious-packages) records.
- **Project health**: [deps.dev](https://deps.dev) for npm, PyPI and NuGet (licenses, provenance and
  the OpenSSF Scorecard of the source repository). deps.dev data is licensed under
  [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) and is shown with that attribution.
- **Behaviour analysis** (optional, off by default): [Socket.dev](https://socket.dev) alerts and scores for
  npm, PyPI and NuGet, shown only after Socket has allowed public display.
- **Scorecard for pub.dev**: the [OpenSSF Scorecard API](https://api.securityscorecards.dev) for
  packages whose repository is on GitHub.

## Tech stack

**Backend:** .NET 10, ASP.NET Core, Entity Framework Core with PostgreSQL 18, Hangfire, SignalR,
Redis, Polly, AngleSharp, QuestPDF.

**Frontend:** Angular 21 (standalone components, signals, zoneless) with `@angular/ssr`, Tailwind CSS,
Lucide icons.

**Operations:** Docker Compose on one host, images built and scanned in GitHub Actions and pulled from
GHCR, nginx in front, Prometheus and Grafana.

## Repository layout

```
src/
  backend/
    PkgDiff.API/             controllers, middleware, hubs, composition root
    PkgDiff.Application/     services, interfaces (ports), background jobs
    PkgDiff.Domain/          entities and persistence contracts
    PkgDiff.Infrastructure/  EF Core, registry clients, scraping
    PkgDiff.Shared/          DTOs and constants
    PkgDiff.Tests/           unit and integration tests
  frontend/                  Angular application and its render server (src/server.ts)
deploy/                    deploy, rollback and backup scripts, host nginx, Grafana provisioning
.github/scripts/           dependency gate, QuestPDF CPU check and their tests
```

## Running it locally

Prerequisites: .NET 10 SDK, Node.js 22 with npm, PostgreSQL and Redis on `localhost`
(or `docker compose up -d db redis` with a `.env` made from `.env.example`).

### Backend

`src/backend/PkgDiff.API/appsettings.json` holds the defaults and no secrets. Put your own values in
`src/backend/PkgDiff.API/appsettings.Development.json`, which git ignores:

```json
{
  "ConnectionStrings": { "DefaultConnection": "Host=localhost;Database=pkgdiffdb;Username=postgres;Password=..." },
  "JwtSettings": { "SecretKey": "<32 or more random bytes>", "Issuer": "PkgDiffAPI", "Audience": "PkgDiffClient" },
  "Security": { "SettingsEncryptionKey": "<base64 of 32 random bytes>" },
  "AdminSettings": { "AdminPassword": "<password of the first administrator>" }
}
```

```bash
cd src/backend
dotnet run --project PkgDiff.API
```

The API applies its migrations on start and listens on `http://localhost:5054`
(Swagger at `/swagger` in Development).

### Frontend

```bash
cd src/frontend
npm ci
npm start
```

The application runs on `http://localhost:4200`, rendered on the server by the development server
as in production.

### Tests

```bash
dotnet test --project src/backend/PkgDiff.Tests/PkgDiff.Tests.csproj
cd src/frontend && npm test -- --watch=false && npm run build && npm run test:ssr
node --test .github/scripts/tests/*.test.mjs
```

Backend tests that need PostgreSQL run when `PKGDIFF_TEST_POSTGRES` holds a connection string
(without a database name); without it they are skipped.

## CI and deployment

- **CI** (`.github/workflows/ci.yml`) runs on every pull request into `develop` or `master`: backend
  tests against PostgreSQL, the frontend build, unit and server-rendering tests, the script tests and
  the dependency gate (`.github/scripts/check-deps.sh`: locked restores and known vulnerabilities).
- **Publish** (`.github/workflows/publish.yml`) runs on a push to `develop` or `master`: the same
  gate, then the backend and frontend images are built, started, scanned with Trivy and pushed to
  GHCR (`ghcr.io/pkgdiff-dev/pkgdiff-backend`, `-frontend`).
- **Deploy**: the server pulls a tag with `deploy/deploy.sh <tag>`, which takes a database dump,
  switches the containers and goes back to the previous images by itself when the new ones are not
  healthy. With the repository variable `PKGDIFF_AUTO_DEPLOY=true` every push to `develop` is
  deployed this way over a restricted SSH key (`.github/workflows/deploy.yml`, `deploy/remote.sh`).


## API

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/package/registries` | Registries that can be analysed |
| `GET` | `/api/package/{registry}/{packageId}` | Package metadata and its versions |
| `GET` | `/api/package/{registry}/suggest?q=` | Package name suggestions |
| `POST` | `/api/package/analyze` | Release notes between two versions |
| `GET` | `/api/package/{registry}/{packageId}/export/pdf` | Release notes as PDF |
| `POST` | `/api/package/export/diff/pdf` | A comparison as PDF |
| `POST` | `/api/audit/parse` | Read the packages of a dependency file |
| `POST` | `/api/audit` | Start a dependency audit; returns its id |
| `GET` | `/api/audit/{id}` | The audit report (kept for 30 days) |
| `GET` | `/health/ready` | Ready when the database and Redis answer |

## Contributing and security

See [CONTRIBUTING.md](CONTRIBUTING.md). Please report vulnerabilities privately as described in
[SECURITY.md](SECURITY.md).

## License

MIT, see [LICENSE](LICENSE).
