#!/usr/bin/env bash
# Pull-based deploy for pkgdiff. Runs prebuilt images from GHCR; nothing is built on the server.
#
#   bash deploy/deploy.sh <tag>        deploy the backend and frontend images with this tag
#   bash deploy/deploy.sh --rollback   run the images again that ran before the last deploy
#   bash deploy/deploy.sh --status     show the configured tag and what is running
#
# <tag> is one the publish workflow pushed: sha-<7 hex> (never moves), develop, master or a version.
#
# A deploy pulls both images first and changes nothing when a pull fails. It then tags the images
# that run now as ":rollback", writes the tag to .env, recreates the containers without building
# and waits until the backend and the frontend answer on their loopback ports and, for an image
# that renders on the server, until a page arrives rendered. When they do not, it switches back
# to the ":rollback" images by itself and exits 1.
#
# The database and the volumes are never changed by this script. Before a deploy it takes a dump of
# the database, because a rollback switches images only: it does not undo a migration that the new
# backend applied on start, and that dump is the way back.
#
# Environment:
#   PKGDIFF_GHCR_TOKEN_FILE   file with a read-only GHCR token (default ~/.pkgdiff-ghcr-token);
#                             when it exists, the script logs in to ghcr.io before it pulls
#   PKGDIFF_GHCR_USER         GitHub user the token belongs to (default fleizean)
#   PKGDIFF_HEALTH_TIMEOUT    seconds to wait for a healthy stack (default 180)
#   PKGDIFF_BACKUP_DIR        where the dumps go (default ~/pkgdiff-backups)
#   PKGDIFF_BACKUP_KEEP       how many dumps to keep (default 10)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="${PKGDIFF_ROOT:-$SCRIPT_DIR/..}"
ROOT="$(cd "$ROOT" && pwd)"

IMAGE_PREFIX="${PKGDIFF_IMAGE_PREFIX:-ghcr.io/pkgdiff-dev/pkgdiff}"
SERVICES='backend frontend'
ROLLBACK_TAG='rollback'
ENV_FILE="$ROOT/.env"
SETTINGS_FILE="$ROOT/config/appsettings.json"
STATE_DIR="$ROOT/.deploy"
TOKEN_FILE="${PKGDIFF_GHCR_TOKEN_FILE:-$HOME/.pkgdiff-ghcr-token}"
GHCR_USER="${PKGDIFF_GHCR_USER:-fleizean}"
BACKEND_URL="${PKGDIFF_BACKEND_URL:-http://127.0.0.1:8080/health/ready}"
LEGACY_BACKEND_URL="${PKGDIFF_LEGACY_BACKEND_URL:-http://127.0.0.1:8080/api/package/health}"
FRONTEND_URL="${PKGDIFF_FRONTEND_URL:-http://127.0.0.1:8090/}"
HEALTH_TIMEOUT="${PKGDIFF_HEALTH_TIMEOUT:-180}"
HEALTH_INTERVAL="${PKGDIFF_HEALTH_INTERVAL:-5}"
# What Angular writes on the root element of a page it rendered on the server
RENDERED_MARKER='ng-server-context="ssr"'

BACKUP_DIR="${PKGDIFF_BACKUP_DIR:-$HOME/pkgdiff-backups}"
BACKUP_KEEP="${PKGDIFF_BACKUP_KEEP:-10}"

say() { printf 'deploy: %s\n' "$*"; }

die() {
  printf 'deploy: %s\n' "$*" >&2
  exit 1
}

usage() {
  sed -n '2,8p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
  exit 2
}

image() { printf '%s-%s:%s' "$IMAGE_PREFIX" "$1" "$2"; }

image_id() { docker image inspect "$1" --format '{{.Id}}' 2>/dev/null || true; }

revision() {
  docker image inspect "$1" --format '{{index .Config.Labels "org.opencontainers.image.revision"}}' 2>/dev/null || true
}

# Image ID that the container of a service runs; empty when there is no such container.
running_image() { docker inspect "pkgdiff-$1" --format '{{.Image}}' 2>/dev/null || true; }

# The shell value wins over the one in .env, so compose sees the tag before .env is written.
compose_with_tag() {
  local tag="$1"
  shift
  (cd "$ROOT" && PKGDIFF_IMAGE_TAG="$tag" docker compose "$@")
}

valid_tag() { printf '%s' "$1" | grep -Eq '^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$'; }

