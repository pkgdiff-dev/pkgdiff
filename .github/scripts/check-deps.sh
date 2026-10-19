#!/usr/bin/env bash
# Supply-chain gate: NuGet and npm audits, plus image scans with TRIVY=1. Exit 0 when all pass.
# Env: TRIVY=1, CHECK_DEPS_DOCKER=auto|1|0, PKGDIFF_ROOT=<dir>, BACKEND_IMAGE, FRONTEND_IMAGE.
set -uo pipefail

export DOTNET_CLI_UI_LANGUAGE=en DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1

# The scanner gets the Docker socket, so it is referenced by tag and digest only.
TRIVY_IMAGE='aquasec/trivy:0.74.0@sha256:62b1e65e8869bc4b4c6aa4fa2b21595256c7c2f6018a9d9ad61caf87187c1969'
TRIVY_CACHE_VOLUME='pkgdiff-trivy-cache'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="${PKGDIFF_ROOT:-$SCRIPT_DIR/../..}"
if ! ROOT="$(cd "$ROOT" 2>/dev/null && pwd)"; then
  echo "check-deps: cannot run: PKGDIFF_ROOT is not a directory (${PKGDIFF_ROOT:-})" >&2
  exit 1
fi

MODE="${CHECK_DEPS_DOCKER:-auto}"
case "$MODE" in
  auto | 1 | 0) ;;
  *)
    echo "check-deps: cannot run: CHECK_DEPS_DOCKER must be auto, 1 or 0 (got '$MODE')" >&2
    exit 1
    ;;
esac

# Git Bash rewrites POSIX-looking arguments before docker.exe sees them; turn that off.
DOCKER_SOCKET='/var/run/docker.sock'
case "$(uname -s 2>/dev/null)" in
  MINGW* | MSYS* | CYGWIN*)
    export MSYS_NO_PATHCONV=1
    DOCKER_SOCKET='//var/run/docker.sock'
    ;;
esac

PASSED=0
FAILED=0
UNCHECKED=0
CONTAINERS=''
WORK=''

cleanup() {
  local cid
  for cid in $CONTAINERS; do
    docker rm -f "$cid" >/dev/null 2>&1
  done
  if [ -n "$WORK" ]; then
    rm -rf "$WORK"
  fi
}
trap cleanup EXIT
trap 'exit 130' INT TERM

if ! WORK="$(mktemp -d 2>/dev/null)" || [ -z "$WORK" ]; then
  WORK=''
  echo "check-deps: cannot run: mktemp -d failed" >&2
  exit 1
fi

pass() {
  echo "PASS $1"
  PASSED=$((PASSED + 1))
}

fail() {
  echo "FAIL $1: $2"
  FAILED=$((FAILED + 1))
}

unchecked() {
  echo "FAIL $1: could not check - $2"
  FAILED=$((FAILED + 1))
  UNCHECKED=$((UNCHECKED + 1))
}

evidence() {
  sed 's/^/    /'
}

have() {
  command -v "$1" >/dev/null 2>&1
}

read_rc() {
  if [ -s "$1" ]; then
    tr -d '[:space:]' <"$1"
  else
    echo missing
  fi
}

one_line() {
  tr '\r\n' '  ' | sed 's/[[:space:]][[:space:]]*/ /g; s/^ //; s/ $//'
}

build_stage_image() {
  [ -f "$1" ] || return 1
  tr -d '\r' <"$1" |
    sed -n 's/^FROM[[:space:]][[:space:]]*\([^[:space:]][^[:space:]]*\)[[:space:]][[:space:]]*[Aa][Ss][[:space:]][[:space:]]*build[[:space:]]*$/\1/p' |
    head -n 1
}

# No `writer | grep -q` for decisions: under pipefail a SIGPIPE'd writer turns a match into a miss.
is_pinned() {
  grep -Eq '^[A-Za-z0-9][A-Za-z0-9./_-]*:[A-Za-z0-9._-]+@sha256:[0-9a-f]{64}$' <<<"$1"
}

