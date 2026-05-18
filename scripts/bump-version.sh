#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

kind="${1:-}"
if [[ "$kind" != "patch" && "$kind" != "minor" && "$kind" != "major" ]]; then
  echo "usage: $0 {patch|minor|major}" >&2
  exit 2
fi

current=$(cat VERSION)
IFS='.' read -r major minor patch <<< "$current"
case "$kind" in
  patch) patch=$((patch + 1)) ;;
  minor) minor=$((minor + 1)); patch=0 ;;
  major) major=$((major + 1)); minor=0; patch=0 ;;
esac
next="${major}.${minor}.${patch}"
echo "$next" > VERSION
echo "VERSION: $current -> $next"

spec="packaging/comfyui-server-manager.spec"
date_str=$(date "+%a %b %d %Y")
entry="* ${date_str} Philippe <draekz@gmail.com> - ${next}-1\n- Version bump (${kind})."
awk -v e="$entry" '/^%changelog$/ { print; print e; next } { print }' "$spec" > "$spec.tmp" && mv "$spec.tmp" "$spec"
echo "spec: added changelog entry for ${next}"
