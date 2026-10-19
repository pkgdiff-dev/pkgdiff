#!/usr/bin/env bash
# Everything the deploy key of GitHub Actions may do on the server:
#
#   remote.sh deploy <tag> [<commit>]   bring the checkout to <commit> (without one: to the newest
#                                       commit of its branch) and deploy the images with <tag>
#   remote.sh rollback                  run the images again that ran before the last deploy
#   remote.sh status                    show the configured tag and what is running
#
# It is meant to be the forced command of that key in ~/.ssh/authorized_keys (docs/ops/deploy-ghcr.md):
#
#   command="/opt/pkgdiff/src/deploy/remote.sh",no-pty,no-port-forwarding,no-agent-forwarding,no-X11-forwarding ssh-ed25519 AAAA... pkgdiff-actions-deploy
#
# sshd then runs this script whatever the client asked for and hands over what it asked for in
# SSH_ORIGINAL_COMMAND. That text is only split into words and checked; nothing in it is evaluated.
set -euo pipefail

usage() {
  sed -n '2,8p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//' >&2
  exit 2
}

# The whole script is inside functions, so bash has read all of it before git replaces the file
main() {
  local root action tag='' commit='' argument
  local -a words=()
  root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

  if [ -n "${SSH_ORIGINAL_COMMAND:-}" ]; then
    read -r -a words <<<"$SSH_ORIGINAL_COMMAND"
  else
    words=("$@")
  fi

  # The client may put the path of this script in front of the action
  while [ "${#words[@]}" -gt 0 ]; do
    case "${words[0]}" in
      deploy | rollback | status) break ;;
      *) words=("${words[@]:1}") ;;
    esac
  done
  [ "${#words[@]}" -gt 0 ] || usage
  action="${words[0]}"

  case "$action" in
    deploy)
      [ "${#words[@]}" -eq 2 ] || [ "${#words[@]}" -eq 3 ] || usage
      tag="${words[1]}"
      [ "${#words[@]}" -lt 3 ] || commit="${words[2]}"
      printf '%s' "$tag" | grep -Eq '^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$' || usage
      [ -z "$commit" ] || printf '%s' "$commit" | grep -Eq '^[0-9a-f]{40}$' || usage
      argument="$tag"
      ;;
    rollback)
      [ "${#words[@]}" -eq 1 ] || usage
      argument='--rollback'
      ;;
    status)
      [ "${#words[@]}" -eq 1 ] || usage
      argument='--status'
      ;;
  esac

  cd "$root"
  if [ "$action" = deploy ]; then
    # The compose file and the deploy script belong to the commit the images were built from.
    # A fast-forward only: a checkout with local commits or changes in the way stops here.
    if [ -n "$commit" ]; then
      git fetch --quiet origin
      # Only what was merged: a commit that is on no deployed branch is refused, whoever asks
      if ! git merge-base --is-ancestor "$commit" origin/develop 2>/dev/null \
        && ! git merge-base --is-ancestor "$commit" origin/master 2>/dev/null; then
        echo "deploy: $commit is not on develop or master; nothing was done" >&2
        exit 1
      fi
      git merge --ff-only "$commit"
    else
      git pull --ff-only
    fi
  fi

  exec bash deploy/deploy.sh "$argument"
}

main "$@"