use_container() {
  local tool
  case "$MODE" in
    1) return 0 ;;
    0) return 1 ;;
  esac
  for tool in "$@"; do
    have "$tool" || return 0
  done
  return 1
}

# Copies files in and out instead of bind-mounting, so host file sharing with Docker doesn't matter.
run_in_container() {
  local image="$1" dir="$2" cid rc
  shift 2
  if ! cid="$(docker create "$@" -w /work --entrypoint sh "$image" /work/run.sh 2>"$dir/container-create.log")"; then
    return 125
  fi
  CONTAINERS="$CONTAINERS $cid"
  if ! tar -C "$dir" -cf - . | docker cp - "$cid:/work" >>"$dir/container-create.log" 2>&1; then
    docker rm -f "$cid" >/dev/null 2>&1
    return 125
  fi
  docker start -a "$cid" >"$dir/container.log" 2>&1
  rc=$?
  docker cp "$cid:/work/out" - 2>/dev/null | tar -C "$dir" -xf - 2>/dev/null
  docker rm -f "$cid" >/dev/null 2>&1
  return "$rc"
}

check_sdk_image_pin() {
  local name='sdk-image-pin'
  local dockerfile="$ROOT/src/backend/Dockerfile"
  local props="$ROOT/src/backend/Directory.Build.props"
  local harness="$ROOT/.github/scripts/questpdf-cpu-check/Dockerfile"
  local file from_dockerfile from_props from_harness

  for file in "$dockerfile" "$props" "$harness"; do
    if [ ! -f "$file" ]; then
      unchecked "$name" "missing file ${file#"$ROOT"/}"
      return
    fi
  done

  from_dockerfile="$(build_stage_image "$dockerfile")"
  from_harness="$(build_stage_image "$harness")"
  from_props="$(tr -d '\r' <"$props" | grep -oE 'mcr\.microsoft\.com/dotnet/sdk:[A-Za-z0-9._-]+(@sha256:[0-9a-f]{64})?' | sort -u)"

  if ! is_pinned "$from_dockerfile"; then
    fail "$name" "the build stage of src/backend/Dockerfile is not pinned as tag@sha256 ('$from_dockerfile')"
    return
  fi
  if [ "$from_props" != "$from_dockerfile" ] || [ "$from_harness" != "$from_dockerfile" ]; then
    {
      echo "src/backend/Dockerfile:                  $from_dockerfile"
      echo "src/backend/Directory.Build.props:       $(printf '%s' "$from_props" | one_line)"
      echo ".github/scripts/questpdf-cpu-check/Dockerfile: $from_harness"
    } | evidence
    fail "$name" "the SDK image reference differs between the three files; the lock files are only valid for one SDK image"
    return
  fi
  pass "$name"
}

prepare_nuget() {
  local dir="$1" rel
  mkdir -p "$dir" || return 1
  [ -f "$ROOT/src/backend/PkgDiff.slnx" ] || return 1
  (
    cd "$ROOT/src/backend" &&
      find . -maxdepth 2 -type f \( -name '*.slnx' -o -name '*.csproj' -o -name '*.props' -o -name '*.targets' \
        -o -name 'nuget.config' -o -name 'packages.lock.json' \)
  ) >"$WORK/nuget-files.txt" || return 1
  while IFS= read -r rel; do
    mkdir -p "$dir/$(dirname "$rel")" && cp "$ROOT/src/backend/$rel" "$dir/$rel" || return 1
  done <"$WORK/nuget-files.txt"

  cat >"$dir/run.sh" <<'EOF'
#!/bin/sh
# Runs in the copied Backend manifests, on this machine or in the SDK container.
mkdir -p out
dotnet --version >out/sdk-version.txt 2>&1

# The gate: exactly what the lock files record, with known-vulnerable packages as errors.
dotnet restore PkgDiff.slnx --locked-mode -p:ContinuousIntegrationBuild=true >out/locked-restore.log 2>&1
echo "$?" >out/locked-restore.rc

# A plain restore, so the report below exists even when the gate above failed.
dotnet restore PkgDiff.slnx >out/plain-restore.log 2>&1
echo "$?" >out/plain-restore.rc

# The exit status of this command is 0 even with findings, and its text output is localized: only the JSON counts.
dotnet list PkgDiff.slnx package --vulnerable --include-transitive --format json >out/vulnerable.json 2>out/vulnerable.err
echo "$?" >out/vulnerable.rc
EOF
}

