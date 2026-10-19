# Security policy

## Reporting a vulnerability

Please do not open a public issue for a security problem. Report it privately through
[GitHub's private vulnerability reporting](https://github.com/pkgdiff-dev/pkgdiff/security/advisories/new)
for this repository.

Include what is affected (an endpoint, a page, a script), how to reproduce it, and what an attacker
could do with it. You will get an answer within a week. Please give us a reasonable time to fix the
problem before you publish anything about it.

## Scope

In scope: the application at https://pkgdiff.dev and https://api.pkgdiff.dev, the code in this
repository, and the published images `ghcr.io/pkgdiff-dev/pkgdiff-backend` and `-frontend`.

Out of scope: denial of service by volume, findings from automated scanners without a demonstrated
impact, missing headers without a concrete attack, and the third-party registries and services
pkgdiff reads from (NuGet, npm, PyPI, pub.dev, GitHub).

## What the project does to stay safe

- Dependencies are locked; CI fails on a known vulnerability in a NuGet or npm package
  (`.github/scripts/check-deps.sh`) and every published image is scanned with Trivy. Dependabot proposes
  updates weekly; the few deliberate holds are kept in `.github/dependabot.yml`.
- Outbound requests to addresses taken from third-party content go through a guard that refuses
  private, loopback and link-local targets.
- Secret settings are encrypted at rest; admin endpoints are protected by role and permission
  policies; containers run as unprivileged users and only the host nginx is reachable from the
  internet.
