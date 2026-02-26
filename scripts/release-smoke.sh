#!/usr/bin/env bash
set -euo pipefail

manifest="${1:-release/release-manifest.json}"
pack_dir="${2:-}"

if [[ ! -f "$manifest" ]]; then
  echo "ERROR: manifest not found: $manifest" >&2
  exit 1
fi
manifest="$(cd "$(dirname "$manifest")" && pwd)/$(basename "$manifest")"

require_tool() {
  command -v "$1" >/dev/null 2>&1 || { echo "ERROR: missing tool: $1" >&2; exit 1; }
}

require_tool jq
require_tool dotnet
require_tool curl

mapfile -t pkg_ids < <(jq -r '.packages[].id' "$manifest")
if [[ "${#pkg_ids[@]}" -eq 0 ]]; then
  echo "ERROR: packages list is empty" >&2
  exit 1
fi

mapfile -t publish_ordered_ids < <(
  jq -r '
    . as $m
    | .publishOrder[] as $id
    | select(any($m.packages[]; .id == $id and .publish == true))
    | $id
  ' "$manifest"
)

tmpdir=$(mktemp -d)
trap 'rm -rf "$tmpdir"' EXIT

if [[ -z "$pack_dir" ]]; then
  pack_dir="$tmpdir/nupkgs"
  mkdir -p "$pack_dir"
  echo "Packing publish=true packages for local smoke feed..."
  for id in "${publish_ordered_ids[@]}"; do
    project=$(jq -r --arg id "$id" '.packages[] | select(.id==$id) | .project' "$manifest")
    dotnet pack "$project" -c Release -o "$pack_dir" >/dev/null
    echo "  packed $id"
  done
else
  if [[ ! -d "$pack_dir" ]]; then
    echo "ERROR: pack directory not found: $pack_dir" >&2
    exit 1
  fi
fi

echo "Running transitive resolution smoke tests..."
ran_any=0
for id in "${publish_ordered_ids[@]}"; do
  top_version=$(jq -r --arg id "$id" '.packages[] | select(.id==$id) | .version' "$manifest")

  # When the exact version is already published on NuGet, package source selection can
  # resolve internal dependencies from remote artifacts with the same ID/version.
  # For deterministic pre-release smoke checks, only enforce this check on unreleased versions.
  lid=$(printf '%s' "$id" | tr '[:upper:]' '[:lower:]')
  if idx_json=$(curl -fsSL "https://api.nuget.org/v3-flatcontainer/${lid}/index.json" 2>/dev/null); then
    if printf '%s' "$idx_json" | jq -r --arg v "$top_version" '.versions[] | select(. == $v)' | grep -qx "$top_version"; then
      echo "  SKIP $id (version $top_version already published on NuGet)"
      continue
    fi
  fi

  ran_any=1
  work="$tmpdir/smoke-$id"
  mkdir -p "$work"
  pkg_cache="$work/packages"
  mkdir -p "$pkg_cache"

  cat > "$work/NuGet.Config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$pack_dir" />
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF

  pushd "$work" >/dev/null
  dotnet new classlib -n Smoke -f net8.0 >/dev/null
  pushd Smoke >/dev/null
  dotnet add package "$id" --version "$top_version" --no-restore >/dev/null
  dotnet restore --configfile ../NuGet.Config --packages "$pkg_cache" --no-cache >/dev/null
  json=$(dotnet list package --include-transitive --format json --no-restore)

  # Verify that any internal package that appears in the graph resolves to manifest version.
  for dep_id in "${pkg_ids[@]}"; do
    expected=$(jq -r --arg dep "$dep_id" '.packages[] | select(.id==$dep) | .version' "$manifest")
    actual=$(echo "$json" | jq -r --arg dep "$dep_id" '
      (.projects[]?.frameworks[]? | ((.topLevelPackages // []) + (.transitivePackages // []))[]? | select(.id==$dep) | .resolvedVersion) // empty
    ' | head -n1)

    if [[ -n "$actual" && "$actual" != "$expected" ]]; then
      echo "ERROR: smoke mismatch for top=$id dep=$dep_id expected=$expected actual=$actual" >&2
      exit 1
    fi
  done

  popd >/dev/null
  popd >/dev/null
  echo "  OK $id"
done

if [[ "$ran_any" -eq 0 ]]; then
  echo "release-smoke: no unreleased package versions to test (all manifest versions already published)."
  echo "release-smoke: PASS"
  exit 0
fi

echo "release-smoke: PASS"
