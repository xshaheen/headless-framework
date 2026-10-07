# Repository Guidelines

## What this repository is

**headless-framework** is a modular .NET 10 framework for APIs and backend services. It ships 150+ NuGet packages grouped by domain (API, Blobs, Caching, Messaging, ORM, and more). It is unopinionated and has no lock-in.

Two facts decide most judgment calls:

- **It is a framework, not an application.** Downstream consumers and future providers call abstractions, extension points, and helpers that nothing in this repository calls. Keep a public member that has no local caller.
- **It is greenfield.** Prefer the simpler, cleaner API, even when it breaks consumers. A breaking change that improves correctness or performance beats a compatibility shim, unless the request says otherwise. The same applies to the database: no deployed schema or data exists. Change a schema in place and reset or rewrite its migrations. Write no migration, backfill, or compatibility shim for a schema nobody runs.

Coverage targets:

| Metric | Target | Floor or goal |
| --- | --- | --- |
| Line | ≥85% | floor 80% |
| Branch | ≥80% | floor 70% |
| Mutation score | ≥70% | goal 85% |

## Architecture

Each feature ships as one abstraction package plus one package per provider. `Headless.<Feature>.Abstractions` holds the interfaces, and `Headless.<Feature>.<Provider>` implements them. For example, `Headless.Caching.Redis` implements `Headless.Caching.Abstractions`.

Registration follows the same split. The feature's root package (`Headless.<Feature>`) owns `AddHeadless{Feature}(Action<Headless{Feature}SetupBuilder>)` and the provider gates. Each provider package adds `Use{Provider}` members to the builder, in a single static `Setup{Provider}` class in `Setup.cs` at the package root, with the same overload trio (`IConfiguration`, `Action<TOptions>`, `Action<TOptions, IServiceProvider>`). Options validate through FluentValidation via the `Headless.Hosting` `AddOptions<TOptions, TValidator>()` and `Configure<TOptions, TValidator>(...)` extensions, with the validator in the options file. Before you add a registration entry point, a provider package, or an options class, match the closest existing provider package.

Transactions are explicit. `IUnitOfWorkFactory` is a singleton with no `Current` property. The `IUnitOfWork` handle is the unit's only identity, and the receiver always decides whether to enlist. Before you change how Messaging, Jobs, or a storage provider joins a unit, read the design rationale in [docs/llms/unit-of-work.md](docs/llms/unit-of-work.md).

Relational storage features create their tables through one `SchemaRunner`, which trusts `headless_schema_history`. A table dropped by hand, or by a test reset on a reused container, is not recreated until its feature's history rows go too. Never edit a released step: add a new one. Before a release no database runs the step, so change it in place. Put every configurable object name in `SchemaContribution.FeatureId`. Before you add or change a storage feature's DDL, match an existing feature's `SchemaContribution`.

## Build and test

Use the `make` targets instead of raw `dotnet`. They pin configuration, results directories, and parallelism. `make help` lists every target, `make doctor` checks the prerequisites with a fix for each, and `make check` runs the CI gates over the affected scope. The rules below cover what `make help` does not say.

### Work in the affected scope

The solution has ~430 projects. A full build takes a long time, and the integration suites take minutes per module. Build and test only the projects your change affects:

1. The projects you changed.
2. The projects whose behavior depends on the code you changed. For example, a change to `Headless.Caching.Abstractions` affects every `Headless.Caching.*` provider.
3. The unit and integration test projects of the projects in 1 and 2.

`make affected` prints that set, computed from the `ProjectReference` graph (`scripts/project-graph.py`) against `@{upstream}` (else `origin/main`), uncommitted and untracked files included. A change to a build-wide file (`global.json`, `Directory.*.props`, `.editorconfig`, `eng/DashboardSpa.targets`) selects every project below it. It adds only the direct dependents of a changed project. A pull request's CI run adds every transitive dependent (`scripts/project-graph.py affected --transitive`), so CI can build and test a dependent of a dependent that the local run skipped.