restore_errors() {
  tr -d '\r' <"$1" | sed -n 's/.*\(error [A-Z][A-Z]*[0-9][0-9]*: .*\)$/\1/p' | sed 's/ \[[^][]*\]$//' | sort -u
}

check_nuget() {
  local dir="$WORK/nuget" out="$WORK/nuget/out"
  local image rc errors count first projects reported findings

  if ! prepare_nuget "$dir"; then
    unchecked nuget-locked-restore "src/backend/PkgDiff.slnx and its project files could not be copied from $ROOT"
    unchecked nuget-vulnerable "src/backend/PkgDiff.slnx and its project files could not be copied from $ROOT"
    return
  fi

  if use_container dotnet; then
    image="$(build_stage_image "$ROOT/src/backend/Dockerfile")"
    if ! is_pinned "$image"; then
      unchecked nuget-locked-restore "no tag@sha256 build-stage image in src/backend/Dockerfile ('$image')"
      unchecked nuget-vulnerable "no tag@sha256 build-stage image in src/backend/Dockerfile ('$image')"
      return
    fi
    if ! have docker; then
      unchecked nuget-locked-restore "dotnet runs in a container here, but docker is not on PATH"
      unchecked nuget-vulnerable "dotnet runs in a container here, but docker is not on PATH"
      return
    fi
    echo "check-deps: NuGet checks run in $image"
    run_in_container "$image" "$dir" \
      -e DOTNET_CLI_UI_LANGUAGE=en -e DOTNET_NOLOGO=1 -e DOTNET_CLI_TELEMETRY_OPTOUT=1
    rc=$?
    if [ "$rc" -ne 0 ]; then
      cat "$dir/container-create.log" "$dir/container.log" 2>/dev/null | tail -n 15 | evidence
      unchecked nuget-locked-restore "the SDK container did not run (exit $rc)"
      unchecked nuget-vulnerable "the SDK container did not run (exit $rc)"
      return
    fi
  elif have dotnet; then
    echo "check-deps: NuGet checks run with dotnet $(dotnet --version 2>/dev/null | one_line) from PATH"
    (cd "$dir" && sh ./run.sh)
  else
    unchecked nuget-locked-restore "dotnet is not on PATH and CHECK_DEPS_DOCKER=0"
    unchecked nuget-vulnerable "dotnet is not on PATH and CHECK_DEPS_DOCKER=0"
    return
  fi

  rc="$(read_rc "$out/locked-restore.rc")"
  if [ "$rc" = "0" ] && grep -Eq 'NU190[05]' "$out/locked-restore.log"; then
    # A cached restore succeeds with only a warning when advisory data can't load: no audit ran.
    tr -d '\r' <"$out/locked-restore.log" | grep -oE 'warning NU190[05]: [^[]*' | sort -u | evidence
    unchecked nuget-locked-restore "the restore succeeded, but the advisory data could not be loaded (NU1900/NU1905), so the audit did not run"
  elif [ "$rc" = "0" ]; then
    pass nuget-locked-restore
  else
    errors="$(restore_errors "$out/locked-restore.log" 2>/dev/null)"
    count="$(printf '%s\n' "$errors" | grep -c . || true)"
    first="$(printf '%s\n' "$errors" | head -n 1)"
    if [ -n "$errors" ]; then
      printf '%s\n' "$errors" | evidence
    else
      tail -n 15 "$out/locked-restore.log" 2>/dev/null | evidence
    fi
    if grep -Eq 'error NU190[1-4]:' <<<"$errors"; then
      # NU1904 (critical) sorts last, so the reverse order names the most severe advisory first.
      first="$(printf '%s\n' "$errors" | grep -E 'error NU190[1-4]:' | sort -r | head -n 1)"
      fail nuget-locked-restore "known-vulnerable package, $count error(s): $first"
    elif grep -Eq 'error NU1004:' <<<"$errors"; then
      fail nuget-locked-restore "the lock files do not match the project files, $count error(s): $first"
    else
      unchecked nuget-locked-restore "restore exit $rc: ${first:-no NuGet error line in the output}"
    fi
  fi

  rc="$(read_rc "$out/plain-restore.rc")"
  if [ "$rc" != "0" ]; then
    restore_errors "$out/plain-restore.log" 2>/dev/null | head -n 10 | evidence
    unchecked nuget-vulnerable "the plain restore before the report failed (exit $rc)"
    return
  fi
  rc="$(read_rc "$out/vulnerable.rc")"
  if [ "$rc" != "0" ]; then
    cat "$out/vulnerable.err" "$out/vulnerable.json" 2>/dev/null | tail -n 10 | evidence
    unchecked nuget-vulnerable "dotnet list package --vulnerable failed (exit $rc)"
    return
  fi
  projects="$(grep -c '\.csproj$' "$WORK/nuget-files.txt" || true)"
  reported="$(grep -c '"path"' "$out/vulnerable.json" 2>/dev/null || true)"
  if ! grep -q '"parameters"' "$out/vulnerable.json" 2>/dev/null ||
    ! grep -q -- '--vulnerable' "$out/vulnerable.json" ||
    [ "$projects" -eq 0 ] || [ "$reported" != "$projects" ]; then
    head -n 10 "$out/vulnerable.json" 2>/dev/null | evidence
    unchecked nuget-vulnerable "the JSON report is not usable (${reported:-0} of $projects projects reported)"
    return
  fi
  if grep -q '"problems"' "$out/vulnerable.json"; then
    grep -oE '"text": *"[^"]*"' "$out/vulnerable.json" | sort -u | head -n 10 | evidence
    unchecked nuget-vulnerable "the JSON report lists problems instead of a complete result"
    return
  fi
  if grep -q '"vulnerabilities"' "$out/vulnerable.json"; then
    findings="$(grep -oE '"(id|resolvedVersion|severity|advisoryurl)": *"[^"]*"' "$out/vulnerable.json" |
      awk -F'"' '
        $2 == "id" { id = $4 }
        $2 == "resolvedVersion" { version = $4 }
        $2 == "severity" { severity = $4 }
        $2 == "advisoryurl" { print id " " version " (" severity ", " $4 ")" }
      ' | sort -u)"
    printf '%s\n' "$findings" | evidence
    fail nuget-vulnerable "$(printf '%s\n' "$findings" | grep -c . || true) vulnerable package advisory(ies): $(printf '%s' "$findings" | cut -d' ' -f1,2 | sort -u | one_line)"
    return
  fi
  pass nuget-vulnerable
}

prepare_npm() {
  local dir="$1" file
  mkdir -p "$dir/Frontend" "$dir/scripts" || return 1
  for file in package.json package-lock.json; do
    cp "$ROOT/src/frontend/$file" "$dir/Frontend/$file" 2>/dev/null || return 1
  done
  # Without the allowlist the gate reports "could not check" by itself.
  cp "$ROOT/src/frontend/audit-allowlist.json" "$dir/Frontend/audit-allowlist.json" 2>/dev/null
  cp "$SCRIPT_DIR/npm-audit-gate.mjs" "$dir/scripts/npm-audit-gate.mjs" 2>/dev/null || return 1

  cat >"$dir/run.sh" <<'EOF'
#!/bin/sh
# Runs on the copied Frontend manifests, on this machine or in the Node container.
mkdir -p out
node --version >out/node-version.txt 2>&1

# npm takes the audit scope from NODE_ENV and from npm_config_omit / npm_config_include (any letter
# case, also the legacy names). The caller's values must not decide what is audited.
for name in $(env | sed -n 's/^\([A-Za-z_][A-Za-z0-9_]*\)=.*/\1/p'); do
  case "$(printf '%s' "$name" | tr '[:upper:]' '[:lower:]')" in
    node_env | npm_config_omit | npm_config_include | npm_config_production | npm_config_only | npm_config_also | npm_config_dev)
      unset "$name"
      ;;
  esac
done

# Production dependencies: any advisory fails. The level is explicit so a local npm config cannot raise it.
(cd Frontend && npm audit --omit=dev --audit-level=low --json) >out/audit-prod.json 2>out/audit-prod.err
echo "$?" >out/audit-prod.rc

# All dependencies (the gate asks npm for --include=dev itself): allowlist-aware gate.
# Exit 0 = pass, 1 = rule broken, 2 = could not check.
node scripts/npm-audit-gate.mjs Frontend >out/gate.log 2>&1
echo "$?" >out/gate.rc
EOF
}

