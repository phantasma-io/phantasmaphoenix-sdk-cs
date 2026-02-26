#!/usr/bin/env bash
set -euo pipefail

manifest="${1:-release/release-manifest.json}"
pack_dir="${2:-./output/nupkgs}"
dry_run="${3:-}"

if [[ ! -f "$manifest" ]]; then
  echo "ERROR: manifest not found: $manifest" >&2
  exit 1
fi

if [[ ! -d "$pack_dir" ]]; then
  echo "ERROR: package directory not found: $pack_dir" >&2
  exit 1
fi

if [[ -f ".env" ]]; then
  # shellcheck disable=SC2046
  export $(grep -v '^#' .env | xargs || true)
fi

: "${NUGET_API_KEY:?Missing NUGET_API_KEY in .env or environment}"

echo "Publishing packages in manifest order from: $pack_dir"
if [[ "$dry_run" == "--dry-run" ]]; then
  echo "Dry-run mode enabled."
fi

while IFS= read -r id; do
  publish=$(jq -r --arg id "$id" '.packages[] | select(.id==$id) | .publish' "$manifest")
  if [[ "$publish" != "true" ]]; then
    continue
  fi

  version=$(jq -r --arg id "$id" '.packages[] | select(.id==$id) | .version' "$manifest")
  pkg="$pack_dir/${id}.${version}.nupkg"
  if [[ ! -f "$pkg" ]]; then
    echo "ERROR: package not found for publish: $pkg" >&2
    exit 1
  fi

  echo "Publishing $pkg"
  if [[ "$dry_run" != "--dry-run" ]]; then
    dotnet nuget push "$pkg" \
      --api-key "$NUGET_API_KEY" \
      --source "https://api.nuget.org/v3/index.json" \
      --skip-duplicate \
      --no-symbols
  fi
done < <(jq -r '.publishOrder[]' "$manifest")

echo "release-publish: DONE"