configured_tag() {
  [ -f "$ENV_FILE" ] || return 0
  sed -n 's/^PKGDIFF_IMAGE_TAG=//p' "$ENV_FILE" | tail -n 1 | tr -d '\r"'"'"
}

# Replaces (or appends) the one PKGDIFF_IMAGE_TAG line; every other line of .env stays as it is.
write_tag() {
  local tag="$1" tmp
  tmp="$(mktemp "$ENV_FILE.XXXXXX")" || return 1
  if grep -q '^PKGDIFF_IMAGE_TAG=' "$ENV_FILE"; then
    sed "s|^PKGDIFF_IMAGE_TAG=.*|PKGDIFF_IMAGE_TAG=$tag|" "$ENV_FILE" >"$tmp" || { rm -f "$tmp"; return 1; }
  else
    cat "$ENV_FILE" >"$tmp" || { rm -f "$tmp"; return 1; }
    [ -z "$(tail -c 1 "$ENV_FILE")" ] || printf '\n' >>"$tmp"
    printf 'PKGDIFF_IMAGE_TAG=%s\n' "$tag" >>"$tmp"
  fi
  chmod --reference="$ENV_FILE" "$tmp" 2>/dev/null || chmod 600 "$tmp"
  mv "$tmp" "$ENV_FILE"
}

record() {
  mkdir -p "$STATE_DIR"
  printf '%s %s tag=%s backend=%s frontend=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$1" "$2" \
    "$(running_image backend)" "$(running_image frontend)" >>"$STATE_DIR/history.log"
}

preflight() {
  command -v docker >/dev/null 2>&1 || die "docker is not installed"
  docker compose version >/dev/null 2>&1 || die "docker compose v2 is not available"
  command -v curl >/dev/null 2>&1 || die "curl is not installed"
  [ -f "$ROOT/docker-compose.yml" ] || die "$ROOT is not a pkgdiff checkout"
  [ -f "$ENV_FILE" ] || die "$ENV_FILE is missing"
  [ -f "$SETTINGS_FILE" ] ||
    die "$SETTINGS_FILE is missing: the published images carry no settings file (docs/ops/deploy-ghcr.md)"
  mkdir -p "$STATE_DIR"
  if command -v flock >/dev/null 2>&1; then
    exec 9>"$STATE_DIR/lock"
    flock -n 9 || die "another deploy is running"
  fi
}

# The backend image runs as an unprivileged user and compose mounts the settings file into it.
# A file only root can read would stop the new backend from starting, so this is settled before
# anything changes: the file is given to the group of that user (it stays unreadable for others).
settings_readable() {
  local backend user
  backend="$(image backend "$1")"
  user="$(docker image inspect "$backend" --format '{{.Config.User}}' 2>/dev/null || true)"
  case "$user" in
    '' | 0 | root | 0:*) return 0 ;;
  esac
  can_read() { docker run --rm --network none --entrypoint cat -v "$SETTINGS_FILE:/settings.json:ro" "$backend" /settings.json >/dev/null 2>&1; }
  can_read && return 0
  if printf '%s' "$user" | grep -Eq '^[0-9]+$' && chgrp "$user" "$SETTINGS_FILE" 2>/dev/null && chmod 640 "$SETTINGS_FILE" && can_read; then
    say "$SETTINGS_FILE now belongs to group $user (mode 640), so the backend user can read it"
    return 0
  fi
  die "the backend image runs as user $user, which cannot read $SETTINGS_FILE; run: sudo chgrp $user $SETTINGS_FILE && sudo chmod 640 $SETTINGS_FILE (nothing was changed)"
}

login() {
  [ -s "$TOKEN_FILE" ] || return 0
  docker login ghcr.io -u "$GHCR_USER" --password-stdin <"$TOKEN_FILE" >/dev/null ||
    die "login to ghcr.io failed with the token in $TOKEN_FILE; nothing was changed"
}

