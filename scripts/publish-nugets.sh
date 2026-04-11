#!/usr/bin/env sh
set -eu

manifest="${1:-release/release-manifest.json}"
dry_run="${2:-}"

if [ -n "$dry_run" ] && [ "$dry_run" != "--dry-run" ]; then
  echo "ERROR: unsupported option: $dry_run" >&2
  echo "Usage: $0 [manifest] [--dry-run]" >&2
  exit 1
fi

echo "Running manifest validation and smoke checks..."
just release-check "$manifest"

echo "Building release artifacts..."
just c
just p

echo "Publishing packages in manifest order..."
if [ "$dry_run" = "--dry-run" ]; then
  just release-publish "$manifest" "$dry_run"
else
  just release-publish "$manifest"
fi

echo "Done"
