# Contributing to pkgdiff

Thank you for helping. Bug reports, registry quirks (a package whose release notes are not found)
and pull requests are all welcome.

## Before you start

Every change starts with an issue. Open one first — a bug, a registry quirk, a feature or a chore —
using the matching issue template, so the work has a tracked rationale before any code is written.

- For a bug, include the registry, the package, the two versions and what you expected.
- For a feature or a larger change, describe the approach in the issue so we can agree on it first.
- Security problems go through [SECURITY.md](SECURITY.md), never a public issue.

## Setting up

Follow "Running it locally" in the [README](README.md). You need the .NET 10 SDK, Node.js 22 and
a PostgreSQL and Redis you can reach on `localhost`.

## Branches and pull requests

We work issue-first, with the pull request opened alongside the issue rather than at the end:

- Once an issue exists, branch from `develop` and open the pull request into `develop` early — a
  draft is fine — so the work is visible while it is still in progress. `master` only receives
  releases.
- Link the pull request to its issue with a closing keyword (`Closes #123`) so merging the pull
  request closes the issue. A pull request without a linked issue will be asked to add one.
- Keep a pull request about one thing and fill in the pull request template, in English. CI
  (backend tests, frontend build and tests, dependency gate) must be green.
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

**Both:** few comments, in English. Name things so the code explains itself; comment only a *why* the
code cannot show, never the task or the package that prompted the change (that belongs in the pull
request description). When a backend DTO changes, its TypeScript twin in
`src/frontend/src/app/core/models` changes in the same pull request.

## Dependencies

Packages are locked (`packages.lock.json`, `package-lock.json`) and images are pinned by digest.
Add a dependency only when it earns its place, and update the lock file in the same commit.

## License

By contributing you agree that your contribution is licensed under the [MIT License](LICENSE).