# Dumps the database next to the earlier dumps and keeps the newest ones. A dump that cannot be
# taken or is not complete stops the deploy before anything was changed. Without a running
# database container (a first install) there is nothing to dump.
backup_database() {
  local tag="$1" file old
  if [ -z "$(running_image db)" ]; then
    say "no database container is running: no backup was taken"
    return 0
  fi
  mkdir -p "$BACKUP_DIR" && chmod 700 "$BACKUP_DIR" || die "cannot create $BACKUP_DIR; nothing was changed"
  file="$BACKUP_DIR/pkgdiffdb-$(date -u +%Y%m%dT%H%M%SZ)-before-$tag.sql.gz"
  if compose_with_tag "$tag" exec -T db pg_dump -U postgres pkgdiffdb | gzip >"$file.partial" &&
    gunzip -c "$file.partial" | tail -n 5 | grep -q 'PostgreSQL database dump complete'; then
    mv "$file.partial" "$file"
    chmod 600 "$file"
    say "database backup: $file"
  else
    rm -f "$file.partial"
    die "the database backup failed; nothing was changed"
  fi
  # The names start with the time of the dump, so their order is the order in time
  ls -1 "$BACKUP_DIR"/pkgdiffdb-*.sql.gz 2>/dev/null | sort -r | tail -n "+$((BACKUP_KEEP + 1))" | while IFS= read -r old; do
    rm -f "$old"
  done
}

http_ok() { curl -fsS -o /dev/null --max-time 10 "$1" 2>/dev/null; }

# Ready means the database and Redis answer too. An image from before /health/ready existed answers
# 404 there and is asked through its old health endpoint, so rolling back to it still works.
backend_ok() {
  local code
  code="$(curl -s -o /dev/null --max-time 10 -w '%{http_code}' "$BACKEND_URL" 2>/dev/null || true)"
  case "$code" in
    200) return 0 ;;
    404) http_ok "$LEGACY_BACKEND_URL" ;;
    *) return 1 ;;
  esac
}

# An image that renders its pages on the server says so in a label; an older one serves the
# client-side application only.
renders_on_server() {
  [ "$(docker image inspect "$1" --format '{{index .Config.Labels "dev.pkgdiff.rendering"}}' 2>/dev/null || true)" = 'server' ]
}

# nginx answers with the client-side page while the render server is down, so a 200 alone says
# nothing about it: the page itself has to say that it was rendered.
page_is_rendered() { curl -fsS --max-time 20 "$FRONTEND_URL" 2>/dev/null | grep -q "$RENDERED_MARKER"; }

# Both ports must stay on the loopback interface: the host nginx is the only public entry.
ports_ok() {
  [ "$(docker port pkgdiff-backend 8080/tcp 2>/dev/null)" = '127.0.0.1:8080' ] &&
    [ "$(docker port pkgdiff-frontend 80/tcp 2>/dev/null)" = '127.0.0.1:8090' ]
}

# Waits until the containers run the images of this tag and answer; prints the reason when they do not.
wait_healthy() {
  local tag="$1" deadline=$((SECONDS + HEALTH_TIMEOUT)) s wanted problem
  while :; do
    problem=''
    for s in $SERVICES; do
      wanted="$(image_id "$(image "$s" "$tag")")"
      if [ -z "$wanted" ] || [ "$(running_image "$s")" != "$wanted" ]; then
        problem="$s does not run $(image "$s" "$tag")"
      fi
    done
    [ -n "$problem" ] || backend_ok || problem="the backend is not ready on $BACKEND_URL"
    [ -n "$problem" ] || http_ok "$FRONTEND_URL" || problem="the frontend does not answer on $FRONTEND_URL"
    [ -n "$problem" ] || ! renders_on_server "$(image frontend "$tag")" || page_is_rendered ||
      problem="the frontend answers, but its pages do not arrive rendered (see: docker compose logs ssr)"
    [ -n "$problem" ] || ports_ok || problem="a published port is not 127.0.0.1:8080 / 127.0.0.1:8090"
    [ -n "$problem" ] || return 0
    if [ "$SECONDS" -ge "$deadline" ]; then
      say "not healthy after ${HEALTH_TIMEOUT}s: $problem"
      return 1
    fi
    sleep "$HEALTH_INTERVAL"
  done
}

switch_to() {
  local tag="$1"
  write_tag "$tag" && compose_with_tag "$tag" up -d --no-build && wait_healthy "$tag"
}

have_rollback_point() {
  local s
  for s in $SERVICES; do
    [ -n "$(image_id "$(image "$s" "$ROLLBACK_TAG")")" ] || return 1
  done
}

