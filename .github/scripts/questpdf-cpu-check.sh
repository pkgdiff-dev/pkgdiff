#!/usr/bin/env bash
# Checks in containers that QuestPDF renders on a CPU without AVX2, FMA or BMI2 (production).
# Exit status: 0 = PASS, 1 = FAIL, 2 = usage error.
set -euo pipefail

usage() {
  cat >&2 <<'EOF'
Usage: questpdf-cpu-check.sh (--native | --emulate) [--version <questpdf version>]

  --native    render on this machine's real CPU (use this on the production server)
  --emulate   render under QEMU -cpu IvyBridge (no AVX2) with a detector self-test
  --version   QuestPDF version to test; default is the central version in
              src/backend/Directory.Packages.props

The harness images stay behind as pkgdiff-questpdf-check:<version>[-emu];
remove them with: docker rmi <image>
EOF
}

MODE=""
VERSION=""
while [ $# -gt 0 ]; do
  case "$1" in
    --native | --emulate)
      if [ -n "$MODE" ]; then
        usage
        exit 2
      fi
      MODE="${1#--}"
      ;;
    --version)
      if [ $# -lt 2 ]; then
        usage
        exit 2
      fi
      VERSION="$2"
      shift
      ;;
    -h | --help)
      usage
      exit 0
      ;;
    *)
      usage
      exit 2
      ;;
  esac
  shift
done

if [ -z "$MODE" ]; then
  usage
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CONTEXT="$ROOT/.github/scripts/questpdf-cpu-check"

if [ -z "$VERSION" ]; then
  # sed quits itself: `| head -n 1` could SIGPIPE sed and silently abort under pipefail.
  VERSION="$(sed -n '/<PackageVersion Include="QuestPDF" Version="/{s/.*<PackageVersion Include="QuestPDF" Version="\([^"]*\)".*/\1/p;q;}' "$ROOT/src/backend/Directory.Packages.props")"
fi

if ! [[ "$VERSION" =~ ^[0-9A-Za-z][0-9A-Za-z.-]*$ ]]; then
  echo "Could not determine a valid QuestPDF version (got '$VERSION')." >&2
  exit 2
fi

fail() {
  echo "FAIL questpdf $VERSION $MODE: $1"
  exit 1
}

command -v docker >/dev/null 2>&1 || fail "docker is not available"

# Git Bash: keep container paths such as /app intact and hand Docker a Windows path
export MSYS_NO_PATHCONV=1
if command -v cygpath >/dev/null 2>&1; then
  CONTEXT="$(cygpath -m "$CONTEXT")"
fi

if [ "$MODE" = "native" ]; then
  TARGET="native"
  IMAGE="pkgdiff-questpdf-check:$VERSION"
else
  TARGET="emulate"
  IMAGE="pkgdiff-questpdf-check:$VERSION-emu"
fi

echo "Building $IMAGE (QuestPDF $VERSION, target $TARGET) ..."
if ! BUILD_LOG="$(docker build --target "$TARGET" --build-arg "QUESTPDF_VERSION=$VERSION" -t "$IMAGE" "$CONTEXT" 2>&1)"; then
  echo "$BUILD_LOG" | tail -n 40 >&2
  fail "the harness image did not build"
fi

OUT=""
STATUS=0

capture() {
  local seconds="$1"
  shift
  set +e
  OUT="$(docker run --rm --init --network none --memory 3g --entrypoint timeout "$IMAGE" -s KILL "$seconds" "$@" 2>&1)"
  STATUS=$?
  set -e
}

show() {
  echo "--- $1 (exit $STATUS)"
  if [ -n "$OUT" ]; then
    echo "$OUT" | sed 's/^/    /'
  fi
}

has() {
  grep -q -- "$1" <<<"$OUT"
}

DOTNET=/usr/share/dotnet/dotnet
HARNESS=/app/QuestPdfCpuCheck.dll

if [ "$MODE" = "native" ]; then
  capture 30 grep -m 1 "model name" /proc/cpuinfo
  show "CPU of this machine"

  capture 300 "$DOTNET" "$HARNESS"
  show "render on the real CPU"
  if [ "$STATUS" -ne 0 ] || ! has "^PDF_OK "; then
    fail "the render did not produce a PDF on this CPU (exit $STATUS)"
  fi

  echo "PASS questpdf $VERSION native"
  exit 0
fi

# A pass only means something if the emulator kills instructions Ivy Bridge lacks.
for PROBE in avx2 fma bmi2; do
  MARKER="$(echo "$PROBE" | tr '[:lower:]' '[:upper:]')_EXECUTED"

  capture 60 qemu-x86_64 -cpu max /probe/avx2-probe "$PROBE"
  show "self-test: $PROBE under -cpu max"
  if [ "$STATUS" -ne 0 ] || ! has "$MARKER"; then
    fail "detector self-test: the $PROBE probe did not run under -cpu max (exit $STATUS)"
  fi

  capture 60 qemu-x86_64 -cpu IvyBridge /probe/avx2-probe "$PROBE"
  show "self-test: $PROBE under -cpu IvyBridge"
  if has "$MARKER"; then
    fail "detector self-test: the emulator executed $PROBE under -cpu IvyBridge, so it proves nothing"
  fi
  if [ "$STATUS" -ne 132 ] && ! has "uncaught target signal 4"; then
    fail "detector self-test: the $PROBE probe did not die with SIGILL under -cpu IvyBridge (exit $STATUS)"
  fi
done

# The emulator must host .NET at all, or a failure below says nothing.
capture 600 qemu-x86_64 -cpu max "$DOTNET" "$HARNESS"
show "render under -cpu max"
if [ "$STATUS" -ne 0 ] || ! has "^PDF_OK "; then
  fail "the emulator could not host the render under -cpu max (exit $STATUS); this says nothing about Ivy Bridge"
fi

capture 600 qemu-x86_64 -cpu IvyBridge "$DOTNET" "$HARNESS"
show "render under -cpu IvyBridge"
if ! has "AVX2=False"; then
  fail "the emulated CPU did not report AVX2=False, so the run does not represent Ivy Bridge"
fi
if [ "$STATUS" -eq 132 ] || has "uncaught target signal 4"; then
  fail "illegal instruction (SIGILL) while rendering under -cpu IvyBridge"
fi
if [ "$STATUS" -ne 0 ] || ! has "^PDF_OK "; then
  fail "the render did not produce a PDF under -cpu IvyBridge (exit $STATUS)"
fi

echo "PASS questpdf $VERSION emulate"