check_npm() {
  local dir="$WORK/npm" out="$WORK/npm/out"
  local image rc packages

  if ! prepare_npm "$dir"; then
    unchecked npm-audit-prod "src/frontend/package.json, src/frontend/package-lock.json or .github/scripts/npm-audit-gate.mjs is missing"
    unchecked npm-audit-high "src/frontend/package.json, src/frontend/package-lock.json or .github/scripts/npm-audit-gate.mjs is missing"
    return
  fi

  if use_container npm node; then
    image="$(build_stage_image "$ROOT/src/frontend/Dockerfile")"
    if ! is_pinned "$image"; then
      unchecked npm-audit-prod "no tag@sha256 build-stage image in src/frontend/Dockerfile ('$image')"
      unchecked npm-audit-high "no tag@sha256 build-stage image in src/frontend/Dockerfile ('$image')"
      return
    fi
    if ! have docker; then
      unchecked npm-audit-prod "npm runs in a container here, but docker is not on PATH"
      unchecked npm-audit-high "npm runs in a container here, but docker is not on PATH"
      return
    fi
    echo "check-deps: npm checks run in $image"
    run_in_container "$image" "$dir"
    rc=$?
    if [ "$rc" -ne 0 ]; then
      cat "$dir/container-create.log" "$dir/container.log" 2>/dev/null | tail -n 15 | evidence
      unchecked npm-audit-prod "the Node container did not run (exit $rc)"
      unchecked npm-audit-high "the Node container did not run (exit $rc)"
      return
    fi
  elif have npm && have node; then
    echo "check-deps: npm checks run with npm $(npm --version 2>/dev/null | one_line) from PATH"
    (cd "$dir" && sh ./run.sh)
  else
    unchecked npm-audit-prod "npm and node are not on PATH and CHECK_DEPS_DOCKER=0"
    unchecked npm-audit-high "npm and node are not on PATH and CHECK_DEPS_DOCKER=0"
    return
  fi

  # npm audit exits non-zero on findings and on errors; only the JSON tells them apart.
  rc="$(read_rc "$out/audit-prod.rc")"
  if ! grep -q '"auditReportVersion"' "$out/audit-prod.json" 2>/dev/null; then
    cat "$out/audit-prod.json" "$out/audit-prod.err" 2>/dev/null | tail -n 10 | evidence
    unchecked npm-audit-prod "npm audit --omit=dev returned no audit report (exit $rc)"
  elif [ "$rc" = "0" ]; then
    pass npm-audit-prod
  else
    packages="$(grep -oE '"(name|severity)": *"[^"]*"' "$out/audit-prod.json" |
      awk -F'"' '$2 == "name" { name = $4 } $2 == "severity" { print name " (" $4 ")" }' | sort -u)"
    printf '%s\n' "$packages" | evidence
    fail npm-audit-prod "production dependencies have advisories: $(printf '%s' "$packages" | one_line)"
  fi

  rc="$(read_rc "$out/gate.rc")"
  case "$rc" in
    0)
      tr -d '\r' <"$out/gate.log" | sed -n \
        -e '1p' \
        -e '/^Accepted exceptions/,/^$/s/^  - \(GHSA-.*\)$/accepted exception: \1/p' \
        -e '/^Not gated (no fix available)/,/^$/s/^  - \(.*\)$/not gated (no fix available): \1/p' \
        -e '/^Not gated (only a semver-major fix)/,/^$/s/^  - \(.*\)$/not gated (only a semver-major fix): \1/p' |
        evidence
      pass npm-audit-high
      ;;
    1)
      tr -d '\r' <"$out/gate.log" | grep '^FAIL ' | evidence
      fail npm-audit-high "rule broken (npm-audit-gate exit 1), $(grep -c '^FAIL ' "$out/gate.log" || true) violation(s): $(tr -d '\r' <"$out/gate.log" | sed -n 's/^FAIL //p' | head -n 1)"
      ;;
    2)
      tr -d '\r' <"$out/gate.log" | tail -n 5 | evidence
      unchecked npm-audit-high "npm-audit-gate exit 2: $(tr -d '\r' <"$out/gate.log" | sed -n 's/^ERROR //p' | one_line)"
      ;;
    *)
      tail -n 5 "$out/gate.log" 2>/dev/null | evidence
      unchecked npm-audit-high "npm-audit-gate did not finish (exit $rc)"
      ;;
  esac
}