# The tag to write when going back: the earlier tag name while it still names the rollback
# images, otherwise the local ":rollback" tag (a moving tag such as develop has moved on).
rollback_tag_name() {
  local previous="$1" s
  if [ -n "$previous" ] && [ "$previous" != "$ROLLBACK_TAG" ] && valid_tag "$previous"; then
    for s in $SERVICES; do
      [ "$(image_id "$(image "$s" "$previous")")" = "$(image_id "$(image "$s" "$ROLLBACK_TAG")")" ] || {
        printf '%s' "$ROLLBACK_TAG"
        return
      }
    done
    printf '%s' "$previous"
    return
  fi
  printf '%s' "$ROLLBACK_TAG"
}

# Remembers what runs now, image and tag name, unless this deploy is a repeat of the last one.
mark_rollback_point() {
  local tag="$1" previous="$2" s running changed=0
  for s in $SERVICES; do
    [ "$(running_image "$s")" = "$(image_id "$(image "$s" "$tag")")" ] || changed=1
  done
  if [ "$changed" -eq 0 ]; then
    say "these images are running already: the rollback point of the earlier deploy is kept"
    return 0
  fi
  for s in $SERVICES; do
    running="$(running_image "$s")"
    if [ -n "$running" ]; then
      docker tag "$running" "$(image "$s" "$ROLLBACK_TAG")"
    else
      say "no $s container is running: it has no rollback point"
    fi
  done
  printf '%s\n' "$previous" >"$STATE_DIR/previous-tag"
}

deploy() {
  local tag="$1" s back
  valid_tag "$tag" || die "'$tag' is not a valid image tag"
  [ "$tag" != "$ROLLBACK_TAG" ] || die "'$ROLLBACK_TAG' is reserved; use --rollback"
  preflight
  login

  for s in $SERVICES; do
    say "pulling $(image "$s" "$tag")"
    docker pull -q "$(image "$s" "$tag")" >/dev/null ||
      die "could not pull $(image "$s" "$tag"); nothing was changed (unknown tag, or no access to ghcr.io)"
  done

  settings_readable "$tag"
  backup_database "$tag"
  mark_rollback_point "$tag" "$(configured_tag)"

  say "starting $tag (revision $(revision "$(image backend "$tag")"))"
  if switch_to "$tag"; then
    record deploy "$tag"
    docker image prune -f >/dev/null 2>&1 || true
    say "deployed $tag"
    return 0
  fi

  record deploy-failed "$tag"
  have_rollback_point || die "deploy of $tag failed and there is no rollback point; the stack needs attention"
  back="$(rollback_tag_name "$(cat "$STATE_DIR/previous-tag" 2>/dev/null || true)")"
  say "switching back to the earlier images ($back)"
  if switch_to "$back"; then
    record auto-rollback "$back"
    die "deploy of $tag failed; the earlier images are running again"
  fi
  die "deploy of $tag failed and the earlier images did not come up either; the stack needs attention"
}

rollback() {
  local s back same=1
  preflight
  have_rollback_point || die "there is no rollback point: no deploy was made with this script yet"
  for s in $SERVICES; do
    [ "$(running_image "$s")" = "$(image_id "$(image "$s" "$ROLLBACK_TAG")")" ] || same=0
  done
  if [ "$same" -eq 1 ]; then
    say "the rollback images are running already; nothing to do"
    return 0
  fi
  back="$(rollback_tag_name "$(cat "$STATE_DIR/previous-tag" 2>/dev/null || true)")"
  say "switching back to the earlier images ($back)"
  if switch_to "$back"; then
    record rollback "$back"
    say "rolled back to $back"
    return 0
  fi
  record rollback-failed "$back"
  die "the earlier images did not come up healthy; the stack needs attention"
}

status() {
  local s tag
  tag="$(configured_tag)"
  say "configured tag: ${tag:-<not set>}"
  for s in $SERVICES; do
    printf '  %-9s running=%s rollback=%s\n' "$s" "$(running_image "$s")" "$(image_id "$(image "$s" "$ROLLBACK_TAG")")"
  done
  [ -z "$tag" ] || compose_with_tag "$tag" ps --format 'table {{.Name}}\t{{.Status}}' || true
  [ ! -f "$STATE_DIR/history.log" ] || tail -n 5 "$STATE_DIR/history.log"
}

[ $# -eq 1 ] || usage
case "$1" in
  --rollback) rollback ;;
  --status) status ;;
  -*) usage ;;
  *) deploy "$1" ;;
esac
