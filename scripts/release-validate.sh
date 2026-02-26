#!/usr/bin/env bash
set -euo pipefail

manifest="${1:-release/release-manifest.json}"

if [[ ! -f "$manifest" ]]; then
  echo "ERROR: manifest not found: $manifest" >&2
  exit 1
fi

require_tool() {
  command -v "$1" >/dev/null 2>&1 || { echo "ERROR: missing tool: $1" >&2; exit 1; }
}

require_tool jq
require_tool dotnet
require_tool bsdtar
require_tool curl

schema=$(jq -r '.schemaVersion // empty' "$manifest")
if [[ "$schema" != "1" ]]; then
  echo "ERROR: unsupported schemaVersion: ${schema:-<empty>}" >&2
  exit 1
fi

mapfile -t pkg_ids < <(jq -r '.packages[].id' "$manifest")
if [[ "${#pkg_ids[@]}" -eq 0 ]]; then
  echo "ERROR: packages list is empty" >&2
  exit 1
fi

mapfile -t dup_ids < <(printf '%s\n' "${pkg_ids[@]}" | sort | uniq -d)
if [[ "${#dup_ids[@]}" -gt 0 ]]; then
  echo "ERROR: duplicate package ids in manifest:" >&2
  printf '  %s\n' "${dup_ids[@]}" >&2
  exit 1
fi

is_internal_package() {
  local id="$1"
  printf '%s\n' "${pkg_ids[@]}" | grep -qx "$id"
}

extract_internal_deps_from_nuspec_file() {
  local nuspec_file="$1"
  tr -d '\r' < "$nuspec_file" |
    sed -n 's:.*<dependency id="\([^"]*\)" version="\([^"]*\)".*:\1|\2:p' |
    while IFS='|' read -r dep_id dep_ver; do
      if is_internal_package "$dep_id"; then
        printf '%s|%s\n' "$dep_id" "$dep_ver"
      fi
    done | sort -u
}

# Validate projects + versions in csproj + dependency references
for id in "${pkg_ids[@]}"; do
  project=$(jq -r --arg id "$id" '.packages[] | select(.id==$id) | .project' "$manifest")
  version=$(jq -r --arg id "$id" '.packages[] | select(.id==$id) | .version' "$manifest")
  publish=$(jq -r --arg id "$id" '.packages[] | select(.id==$id) | .publish' "$manifest")

  if [[ -z "$project" || "$project" == "null" ]]; then
    echo "ERROR: project is missing for $id" >&2
    exit 1
  fi

  if [[ ! -f "$project" ]]; then
    echo "ERROR: project file not found for $id: $project" >&2
    exit 1
  fi

  csproj_version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$project" | head -n1)
  if [[ -z "$csproj_version" ]]; then
    echo "ERROR: no <Version> found in $project" >&2
    exit 1
  fi

  if [[ "$csproj_version" != "$version" ]]; then
    echo "ERROR: version mismatch for $id: manifest=$version csproj=$csproj_version ($project)" >&2
    exit 1
  fi

  # All internal dependency IDs in manifest must exist in package list and be pinned to manifest versions.
  mapfile -t dep_ids < <(jq -r --arg id "$id" '.packages[] | select(.id==$id) | .dependencies | keys[]?' "$manifest")
  for dep_id in "${dep_ids[@]}"; do
    if ! printf '%s\n' "${pkg_ids[@]}" | grep -qx "$dep_id"; then
      echo "ERROR: $id depends on unknown internal package $dep_id" >&2
      exit 1
    fi

    dep_ver_in_pkg=$(jq -r --arg id "$id" --arg dep "$dep_id" '.packages[] | select(.id==$id) | .dependencies[$dep]' "$manifest")
    dep_target_ver=$(jq -r --arg dep "$dep_id" '.packages[] | select(.id==$dep) | .version' "$manifest")

    if [[ "$publish" == "true" && "$dep_ver_in_pkg" != "$dep_target_ver" ]]; then
      echo "ERROR: dependency pin mismatch in $id for $dep_id: declared=$dep_ver_in_pkg target=$dep_target_ver" >&2
      exit 1
    fi
  done
done

# publishOrder must be a stable complete package order (all packages exactly once).
mapfile -t order_ids_raw < <(jq -r '.publishOrder[]' "$manifest")
if [[ "${#order_ids_raw[@]}" -eq 0 ]]; then
  echo "ERROR: publishOrder is empty" >&2
  exit 1
fi

mapfile -t order_dups < <(printf '%s\n' "${order_ids_raw[@]}" | sort | uniq -d)
if [[ "${#order_dups[@]}" -gt 0 ]]; then
  echo "ERROR: duplicate package ids in publishOrder:" >&2
  printf '  %s\n' "${order_dups[@]}" >&2
  exit 1
fi

mapfile -t order_ids < <(printf '%s\n' "${order_ids_raw[@]}" | sort)
mapfile -t all_ids < <(printf '%s\n' "${pkg_ids[@]}" | sort)
if ! diff -u <(printf '%s\n' "${all_ids[@]}") <(printf '%s\n' "${order_ids[@]}") >/dev/null; then
  echo "ERROR: publishOrder must contain every package id exactly once" >&2
  diff -u <(printf '%s\n' "${all_ids[@]}") <(printf '%s\n' "${order_ids[@]}") || true
  exit 1
fi

