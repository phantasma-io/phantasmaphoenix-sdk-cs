#!/usr/bin/env sh
set -eu

[ -f ".env" ] && export $(grep -v '^#' .env | xargs || true)
: "${NUGET_API_KEY:?Missing NUGET_API_KEY in .env or environment}"

manifest="${1:-release/release-manifest.json}"

echo "Running manifest validation and smoke checks..."
just release-check "$manifest"

echo "Building release artifacts..."
just c
just p

echo "Publishing packages in manifest order..."
just release-publish "$manifest"

echo "Done"
