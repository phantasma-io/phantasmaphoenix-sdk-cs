# phantasmaphoenix-sdk-cs
Phantasma Phoenix SDK for C#

## Release workflow (manifest-based)

This repository now supports a manifest-driven release flow for internal package dependencies.

### Files

- `release/release-manifest.json`
  - source of truth for:
    - package versions,
    - internal dependency floors (what each package must depend on),
    - publish order.
- `scripts/release-validate.sh`
  - validates manifest structure and checks:
    - csproj versions match manifest versions,
    - `publishOrder` is complete/stable (all package ids exactly once),
    - release intent against NuGet (`publish` flag and versions),
    - produced nuspec internal dependencies for `publish=true` packages match manifest.
- `scripts/release-smoke.sh`
  - packs packages locally and runs clean restore smoke tests for each `publish=true` package.
  - verifies that resolved internal transitive versions match manifest targets.
- `scripts/release-publish.sh`
  - publishes `.nupkg` files from `output/nupkgs` in manifest order.

### Commands

- Validate release metadata and nuspec floors:
  - `just release-validate`
- Run full release checks (validate + transitive smoke):
  - `just release-check`
- Publish in manifest order:
  - `just release-publish`

### Typical release steps

1. Update package `<Version>` values in csproj files.
2. Update `release/release-manifest.json`:
   - new package versions,
   - internal dependency floors,
   - publish order.
3. Run:
   - `just release-check`
4. Build release artifacts:
   - `just p`
5. Publish:
   - `just release-publish`

### Notes

- For dependency-floor-only changes (no API/behavior change), bump patch versions.
- If a package is changed and downstream packages must expose updated dependency floors, republish downstream packages with patch bumps as well.
