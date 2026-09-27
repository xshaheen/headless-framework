---
title: "Lock files of projects outside the slnx go stale when a shared project's references change"
date: 2026-09-25
category: build-errors
module: headless-framework
problem_type: build_error
component: build_tooling
severity: medium
symptoms:
  - "CI job `.NET · Build & unit tests (UTC)` fails in tests/Headless.Messaging.PackageReference.Tests.Unit, test PackageReferenceFenceTests.should_keep_bus_and_queue_abstractions_compile_time_isolated, for both probes (BusOnlyCannotResolveQueue, QueueOnlyCannotResolveBus)"
  - "Probe build output contains error NU1004: the packages lock file is inconsistent with the project dependencies so restore can't be run in locked mode"
  - "The same test passes locally, including with CI=true set, unless the probes' obj/ directories are deleted first"
  - "Failure appears only after Headless.UnitOfWork.Abstractions gains a new ProjectReference and make restore regenerates the solution-wide lock files but not the two probe projects, which headless-framework.slnx does not list"
root_cause: missing_workflow_step
resolution_type: dependency_update
related_components:
  - "Headless.UnitOfWork.Abstractions"
  - "Headless.Checks"
  - "Headless.Messaging.PackageReference.Tests.Unit"
  - "headless-framework.slnx"
tags:
  - nuget
  - packages-lock-json
  - restore
  - nu1004
  - ci
  - slnx
  - msbuild-sdk
  - probe-projects
---
# Lock files of projects outside the slnx go stale when a shared project's references change

## Problem
PR #973 added a `ProjectReference` from `Headless.UnitOfWork.Abstractions` to `Headless.Checks` (`src/Headless.UnitOfWork.Abstractions/Headless.UnitOfWork.Abstractions.csproj:9`). `make restore` restores only `headless-framework.slnx` (`Makefile:89-90`), so it refreshed about 170 lock files but skipped the two package-reference probe projects, which the slnx does not list. CI restores in locked mode, so the stale probe lock files failed the unit-test job. Local runs stayed green.

## Symptoms
- CI job `.NET · Build & unit tests (UTC)` (`.github/workflows/ci.yml:221`) fails in `tests/Headless.Messaging.PackageReference.Tests.Unit`, test `PackageReferenceFenceTests.should_keep_bus_and_queue_abstractions_compile_time_isolated`, for both probes (`BusOnlyCannotResolveQueue`, `QueueOnlyCannotResolveBus`).
- The probe build output contains (quoted from the failing CI job log):
  `error NU1004: The project references headless.unitofwork.abstractions whose dependencies has changed. The packages lock file is inconsistent with the project dependencies so restore can't be run in locked mode.`
- The same test passes locally.

## What Didn't Work
- **Running the fence test locally.** It passed because the probe build silently regenerated the probe lock files. Outside CI, NuGet updates an existing `packages.lock.json` instead of rejecting it, so the local run fixed the drift as a side effect and still reported success.
- **Adding `CI=true`.** The test still passed, because the probes' `obj/project.assets.json` was already current from the earlier local run, so the locked-mode check found nothing to reject.
- **What reproduced it.** Stash the regenerated probe lock files, delete both probes' `obj/` directories, then run:
  `CI=true make test-project TEST_PROJECT=tests/Headless.Messaging.PackageReference.Tests.Unit/Headless.Messaging.PackageReference.Tests.Unit.csproj`