check_trivy() {
  local name="$1" image="$2" log="$WORK/$1.log" rc detail

  if ! have docker; then
    unchecked "$name" "docker is not on PATH"
    return
  fi
  if ! docker image inspect "$image" >/dev/null 2>&1; then
    unchecked "$name" "image not found: $image (build it first, or set BACKEND_IMAGE / FRONTEND_IMAGE)"
    return
  fi

  docker run --rm \
    -v "$DOCKER_SOCKET:/var/run/docker.sock" \
    -v "$TRIVY_CACHE_VOLUME:/root/.cache" \
    "$TRIVY_IMAGE" \
    image --scanners vuln --severity HIGH,CRITICAL --ignore-unfixed --exit-code 1 --no-progress --skip-version-check "$image" >"$log" 2>&1
  rc=$?

  if [ "$rc" -eq 0 ]; then
    grep -E 'Detected OS|Detecting vulnerabilities|Number of language-specific files' "$log" |
      cut -f3- | tr '\t' ' ' | evidence
    # Exit 0 only means something when the scanner recognised the image's package database.
    if grep -q 'Detected OS' "$log"; then
      pass "$name"
    else
      tail -n 10 "$log" | evidence
      unchecked "$name" "trivy exit 0, but it detected no OS packages in $image, so nothing was scanned"
    fi
  elif [ "$rc" -eq 1 ] && grep -Eq '^Total: [1-9][0-9]* \(' "$log"; then
    # Only exit 1 together with a report means findings.
    grep -v "$(printf '\tINFO\t')" "$log" | evidence
    fail "$name" "fixable HIGH/CRITICAL findings in $image: $(tr -d '\r' <"$log" | grep -E '^Total: ' | one_line)"
  else
    tail -n 10 "$log" | evidence
    detail="$(grep -E 'FATAL|[Ee]rror' "$log" | head -n 1 | one_line)"
    unchecked "$name" "trivy did not complete (exit $rc)${detail:+: $detail}"
  fi
}

echo "check-deps: root $ROOT"

check_sdk_image_pin
check_nuget
check_npm

if [ "${TRIVY:-0}" = "1" ]; then
  check_trivy trivy-backend "${BACKEND_IMAGE:-ghcr.io/pkgdiff-dev/pkgdiff-backend:local}"
  check_trivy trivy-frontend "${FRONTEND_IMAGE:-ghcr.io/pkgdiff-dev/pkgdiff-frontend:local}"
fi

if [ "$UNCHECKED" -gt 0 ]; then
  echo "check-deps: $PASSED passed, $FAILED failed ($UNCHECKED could not be checked)"
else
  echo "check-deps: $PASSED passed, $FAILED failed"
fi

[ "$FAILED" -eq 0 ]
