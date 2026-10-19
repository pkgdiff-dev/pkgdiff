#!/usr/bin/env bash
# Nightly database backup for pkgdiff, and the check that a backup can really be restored.
#
#   bash deploy/backup.sh                 take a backup, verify it, delete the nightly backups past retention
#   bash deploy/backup.sh --check         restore the newest nightly backup into a scratch database and report
#   bash deploy/backup.sh --check <file>  the same for one dump (a dump deploy/deploy.sh took works too)
#   bash deploy/backup.sh --list          show the backups that exist
set -euo pipefail
# Dumps hold password hashes and encrypted settings: owner-only from the first byte
umask 077

# cron runs with a short PATH
PATH="$PATH:/usr/local/bin:/usr/bin:/bin"

BACKUP_DIR="${PKGDIFF_BACKUP_DIR:-$HOME/pkgdiff-backups}"
NIGHTLY_DIR="$BACKUP_DIR/nightly"
KEEP_DAYS="${PKGDIFF_NIGHTLY_KEEP_DAYS:-14}"
CONTAINER="${PKGDIFF_DB_CONTAINER:-pkgdiff-db}"
DATABASE='pkgdiffdb'
SCRATCH='pkgdiffdb_restore_check'

say() { printf '%s backup: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"; }

die() {
  printf '%s backup: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*" >&2
  exit 1
}

usage() {
  sed -n '2,7p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
  exit 2
}

sql() { docker exec -i "$CONTAINER" psql -U postgres -v ON_ERROR_STOP=1 -tA "$@"; }

preflight() {
  command -v docker >/dev/null 2>&1 || die "docker is not installed"
  printf '%s' "$KEEP_DAYS" | grep -Eq '^[1-9][0-9]{0,3}$' || die "PKGDIFF_NIGHTLY_KEEP_DAYS must be a number of days, not '$KEEP_DAYS'"
  [ -n "$(docker inspect "$CONTAINER" --format '{{.Id}}' 2>/dev/null || true)" ] ||
    die "the database container $CONTAINER is not running; no backup was taken"
  mkdir -p "$NIGHTLY_DIR" && chmod 700 "$BACKUP_DIR" "$NIGHTLY_DIR" || die "cannot create $NIGHTLY_DIR"
  if command -v flock >/dev/null 2>&1; then
    exec 8>"$BACKUP_DIR/.backup.lock"
    flock -n 8 || die "another backup or restore check is running"
  fi
}

# pg_dump writes this line last; a dump without it was cut off
complete() { gunzip -c "$1" 2>/dev/null | tail -n 5 | grep -q 'PostgreSQL database dump complete'; }

nightly_files() {
  local file
  for file in "$NIGHTLY_DIR"/pkgdiffdb-*.sql.gz; do
    [ ! -f "$file" ] || printf '%s
' "$file"
  done
}

newest_nightly() { nightly_files | sort | tail -n 1; }

# Names sort by dump time, so they compare against a cutoff name; the newest is always kept.
prune() {
  local cutoff newest old
  cutoff="pkgdiffdb-$(date -u -d "-$KEEP_DAYS days" +%Y%m%dT%H%M%SZ).sql.gz"
  newest="$(newest_nightly)"
  nightly_files | while IFS= read -r old; do
    [ "$old" != "$newest" ] || continue
    if [[ "$(basename "$old")" < "$cutoff" ]]; then
      rm -f "$old"
      say "deleted $(basename "$old") (older than $KEEP_DAYS days)"
    fi
  done
}

backup() {
  local file
  preflight
  file="$NIGHTLY_DIR/pkgdiffdb-$(date -u +%Y%m%dT%H%M%SZ).sql.gz"
  if docker exec "$CONTAINER" pg_dump -U postgres "$DATABASE" | gzip >"$file.partial" && complete "$file.partial"; then
    mv "$file.partial" "$file"
    chmod 600 "$file"
    say "wrote $file ($(du -h "$file" | cut -f1))"
  else
    rm -f "$file.partial"
    die "the dump failed or is not complete; the earlier backups were left as they are"
  fi
  prune
}

drop_scratch() { sql -d postgres -c "DROP DATABASE IF EXISTS $SCRATCH" >/dev/null 2>&1 || true; }

figures() {
  sql -d "$1" -c "SELECT (SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE') || ' tables, ' || (SELECT count(*) FROM \"__EFMigrationsHistory\") || ' migrations, ' || (SELECT count(*) FROM \"AspNetUsers\") || ' users, ' || (SELECT count(*) FROM \"CachedPackages\") || ' cached packages'"
}

check() {
  local file="${1:-}" restored
  preflight
  [ -n "$file" ] || file="$(newest_nightly)"
  [ -n "$file" ] || die "there is no nightly backup in $NIGHTLY_DIR to check"
  [ -f "$file" ] || die "$file does not exist"
  complete "$file" || die "$file is not a complete dump"

  say "restoring $file into the scratch database $SCRATCH"
  trap drop_scratch EXIT
  drop_scratch
  sql -d postgres -c "CREATE DATABASE $SCRATCH" >/dev/null || die "could not create the scratch database"
  gunzip -c "$file" | sql -d "$SCRATCH" -q >/dev/null || die "the dump did not load; the live database was not touched"
  restored="$(figures "$SCRATCH")" || die "the restored database has no pkgdiff tables"
  case "$restored" in
    '0 tables'* | *', 0 migrations'*) die "the restored database is empty ($restored)" ;;
  esac
  say "restored: $restored"
  say "live now: $(figures "$DATABASE")"
  drop_scratch
  trap - EXIT
  say "restore check passed; the scratch database was dropped"
}

list() {
  local found=0 file
  for file in "$NIGHTLY_DIR"/pkgdiffdb-*.sql.gz "$BACKUP_DIR"/pkgdiffdb-*.sql.gz; do
    [ -f "$file" ] || continue
    found=1
    printf '%8s  %s\n' "$(du -h "$file" | cut -f1)" "$file"
  done
  [ "$found" -eq 1 ] || say "no backups in $BACKUP_DIR"
}

case "${1:-}" in
  '') [ $# -eq 0 ] || usage; backup ;;
  --check) [ $# -le 2 ] || usage; check "${2:-}" ;;
  --list) [ $# -eq 1 ] || usage; list ;;
  *) usage ;;
esac