## Solution
Regenerate both probe lock files and commit them (merged with PR #973). The diff only adds the new transitive edge under the `headless.unitofwork.abstractions` project entry in each file:

```diff
-        "type": "Project"
+        "type": "Project",
+        "dependencies": {
+          "Headless.Checks": "[1.0.0, )"
+        }
```

Files: `tests/Headless.Messaging.PackageReference.Tests.Unit/Probes/BusOnlyCannotResolveQueue/packages.lock.json` and `.../Probes/QueueOnlyCannotResolveBus/packages.lock.json`.

## Why This Works
- The fence test does not restore through the solution. It runs a child process, `dotnet build <probe>.csproj -v:q -nologo /clp:ErrorsOnly -p:NuGetAudit=false` (`tests/Headless.Messaging.PackageReference.Tests.Unit/PackageReferenceFenceTests.cs:55-58`), so each probe restores independently against its own `packages.lock.json`, and a solution-level restore never touches those files.
- The probes deliberately do not inherit the repo's `Directory.Build.props`. `tests/Headless.Messaging.PackageReference.Tests.Unit/Probes/Directory.Build.props` is an empty isolation stub, so the repo's `RestoreLockedMode` line (`Directory.Build.props:6-7`, conditioned on `CI` or `ContinuousIntegrationBuild`) does not apply to them. Locked mode comes from the `Headless.NET.Sdk` instead. `SupportDetectContinuousIntegration.props:8-27,40-46` (SDK 0.3.0, pinned in `global.json`) treats `CI=true`, `GITHUB_ACTIONS=true` and similar variables as a known CI environment and turns on `RestoreLockedMode` whenever a `packages.lock.json` exists. On a GitHub runner the probe restore therefore rejects any lock file whose recorded project graph differs from the actual one.
- Each probe references a messaging abstraction that transitively references `Headless.UnitOfWork.Abstractions` (for example, `tests/Headless.Messaging.PackageReference.Tests.Unit/Probes/BusOnlyCannotResolveQueue/BusOnlyCannotResolveQueue.csproj:6`). Its lock file records that project's dependency list, so a new edge anywhere in that closure invalidates it. After regeneration, the recorded graph matches again.
- The SDK does not enable locked restore for coding agents: `SupportDetectLlmContext.props:5-7` states that agent detection does not set `ContinuousIntegrationBuild`. An agent-driven local run behaves like a developer run and rewrites lock files silently.

## Prevention
- **After changing a `ProjectReference` or `PackageReference` of a widely referenced project, also restore every lock-file project that the slnx does not list.** `make restore` does not cover them. List them with the command below (run from the repo root; the slnx mixes `/` and `\` separators, so normalize before matching). Today it prints exactly the two probes:
  ```sh
  git ls-files '*packages.lock.json' | while IFS= read -r lock; do
    for proj in "${lock%/*}"/*.csproj; do
      tr '\\' / < headless-framework.slnx | grep -qF "\"$proj\"" || printf '%s\n' "$proj"
    done
  done
  ```
  Restore each listed project with `dotnet restore <proj> --force-evaluate`, then commit the changed lock files together with the reference change. The restore half of this step is not yet verified on its own. Only the listing command was run.
- **Reproduce CI's locked mode before pushing a reference change.** A green local run proves nothing, because a normal restore rewrites stale lock files. Delete the affected probes' `obj/` directories, then run the test with `CI=true`:
  ```sh
  rm -rf tests/Headless.Messaging.PackageReference.Tests.Unit/Probes/*/obj
  CI=true make test-project TEST_PROJECT=tests/Headless.Messaging.PackageReference.Tests.Unit/Headless.Messaging.PackageReference.Tests.Unit.csproj
  ```
  Also check `git status` after any local test run: a `packages.lock.json` it modified is the same drift that CI would reject.
- **The same drift can land on `main` through two independently green PRs.** #973 added the `Headless.Checks` dependency to `Headless.UnitOfWork.Abstractions`. #971 was merged after it and added `tests/Headless.Features.Tests.Harness` and `tests/Headless.Settings.Tests.Harness`, whose lock files were generated before that dependency existed. Each PR passed CI on its own, and `main` then failed with the same NU1004 in `Lint · .NET analyzers` and `.NET · Build & unit tests (UTC)`. These projects are inside the slnx, so `make restore` does refresh them, but only on a branch rebased onto the merged result. After rebasing a branch that adds new lock-file projects, or after the base gains a new `ProjectReference`, run `make restore` and commit any lock file it changes.
