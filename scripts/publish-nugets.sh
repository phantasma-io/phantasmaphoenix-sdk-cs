#!/usr/bin/env sh
set -eu

[ -f ".env" ] && export $(grep -v '^#' .env | xargs || true)
: "${NUGET_API_KEY:?Missing NUGET_API_KEY in .env or environment}"

just c

just p

for pkg in ./output/nupkgs/*.nupkg; do
  echo "Publishing $pkg..."
  dotnet nuget push "$pkg" \
    --api-key "$NUGET_API_KEY" \
    --source "https://api.nuget.org/v3/index.json" \
    --skip-duplicate \
    --no-symbols
done

echo "Done"