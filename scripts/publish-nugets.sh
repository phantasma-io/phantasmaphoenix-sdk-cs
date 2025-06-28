#!/usr/bin/env sh

# Load .env file
if [ -f ".env" ]; then
  export $(grep -v '^#' .env | xargs)
fi

if [ -z "$NUGET_API_KEY" ]; then
  echo "Missing NUGET_API_KEY in .env or environment"
  exit 1
fi

for pkg in ./output/nupkgs/*.nupkg; do
  echo "Publishing $pkg..."
  dotnet nuget push "$pkg" \
    --api-key "$NUGET_API_KEY" \
    --source "https://api.nuget.org/v3/index.json" \
    --skip-duplicate \
    --no-symbols

  if [ $? -ne 0 ]; then
    echo "Failed to publish $pkg"
    exit 1
  fi
done

echo "All packages published successfully."