Use the affected and project-scoped targets: `test-class` with `TEST_PROJECT`, `test-project`, `build-project`, `build-affected`, `test-affected`, `test-failed`, `verify-affected`, `test-affected-integration`, and `quality-analyzers-affected`. Run a solution-wide target (`build`, `rebuild`, `test`, `test-unit`, `test-integration`, `coverage`, `quality-analyzers`) only when the user asks for it, or when the change touches shared build files such as `Directory.Build.props`, `Directory.Packages.props`, or `eng/`.

### Scope a run

- **Start with `make test-class` and `TEST_PROJECT`.** It runs the matching tests inside one project in seconds. Without `TEST_PROJECT`, `test-class`, `test-method`, `test-namespace`, `test-trait`, and `test-query` build all ~430 projects and pass the whole solution to the runner, integration modules included. Add `REPEAT=5` to chase a flaky test; it reports which runs failed:

  ```sh
  make test-class CLASS='*CultureHelperTests' TEST_PROJECT=tests/Headless.Extensions.Tests.Unit/Headless.Extensions.Tests.Unit.csproj
  ```

  To scope a build, use `make build-project PROJECT=…` or `make build-affected`.
- **Run `make test-affected` before the gate, not in the loop.** It builds the affected set in one solution-filter build, then runs every affected `*.Tests.Unit` project to completion: one failing project does not hide the rest. If the build fails, it skips the tests rather than run them against stale binaries. One module can dominate it: `Headless.Jobs.Composition.Tests.Unit` alone takes about four minutes.
- **Run `make test-affected-integration` when you change provider behavior.** It runs the affected `*.Tests.Integration` projects and needs Docker. CI does not run them, so a local run is the only check.
- **After a narrow fix, run `make test-failed`, not the whole gate.** It reads the latest `verify-affected` or `test-affected` proof bundle and re-runs only the test projects whose modules failed. It names any other failed stage with the target that repeats it. Finish with one full `make verify-affected`.
- **Check a dashboard SPA with `make dashboard-jobs-test` or `make dashboard-messaging-test`.** They run the SPA's `lint:check`, `type-check`, `test:unit`, and `build-only` scripts through `npm run`, after `npm ci` when `node_modules` is missing or older than the lockfile. Do not call `npx`: a wrapper on `PATH` can hide the tool's output. CI's dashboard jobs run the same targets.

### Restore and build

- **`make test-project` and `make build` assert restore and do not repeat it.** They run with `--no-restore`. If `obj/project.assets.json` is missing, or older than `Directory.Packages.props`, the project file, or the project lock file, they fail and print the exact `make restore-project` or `make restore` command to run. Run `make restore` after a package change; `rebuild` still restores. With warm outputs and no source change, `make test-project-fast` (`--no-build --no-restore`) costs less. A fresh clone or worktree needs `make bootstrap`.
- **`build-fast` and `build-project-fast` only type-check.** They drop analyzers and MinVer. Alternating them with a normal build recompiles the graph, so use one kind of build for the whole session. A later incremental `make build` also skips up-to-date projects without running their analyzers. Only `rebuild` and `quality-analyzers` build with `--no-incremental`. `verify-affected` and `quality-analyzers-affected` re-check analyzers on the changed projects through `dotnet format analyzers`, which runs every analyzer at every severity.
- **The dashboards need Node 22+ on `PATH`.** A build of `Headless.Jobs.Dashboard` or `Headless.Messaging.Dashboard` runs `npm ci` and a Vite build (`eng/DashboardSpa.targets`), then embeds the generated `wwwroot/dist`. That folder is not committed. `make dashboards` rebuilds the SPAs. If `wwwroot/dist` already exists, pass `/p:BuildDashboardSpa=false` to skip the npm build. `make dashboards-test` runs each SPA's CI gates (lint, type-check, Vitest, and the Vite build), and `make verify-affected` runs them for a SPA your change touches. To drive the dashboards in a browser, `make up` starts the dashboard sandbox (`sandboxes/README.md`).

### Gates

