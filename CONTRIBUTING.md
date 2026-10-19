# Contributing to pkgdiff

Thank you for helping. Bug reports, registry quirks (a package whose release notes are not found)
and pull requests are all welcome.

## Before you start

- For a bug, open an issue with the registry, the package, the two versions and what you expected.
- For a larger change, open an issue first so we can agree on the approach.
- Security problems go through [SECURITY.md](SECURITY.md), not through an issue.

## Setting up

Follow "Running it locally" in the [README](README.md). You need the .NET 10 SDK, Node.js 22 and
a PostgreSQL and Redis you can reach on `localhost`.

## Branches and pull requests

- Branch from `develop` and open the pull request into `develop`. `master` only receives releases.
- Keep a pull request about one thing. CI (backend tests, frontend build and tests, dependency gate)
  must be green.
- Commit messages follow Conventional Commits: `feat(scope): ...`, `fix(scope): ...`, `docs: ...`.

## Code conventions

**Backend (C#):** Clean Architecture by project. An interface lives in `PkgDiff.Application/Interfaces`,
its implementation in `PkgDiff.Infrastructure/Services` (or `PkgDiff.Application/Services` for pure
orchestration), and it is registered in `PkgDiff.API/Extensions`. File-scoped namespaces, nullable
reference types on, `Async` suffix on awaitable methods, `_camelCase` private fields. Log with message
templates (`_logger.LogError(ex, "... {PackageId}", id)`), never with string interpolation. Errors go
back as `{ "error": "..." }` without exception details. Schema changes are EF Core migrations made
with the tool, never edited by hand.

**Frontend (Angular):** standalone components with `ChangeDetectionStrategy.OnPush`, state in signals,
`inject()` instead of constructor parameters, templates in their own `.html` file, the built-in control
flow (`@if`, `@for` with a stable `track`). All HTTP goes through `ApiService`; a component never
injects `HttpClient`. No `any`. Every HTTP subscription handles its error and resets its loading flag.

**Both:** comments in English, explaining why rather than what. When a backend DTO changes, its
TypeScript twin in `src/frontend/src/app/core/models` changes in the same pull request.

## Dependencies

Packages are locked (`packages.lock.json`, `package-lock.json`) and images are pinned by digest.
Add a dependency only when it earns its place, and update the lock file in the same commit.

## License

By contributing you agree that your contribution is licensed under the [MIT License](LICENSE).
