# Contributing

Thanks for contributing to `headless-framework`.

This repository is a modular .NET 10 framework, not a single application. Changes can affect multiple NuGet packages, package READMEs, XML docs, and provider-specific tests. Keep PRs focused and explicit.

## Before You Start

- Small fixes can go straight to a pull request.
- For larger changes, open an issue first so package boundaries and API shape can be agreed before implementation.
- Search existing issues and the relevant `docs/llms/` domain guide before proposing new abstractions or providers. Package READMEs only route to those guides.
- Treat public APIs as NuGet contracts. Prefer clean breaking changes only when the issue or PR explains the trade-off.

## Local Setup

Use the Makefile as the local entry point:

```bash
make bootstrap
make build
make test-unit
```

Useful scoped commands:

```bash
make format-check
make test-project TEST_PROJECT=tests/Headless.Api.Tests.Unit/Headless.Api.Tests.Unit.csproj
make test-class CLASS='*ClockTests'
make test-integration
```

Integration tests use Testcontainers and require Docker. Unit tests should stay isolated and must not depend on external services.

## Repository Layout

- `src/` - NuGet packages
- `tests/` - unit, integration, and harness projects
- `docs/` - generated and hand-written documentation
- `docs/llms/` - domain guidance for agents and consumers
- `Directory.Packages.props` - central package versions
- `headless-framework.slnx` - solution entry point

Most domains follow an abstraction + provider split:

- `Headless.*.Abstractions` exposes contracts
- `Headless.*.<Provider>` contains concrete implementations

When multiple providers share a behavior contract, prefer a shared `*.Tests.Harness` project for conformance tests instead of duplicating provider fixtures.

## Coding Rules

- Use file-scoped namespaces.
- Prefer primary constructors for DI when they fit the type.
- Use `required` and `init` where appropriate.
- Default to `sealed` unless inheritance is intentional.
- Prefer collection expressions and pattern matching over older syntax.
- Keep package versions in `Directory.Packages.props`. Do not add `Version=` to `.csproj` files.
- Match existing naming, option validation, setup extension, and dependency registration patterns.

## Tests And Docs

- Add or update tests with every behavior change.
- Run the narrowest relevant test first. `make verify-affected` builds, tests, and analyzes everything the change affects and writes a summary for the PR body. Widen to `make test-unit` or `make test` when the change warrants it.
- Run `make format-check` before submitting C# changes.
- Update XML docs for public API changes.
- Update the owning `docs/llms/` domain guide when package behavior, options, or setup changes.
- Update a package `README.md` only when its purpose, name, installation command, or canonical links change.
- Update `README.md` or `docs/llms/index.md` only when framework-level guidance or routing changes.

## Pull Requests

- Rebase your branch onto the latest `main` before opening a pull request. Stale branches create avoidable conflicts and waste a review round.
- Open the description with a minimal, clear explanation of the problem, followed by how the change solves it.
- Link the related issue when one exists.
- Call out consumer impact and migration steps when the change is breaking.
- When the change adds, changes, or removes public API, show it in the template's **Public API** section: the declarations as C# (before and after for a changed member), a short consumer snippet for the main scenario, and the contract a signature can't show. A reviewer should understand the API from the PR without reading the diff.
- Keep PRs reviewable. Separate refactors from behavior changes when possible.
- Let CI report routine automated checks. Mention manual verification or known validation gaps only when they help the reviewer.

## Releasing

Package publication is release-only. Publishing a GitHub Release starts the protected workflow; pull requests and ordinary branch builds can build and verify packages but receive no package-write, attestation-write, or OIDC publishing permissions.

### Integrity gates

`eng/expected-packages.txt` is the canonical package-ID manifest. Keep it sorted with one `Headless.*` ID for every packable project directly under `src/`. Before packing, `make verify-package-manifest` evaluates `IsPackable` and `PackageId` through MSBuild and fails if the project inventory differs from the committed manifest.

Packing uses MinVer's computed package version for both the nuspec and the SPDX root package:

```bash
make pack-sbom
make verify-packages
```

The verifier rejects an incomplete or extra package set, corrupt archives, duplicate ID/version pairs, non-`Headless.*` identities, unexpected versions, repository URLs or commits that do not match the release commit, missing or invalid SPDX 2.2 manifests, and SBOM root identities that do not match the nuspec. Symbols remain embedded in assemblies, so `.snupkg` files are not expected.

The release jobs download and verify the same artifact again before credentials are issued or packages are pushed. GitHub build-provenance attestations bind every `.nupkg` digest to the release workflow using short-lived OIDC permissions.

### Immutable publication

Immediately before the first registry push, the workflow performs a read-only NuGet.org flat-container lookup for every expected package ID/version. This is an early collision check, not a lock: another publisher can still win the race. Both GitHub Packages and NuGet.org pushes therefore omit `--skip-duplicate`; any collision fails the release visibly.

Do not retry a partially published release with rebuilt artifacts under the same version. First inspect both registries and the failed workflow to identify which package IDs were accepted. Then either reconcile the incomplete release manually under maintainer control or publish a new version containing the complete package set. Never delete or overwrite an immutable published version to make a rerun appear successful.

### Local verification

The verifier has no publishing side effects. Its negative fixtures create temporary archives only:

```bash
make test-package-verifier
shellcheck scripts/verify-packages.sh tests/scripts/verify-packages-tests.sh
```

`make nuget-publish-preflight` is also read-only, but it queries NuGet.org and is intended for the protected release workflow immediately before publication.