- **A green test run is not a clean build.** The test SDK's `DisableAnalyzersWhenRunningTests` target turns analyzers off for the test project itself during `dotnet test`, so analyzer findings in test code pass a test run. Referenced `src` projects still build with analyzers. `make verify-affected` and `make quality-analyzers-affected` re-check both.
- **Your builds already treat warnings as errors.** The SDK does this whenever it detects an agent CLI: `SupportDetectLlmContext.props` reads `CLAUDECODE`, `CODEX_CLI`, and similar variables. To see the human posture, pass `HeadlessIsLlmContext=false`.
- **The hooks gate formatting, not code.** Pre-commit formats staged C# files and lists only the files it changed; silence means nothing changed. Pre-push runs a CSharpier check on changed files. Neither compiles, runs analyzers, or runs tests. To compile the solution by hand, run `make hook-build`.
- **Finish with `make verify-affected` and put its proof in the PR.** When all work is done, it checks the formatting of the changed C# files (the check the pre-commit hook would otherwise apply after the proof), builds the affected set, runs the affected unit tests with coverage, runs the analyzers on the changed projects, and runs the dashboard check of a SPA whose `wwwroot/` changed. It writes `artifacts/proof/<run>/summary.md` (paste it into the PR description) and `summary.json`: stage results, failed tests with their first message line, compiler and analyzer findings grouped by rule, and line and branch coverage of the changed assemblies. Coverage is reported, not gated. The analyzer stage fails on info-level suggestions as well as warnings and errors. Resolve every finding:
  - Fix each valid finding.
  - Suppress each invalid finding with an inline reason, as the analyzer-suppression convention in [Conventions](#conventions) describes.

  Then run `make verify-affected` again. `make quality-analyzers-affected` runs the analyzer stage alone and checks only the projects you changed. `make quality-analyzers` is the whole-solution version. It is stricter than CI: CI's `Lint · .NET analyzers` job runs only `make rebuild`, so it fails on warnings and errors but not on info-level suggestions. To narrow a noisy run, set `QUALITY_SEVERITY=warn` or `QUALITY_DIAGNOSTICS=MA0154`. To check one dependent project, use `make quality-analyzers-project PROJECT=<csproj>`.
- **Run `make quality-references` after you add or change a reference.** It builds with ReferenceTrimmer, under `artifacts/reftrim` and with its own lock files, and lists every `PackageReference` or `ProjectReference` the compiled code does not use. It exits 3 on findings. Remove each finding, or mark a deliberate reference `TreatAsUsed="true"` with the reason in a comment. Turn the tool off for a project that compiles code at run time (`<EnableReferenceTrimmer>false</EnableReferenceTrimmer>`). Pass `PROJECT=<csproj>` to check one project. CI does not run it.
- **`make quality-fix` refuses to run without a filter.** Pass one rule at a time in `QUALITY_DIAGNOSTICS`, and run `make rebuild` between rules, because four fixers emit code that does not compile.

### CI

- **CI compiles twice, then runs the unit suite.** The `build` job uses `-p:RunAnalyzers=false`, and the `analyzers` job runs the full analyzer set. Both use `--no-incremental` and treat warnings as errors.
- **A pull request builds and tests only what it can affect.** `make ci-scope` selects the changed projects, every transitive dependent, and their test projects, and writes solution filters that both .NET jobs build. Pushes to `main`, releases, and dispatches build and test the whole solution. So does a pull request that changes anything outside a project, such as a workflow, a script, the `Makefile`, a build-wide props file, or package versions. A pull request that changes no project and only documentation skips the .NET jobs. A break that the project graph cannot see, such as a reflection-only or runtime-discovered dependency, surfaces only on the full run after merge.
- **Coverage runs outside the required check.** `coverage.yml` builds and runs the unit suite with coverage on each push to `main`, and uploads the `coverage-results` artifact. Nothing gates on it.
- **CodeQL does not compile.** It uses build-mode none, so it does not analyze code that source generators emit.
- **`main` requires only the `CI status` job in `ci.yml`.** A skipped job passes. Add every new CI job to the `needs` list of `CI status`. When you change a trigger on a required workflow, change branch protection in the same change.
- **CI runs no integration suite.** See [Work in the affected scope](#work-in-the-affected-scope).
- **Every CI run checks layering.** The `changes` job runs `make check-layering` (an Abstractions package references only Abstractions or foundation packages; a family's root package never references that family's providers; no public namespace or source folder is named after a kind of type).
- **Some legs run only on a release.** Pack and SBOM, the Africa/Cairo test leg, and the messaging and R2 conformance legs run only for a published release or a `workflow_dispatch` with `release_checks`. Before you tag a change to packaging or time handling, rehearse that path.
- **Packages publish only from a published GitHub Release.** Release Drafter never publishes. A release pushes to GitHub Packages, then to nuget.org. To stop at GitHub Packages, set the repository variable `PUBLISH_NUGET_ORG` to `false` before you publish the release (`gh variable set PUBLISH_NUGET_ORG --body false`); delete it to publish to nuget.org again. Before 1.0, Release Drafter resolves the `major` label to a minor bump.
- **A pull request's title sets its release labels.** The autolabeler maps the Conventional Commit type to a release-note category, and a `!` before the colon adds `major`. Mark every breaking change with `!`: without it, the release drafts as a patch.

The solution file is [headless-framework.slnx](headless-framework.slnx). [dotnet-tools.json](dotnet-tools.json) pins the CLI tools. Run `dotnet tool restore`, then `dotnet <tool>`.

## Tests

| Suffix | Contents |
| --- | --- |
| `*.Tests.Unit` | Isolated and mocked. No external dependencies. |
| `*.Tests.Integration` | Real dependencies through Testcontainers. |
| `*.Tests.Harness` | Shared fixtures, builders, and cross-provider conformance suites. |

The stack is xUnit v3 on Microsoft Testing Platform, AwesomeAssertions (a FluentAssertions fork), NSubstitute, and Bogus.

Derive test classes from `TestBase` (`Headless.Testing.Tests`). Pass its `protected static CancellationToken AbortToken` to async calls, and never reference `TestContext.Current.CancellationToken` directly. [docs/llms/testing.md](docs/llms/testing.md) documents the rest of the `Headless.Testing` API, including the Testcontainers fixtures.

To add a second provider-integration project for one feature, extract a shared `*.Tests.Harness` project first instead of copying the fixture; match an existing harness project.

## Conventions

### Follow the existing pattern

Consistency across 150+ packages matters more than a local improvement. Before you add code, find the closest existing example and match it: a sibling provider, another feature's root package, or a test project of the same kind. Match its file layout, naming, registration shape, options class, and test structure.

- **Invent no new convention in a single place.** If no existing pattern covers the case, ask before you choose one.
- **If you find a better convention, propose it for the whole repository.** Keep the current change consistent with the existing pattern. Then describe the proposed refactor to the user, or in the PR description: the convention, the reason it is better, and the projects it would change.

### Write each capability once

Duplicated logic across packages drifts apart. Before you write code, search for an existing implementation in `src/`, not only in the package you are changing.

- **Reuse an existing implementation** instead of copying it.
- **Move shared logic down.** If two packages need the same code, put it in the lowest package that both already reference: the feature's Abstractions or root package, or `Headless.Extensions`. Do not copy it into each package.
- **Check `Headless.Extensions` first for general-purpose code.** See [Reuse before you write a utility](#reuse-before-you-write-a-utility).

### Code rules

- **File header.** Start every `.cs` file with `// Copyright (c) Mahmoud Shaheen. All rights reserved.`
- **Argument validation.** Use `Headless.Checks` (`Argument.*`, `Ensure.*`), not `ArgumentNullException.ThrowIfNull`, `ArgumentOutOfRangeException.ThrowIfGreaterThan`, or their siblings.
- **Package versions.** Keep every version in `Directory.Packages.props`. Never put a `Version` attribute in a `.csproj`.
- **Namespaces.** The project's `<RootNamespace>` is the anchor. Sibling packages may share one family root if type names stay unique across them. Put types in the owning namespace and the registration API in the family root. Put helpers for a foreign type in that type's namespace, inside a `Headless`-prefixed holder class. Before you place a type, an extension holder, or a new package's namespace, match the closest existing package.
- **Public API design.** Public and protected API follows the .NET Framework Design Guidelines. Put a new public type in its family root namespace. Add a sub-namespace only for a distinct audience or opt-in area, such as a provider, `Internal`, extension points for implementers, `Testing`, or `Dashboard`. Never add one for a kind of type (`Models`, `Enums`, `Interfaces`, `Exceptions`, `Helpers`, `Extensions`, `Constants`) or a bucket (`Abstractions`, `Core`, `Common`, `Contracts`), because each one costs consumers another `using` and says nothing about the scenario. `make check-layering` checks every segment after `Headless`. Suffix types by base type (`Exception`, `Attribute`, `EventArgs`, `Collection`), name a `[Flags]` enum in the plural and any other enum in the singular, and never add an `Enum` or `Flags` suffix. Mark preview API `[Experimental("<ID>")]` and deprecated API `[Obsolete]` with a `DiagnosticId`. Three deviations are deliberate: private members use `_PascalCase`, registration lives in the family root instead of `Microsoft.Extensions.DependencyInjection`, and helpers for a foreign type live in that type's namespace.
- **The `Headless` prefix on extension methods.** Prefix only an extension that wires a Headless-owned concept (`AddHeadless{Feature}`, `UseHeadless`, `UseHeadlessTenancy`), so the name says the framework owns the behavior. Leave a generic HTTP or hosting utility unprefixed and descriptive (`UseRedirectToCanonicalUrl`, `MapHostRedirects`). The exception is a generic name that a widely used package already declares in a namespace consumers import, such as `Microsoft.AspNetCore.Builder`. Keep the prefix there and say why in the XML docs, because the duplicate signature makes every call ambiguous (CS0121) in an app that references both.
- **Folders.** Folders do not shape namespaces, so they exist only to help a reader find code. Keep a package's front door at its root: `Setup.cs`, the setup builder, options and their validators, and the main public contracts. A package under about 20 source files stays flat. A larger one groups the rest by feature or area (`Scheduling/`, `Outbox/`, `Storage/`), never by kind of type, so no `Models`, `Contracts`, `Extensions`, `Exceptions`, `Helpers`, `Constants`, `Abstractions`, `Entities`, `Enums`, `Interfaces`, `Base`, `Utilities`, `Common`, `Core`, or `Misc` folder. `Internal/` holds internal-only types, and `Resources/` holds `.resx` files, whose generated class takes the folder's namespace. Nest at most two levels below the package root. `make check-layering` rejects a kind-named or deeper folder.
- **Analyzer suppressions.** Write the reason inline on the pragma: `#pragma warning disable CA2000 // <why>`. Never put it in a comment block above. Extend an existing pragma instead of adding a second one. Suppress only a false positive, or a case where the fix makes the code worse, and say which one in the pragma. Blocking calls in async code report through CA1849, which has no exclusion list, so a non-blocking synchronous call it flags takes a pragma too. A rule's severity in `.editorconfig` is a repository decision, not part of a code change.
- **JSON extension data.** Declare a `[JsonExtensionData]` property as `{ get; set; }`, never `init`. Every source-generated `JsonSerializerContext` whose models carry one must declare `[JsonSerializable(typeof(object))]` and `[JsonSerializable(typeof(JsonElement))]`. Either mistake throws only at runtime, on deserialization, while the build stays green.
- **Clocks.** The registered `TimeProvider` is for times the app records and for schedules tests drive by advancing it. A retry back-off, reconnect, or I/O timeout waits real time: leave Polly on its default clock (setting `ResiliencePipelineBuilderBase.TimeProvider` fails RS0030), and use `ReconnectBackoff` for a reconnect loop, passing it no app clock. A pipeline that must follow the app clock suppresses RS0030 with the reason. [docs/llms/index.md](docs/llms/index.md) states the rule and its one standing exception.
- **Error codes.** `ProblemDetails` error codes use the `g:lower_snake_case` shape. To add one, change a `MessageDescriber` class, `Messages.resx`, and `Messages.ar.resx`.
- **Deliberate non-goals.** `ICache` implementations do not enforce key length. `CacheInvalidationMessage` and similar DTOs do not enforce payload size. Consuming applications own those limits, at their own boundaries and in their broker configuration. Add no enforcement here.

### Reuse before you write a utility

`Headless.Extensions` is the framework's base library, and it most likely already ships the helper you need. Before you write any general-purpose code, check [docs/llms/extensions.md](docs/llms/extensions.md). This covers string, collection, date, IO, and reflection helpers, result and error types, guards, domain primitives and value objects, pagination, constants, and validators.

- **Grep by capability, not by package name**, then read only the section you hit. The types span several namespaces (`Headless.Primitives`, `Headless.Collections`, `Headless.Threading`, `Headless.IO`). Many are extension methods that appear on BCL types in `System.*`.
- **Read the type's Design constraints before you use it.** They state the non-obvious behavior. For example, `Currency` `*` and `/` take a `decimal` scalar, the `KeyedAsyncLock` timeout overload returns `null` instead of throwing, and `ParallelForEachAsync` does not preserve order.
- **If the helper does not exist, add it** to `Headless.Extensions` or the matching foundational package instead of a local copy. Then update `docs/llms/extensions.md` as [docs/authoring/AUTHORING.md](docs/authoring/AUTHORING.md) describes. Update the package README only when the package's purpose or name changes.

### New projects

Give every new `.csproj` a Headless MSBuild SDK, not the stock `Microsoft.NET.Sdk`. [global.json](global.json) pins the SDK versions in its `msbuild-sdks` block, so omit the version from the declaration: `<Project Sdk="Headless.NET.Sdk.Web">`.

| Project type | SDK |
| --- | --- |
| Library, console app | `Headless.NET.Sdk` |
| ASP.NET Core / Web API | `Headless.NET.Sdk.Web` |
| Test project (xUnit v3, MTP) | `Headless.NET.Sdk.Test` |
| Razor class library | `Headless.NET.Sdk.Razor` |
| Blazor WebAssembly | `Headless.NET.Sdk.BlazorWebAssembly` |
| WPF / Windows Forms | `Headless.NET.Sdk.WindowsDesktop` |

Then add the project to [headless-framework.slnx](headless-framework.slnx). These SDKs apply a strict baseline: nullable references, current analyzers, a ban on `Newtonsoft.Json`, deterministic builds, and CI-aware warning handling. To disable a default, document why. The [Headless SDK README](https://raw.githubusercontent.com/xshaheen/headless-sdk/refs/heads/main/README.md) lists the configuration switches and `Disable*` properties.

## Documentation

- **`docs/llms/`** is the canonical consumer contract: how an application chooses, wires, and calls each domain. [docs/llms/index.md](docs/llms/index.md) routes tasks to domain files. Each `docs/llms/<domain>.md` owns that domain's concepts, trade-offs, setup, and provider behavior. Read the relevant guide before you integrate with a domain. The large files (`messaging.md`, `jobs.md`) are ~190 KB each, about 50k tokens. Route through `index.md`, grep the domain file for the heading or symbol you need, and read only that section.
- **Package READMEs** are small NuGet landing pages. They say why the package exists, how to install it, and link to the root README and the domain guide. Keep setup and API reference out of them. Before you edit the index, a domain guide, or a package README, read [docs/authoring/AUTHORING.md](docs/authoring/AUTHORING.md).

### Update `docs/llms` with every package change

Consumers and their agents read `docs/llms/`, so a package change is not done until the owning `docs/llms/<domain>.md` describes it. Update the docs in the same change when a change under `src/Headless.*` does any of these:

- Changes the public API.
- Adds, renames, or removes a package.
- Changes consumer-visible behavior, such as defaults, ordering, retry, cancellation, or threading.
- Adds or removes a configuration option.

The "Change routing" table in [docs/authoring/AUTHORING.md](docs/authoring/AUTHORING.md#change-routing) lists every file each kind of change touches. For example, an added package also touches `README.md`, `README.ar.md`, and `eng/expected-packages.txt`. Internal refactors and perf-only, test-only, or formatting changes need no docs update.
