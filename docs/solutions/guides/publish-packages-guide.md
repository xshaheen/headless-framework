# How to publish packages

Package publication is release-only. Publishing a GitHub Release starts the protected workflow; pull requests and ordinary branch builds can build and verify packages but receive no package-write, attestation-write, or OIDC publishing permissions.

## Integrity gates

`eng/expected-packages.txt` is the canonical package-ID manifest. Keep it sorted with one `Headless.*` ID for every packable project directly under `src/`. Before packing, `make verify-package-manifest` evaluates `IsPackable` and `PackageId` through MSBuild and fails if the project inventory differs from the committed manifest.

Packing uses MinVer's computed package version for both the nuspec and the SPDX root package:

```bash
make pack-sbom
make verify-packages
```

The verifier rejects an incomplete or extra package set, corrupt archives, duplicate ID/version pairs, non-`Headless.*` identities, unexpected versions, repository URLs or commits that do not match the release commit, missing or invalid SPDX 2.2 manifests, and SBOM root identities that do not match the nuspec. Symbols remain embedded in assemblies, so `.snupkg` files are not expected.

The release jobs download and verify the same artifact again before credentials are issued or packages are pushed. GitHub build-provenance attestations bind every `.nupkg` digest to the release workflow using short-lived OIDC permissions.

## Immutable publication

Immediately before the first registry push, the workflow performs a read-only NuGet.org flat-container lookup for every expected package ID/version. This is an early collision check, not a lock: another publisher can still win the race. Both GitHub Packages and NuGet.org pushes therefore omit `--skip-duplicate`; any collision fails the release visibly.

Do not retry a partially published release with rebuilt artifacts under the same version. First inspect both registries and the failed workflow to identify which package IDs were accepted. Then either reconcile the incomplete release manually under maintainer control or publish a new version containing the complete package set. Never delete or overwrite an immutable published version to make a rerun appear successful.

## Local verification

The verifier has no publishing side effects. Its negative fixtures create temporary archives only:

```bash
make test-package-verifier
shellcheck scripts/verify-packages.sh tests/scripts/verify-packages-tests.sh
```

`make nuget-publish-preflight` is also read-only, but it queries NuGet.org and is intended for the protected release workflow immediately before publication.