tmpdir=$(mktemp -d)
trap 'rm -rf "$tmpdir"' EXIT
pack_dir="$tmpdir/nupkgs"
mkdir -p "$pack_dir"

echo "Packing packages to validate nuspecs and release intent..."
for id in "${order_ids_raw[@]}"; do
  project=$(jq -r --arg id "$id" '.packages[] | select(.id==$id) | .project' "$manifest")
  dotnet pack "$project" -c Release -o "$pack_dir" >/dev/null
  echo "  packed $id"
done

echo "Validating package release intent against NuGet..."
for id in "${pkg_ids[@]}"; do
  publish=$(jq -r --arg id "$id" '.packages[] | select(.id==$id) | .publish' "$manifest")
  version=$(jq -r --arg id "$id" '.packages[] | select(.id==$id) | .version' "$manifest")
  nupkg="$pack_dir/${id}.${version}.nupkg"
  if [[ ! -f "$nupkg" ]]; then
    echo "ERROR: expected nupkg not found: $nupkg" >&2
    exit 1
  fi

  work="$tmpdir/unzip-$id"
  mkdir -p "$work"
  bsdtar -xf "$nupkg" -C "$work"
  nuspec=$(find "$work" -maxdepth 1 -name '*.nuspec' | head -n1)
  if [[ -z "$nuspec" ]]; then
    echo "ERROR: nuspec not found in $nupkg" >&2
    exit 1
  fi

  mapfile -t local_deps < <(extract_internal_deps_from_nuspec_file "$nuspec")
  mapfile -t manifest_deps < <(jq -r --arg id "$id" '.packages[] | select(.id==$id) | .dependencies | to_entries[]? | "\(.key)|\(.value)"' "$manifest" | sort -u)

  if [[ "$publish" == "true" ]]; then
    if ! diff -u <(printf '%s\n' "${manifest_deps[@]}") <(printf '%s\n' "${local_deps[@]}") >/dev/null; then
      echo "ERROR: internal nuspec dependency mismatch for publish=true package $id" >&2
      echo "Expected (manifest):" >&2
      printf '  %s\n' "${manifest_deps[@]}" >&2
      echo "Actual (local nupkg):" >&2
      printf '  %s\n' "${local_deps[@]}" >&2
      exit 1
    fi
  fi

  lid=$(printf '%s' "$id" | tr '[:upper:]' '[:lower:]')
  idx_url="https://api.nuget.org/v3-flatcontainer/${lid}/index.json"
  latest_published=""
  version_already_published="false"
  idx_file="$tmpdir/index-${id}.json"
  idx_code=$(curl -sS -L --retry 3 --retry-delay 1 --max-time 30 -o "$idx_file" -w "%{http_code}" "$idx_url" || true)

  if [[ "$idx_code" == "200" ]]; then
    idx_json=$(cat "$idx_file")
    latest_published=$(printf '%s' "$idx_json" | jq -r '.versions[-1] // empty')
    if printf '%s' "$idx_json" | jq -r --arg v "$version" '.versions[] | select(. == $v)' | grep -qx "$version"; then
      version_already_published="true"
    fi
  elif [[ "$idx_code" != "404" ]]; then
    echo "ERROR: failed to query NuGet index for $id (HTTP $idx_code)" >&2
    echo "       URL: $idx_url" >&2
    echo "       Cannot validate publish intent without a reliable NuGet response." >&2
    exit 1
  fi

  if [[ "$publish" == "true" && "$version_already_published" == "true" ]]; then
    echo "ERROR: $id has publish=true but version $version already exists on NuGet" >&2
    exit 1
  fi

  if [[ "$publish" != "true" && "$version_already_published" != "true" ]]; then
    echo "ERROR: $id has publish=false but version $version is not published on NuGet" >&2
    echo "       This usually means publish flag was forgotten." >&2
    exit 1
  fi

  if [[ -n "$latest_published" ]]; then
    latest_published_lower=$(printf '%s' "$latest_published" | tr '[:upper:]' '[:lower:]')
    published_nuspec="$tmpdir/published-${id}.nuspec"
    published_nuspec_url="https://api.nuget.org/v3-flatcontainer/${lid}/${latest_published_lower}/${lid}.nuspec"
    published_nuspec_code=$(curl -sS -L --retry 3 --retry-delay 1 --max-time 30 -o "$published_nuspec" -w "%{http_code}" "$published_nuspec_url" || true)

    if [[ "$published_nuspec_code" != "200" ]]; then
      echo "ERROR: failed to fetch published nuspec for $id $latest_published (HTTP $published_nuspec_code)" >&2
      echo "       URL: $published_nuspec_url" >&2
      exit 1
    fi

    mapfile -t published_deps < <(extract_internal_deps_from_nuspec_file "$published_nuspec")

    if ! diff -u <(printf '%s\n' "${published_deps[@]}") <(printf '%s\n' "${local_deps[@]}") >/dev/null; then
      if [[ "$publish" != "true" ]]; then
        echo "ERROR: internal dependency floors changed for $id compared to published $latest_published, but publish=false" >&2
        echo "Published deps:" >&2
        printf '  %s\n' "${published_deps[@]}" >&2
        echo "Current deps:" >&2
        printf '  %s\n' "${local_deps[@]}" >&2
        echo "       Bump $id version and set publish=true." >&2
        exit 1
      fi
    fi
  fi

  echo "  OK $id"
done

echo "release-validate: PASS"
