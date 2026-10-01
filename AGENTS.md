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

Registration follows the same split. The feature's Core package owns `AddHeadless{Feature}(Action<Headless{Feature}SetupBuilder>)` and the provider gates. Each provider package adds `Use{Provider}` members to the builder. Before you add a registration entry point, a provider package, or an options class, read [provider setup and options](docs/solutions/conventions/provider-setup-and-options.md).

Transactions are explicit. `IUnitOfWorkFactory` is a singleton with no `Current` property. The `IUnitOfWork` handle is the unit's only identity, and the receiver always decides whether to enlist. Before you change how Messaging, Jobs, or a storage provider joins a unit, read [unit-of-work propagation and enlistment](docs/solutions/architecture-patterns/unit-of-work-propagation-and-enlistment-contract.md).

Relational storage features create their tables through one `SchemaRunner`, which trusts `headless_schema_history`. A table dropped by hand, or by a test reset on a reused container, is not recreated until its feature's history rows go too. Never edit a released step: add a new one. Before a release no database runs the step, so change it in place. Put every configurable object name in `SchemaContribution.FeatureId`. Before you add or change a storage feature's DDL, read [one schema runner](docs/solutions/architecture-patterns/schema-runner-history-lock-and-verify.md).

## Build and test

Use the `make` targets instead of raw `dotnet`. They pin configuration, results directories, and parallelism. `make help` lists every target. The rules below cover what `make help` does not say.

### Work in the affected scope

The solution has ~430 projects. A full build takes a long time, and the integration suites take minutes per module. Build and test only the projects your change affects:

1. The projects you changed.
2. The projects whose behavior depends on the code you changed. For example, a change to `Headless.Caching.Abstractions` affects every `Headless.Caching.*` provider.
3. The unit and integration test projects of the projects in 1 and 2.

Use the project-scoped targets: `build-project`, `test-project`, `test-affected`, and `quality-analyzers-affected`. Run a solution-wide target (`build`, `rebuild`, `test`, `test-unit`, `test-integration`, `coverage`, `quality-analyzers`) only when the user asks for it, or when the change touches shared build files such as `Directory.Build.props`, `Directory.Packages.props`, or `eng/`.

### Scope a run

- **Start with `make test-affected`.** It maps every change against `@{upstream}` (else `origin/main`) to the matching `*.Tests.Unit` projects and runs only those. Uncommitted and untracked files count. It names each change it could not map, so a run that tested nothing does not read as a pass.
- **`make test-affected` matches by name only.** A change in `src/<X>` selects `tests/<X>.Tests.Unit` and nothing else. It does not select dependent projects, and it skips `*.Tests.Integration` and `*.Tests.Harness`. Run the test projects of dependents yourself with `make test-project TEST_PROJECT=<csproj>`.
- **Run integration tests per project.** When you change provider behavior, run each affected `tests/<X>.Tests.Integration` project with `make test-project TEST_PROJECT=<csproj>`. These projects need Docker. CI does not run them, so a local run is the only check.
- **Set `TEST_PROJECT` to scope a filtered run.** `test-class`, `test-method`, `test-namespace`, `test-trait`, and `test-query` choose which tests run. Without `TEST_PROJECT`, they build all ~430 projects and pass the whole solution to the runner, integration modules included. With it, the filter applies inside that one project:

  ```sh
  make test-class CLASS='*ClockTests' TEST_PROJECT=tests/Headless.Core.Tests.Unit/Headless.Core.Tests.Unit.csproj
  ```

  To scope a build, use `make build-project PROJECT=…`.

### Restore and build

- **`make test-project` asserts restore and does not repeat it.** It runs with `--no-restore`. If `obj/project.assets.json` is missing, or older than `Directory.Packages.props` or the project lock file, it fails and prints the exact `make restore-project` command to run. With warm outputs and no source change, `make test-project-fast` (`--no-build --no-restore`) costs less. A fresh clone or worktree needs `make bootstrap`.
- **`build-fast` and `build-project-fast` only type-check.** They drop analyzers and MinVer. Alternating them with a normal build recompiles the graph, so use one kind of build for the whole session. A later incremental `make build` also skips up-to-date projects without running their analyzers. Only `rebuild`, `quality-analyzers`, and `quality-analyzers-affected` build with `--no-incremental`, so only they re-check analyzers.
- **The dashboards need Node 22+ on `PATH`.** A build of `Headless.Jobs.Dashboard` or `Headless.Messaging.Dashboard` runs `npm ci` and a Vite build (`eng/DashboardSpa.targets`), then embeds the generated `wwwroot/dist`. That folder is not committed. `make dashboards` rebuilds the SPAs. If `wwwroot/dist` already exists, pass `/p:BuildDashboardSpa=false` to skip the npm build.

### Gates

- **A green test run is not a clean build.** The test SDK's `DisableAnalyzersWhenRunningTests` target turns analyzers off during the MTP build phase, so `make test-project` accepts code that a real build rejects. After a test run, run `make quality-analyzers-affected` or `make build-project PROJECT=<the project you changed>`.
- **Your builds already treat warnings as errors.** The SDK does this whenever it detects an agent CLI: `SupportDetectLlmContext.props` reads `CLAUDECODE`, `CODEX_CLI`, and similar variables. To see the human posture, pass `HeadlessIsLlmContext=false`.
- **Locally, only formatting is gated.** The pre-push hook runs a CSharpier check on changed files. It does not compile, run analyzers, or run tests. To compile the solution by hand, run `make hook-build`.
- **Run the analyzers last, before you open a PR.** When all work is done and the build, test, and format gates pass, run `make quality-analyzers-affected`. It reports info-level suggestions as well as warnings and errors, and fails on any of them. Resolve every finding:
  - Fix each valid finding.
  - Suppress each invalid finding with an inline reason, as the analyzer-suppression convention in [Conventions](#conventions) describes.

  Then run the affected tests again if a fix changed code. `quality-analyzers-affected` checks only the projects you changed. `make quality-analyzers` is the whole-solution version. It is stricter than CI: CI's `Lint · .NET analyzers` job runs only `make rebuild`, so it fails on warnings and errors but not on info-level suggestions. To narrow a noisy run, set `QUALITY_SEVERITY=warn` or `QUALITY_DIAGNOSTICS=MA0154`. To check one dependent project, use `make quality-analyzers-project PROJECT=<csproj>`.
- **`make quality-fix` refuses to run without a filter.** Pass one rule at a time in `QUALITY_DIAGNOSTICS`, and run `make rebuild` between rules, because four fixers emit code that does not compile. See [dotnet format analyzer fixers](docs/solutions/tooling-decisions/dotnet-format-analyzer-fixers.md).

### CI

- **CI compiles twice, then runs the unit suite.** The `build` job uses `-p:RunAnalyzers=false`, and the `analyzers` job runs the full analyzer set. Both use `--no-incremental` and treat warnings as errors.
- **`main` requires only the `CI status` job in `ci.yml`.** A skipped job passes. Add every new CI job to the `needs` list of `CI status`. When you change a trigger on a required workflow, change branch protection in the same change.
- **CI runs no integration suite.** See [Work in the affected scope](#work-in-the-affected-scope).
- **Some legs run only on a release.** Pack and SBOM, the Africa/Cairo test leg, and the messaging and R2 conformance legs run only for a published release or a `workflow_dispatch` with `release_checks`. Before you tag a change to packaging or time handling, rehearse that path.
- **Packages publish only from a published GitHub Release.** Release Drafter never publishes.

The solution file is [headless-framework.slnx](headless-framework.slnx). [dotnet-tools.json](dotnet-tools.json) pins the CLI tools. Run `dotnet tool restore`, then `dotnet <tool>`.

## Tests

| Suffix | Contents |
| --- | --- |
| `*.Tests.Unit` | Isolated and mocked. No external dependencies. |
| `*.Tests.Integration` | Real dependencies through Testcontainers. |
| `*.Tests.Harness` | Shared fixtures, builders, and cross-provider conformance suites. |

The stack is xUnit v3 on Microsoft Testing Platform, AwesomeAssertions (a FluentAssertions fork), NSubstitute, and Bogus.

Derive test classes from `TestBase` (`Headless.Testing.Tests`). Pass its `protected static CancellationToken AbortToken` to async calls, and never reference `TestContext.Current.CancellationToken` directly. [docs/llms/testing.md](docs/llms/testing.md) documents the rest of the `Headless.Testing` API, including the Testcontainers fixtures.

To add a second provider-integration project for one feature, extract a shared harness first instead of copying the fixture. See [Tests.Harness extraction](docs/solutions/best-practices/tests-harness-extraction.md).

## Conventions

### Follow the existing pattern

Consistency across 150+ packages matters more than a local improvement. Before you add code, find the closest existing example and match it: a sibling provider, another feature's Core package, or a test project of the same kind. Match its file layout, naming, registration shape, options class, and test structure.

- **Invent no new convention in a single place.** If no existing pattern covers the case, ask before you choose one.
- **If you find a better convention, propose it for the whole repository.** Keep the current change consistent with the existing pattern. Then describe the proposed refactor to the user, or in the PR description: the convention, the reason it is better, and the projects it would change.

### Write each capability once

Duplicated logic across packages drifts apart. Before you write code, search for an existing implementation in `src/`, not only in the package you are changing.

- **Reuse an existing implementation** instead of copying it.
- **Move shared logic down.** If two packages need the same code, put it in the lowest package that both already reference: the feature's Abstractions or Core package, or `Headless.Extensions`. Do not copy it into each package.
- **Check `Headless.Extensions` first for general-purpose code.** See [Reuse before you write a utility](#reuse-before-you-write-a-utility).

### Code rules

- **File header.** Start every `.cs` file with `// Copyright (c) Mahmoud Shaheen. All rights reserved.`
- **Argument validation.** Use `Headless.Checks` (`Argument.*`, `Ensure.*`), not `ArgumentNullException.ThrowIfNull`, `ArgumentOutOfRangeException.ThrowIfGreaterThan`, or their siblings.
- **Package versions.** Keep every version in `Directory.Packages.props`. Never put a `Version` attribute in a `.csproj`.
- **Namespaces.** The project's `<RootNamespace>` is the anchor. Sibling packages may share one family root if type names stay unique across them. Put types in the owning namespace and the registration API in the family root. Put helpers for a foreign type in that type's namespace, inside a `Headless`-prefixed holder class. Before you place a type, an extension holder, or a new package's namespace, read the [namespace policy](docs/solutions/conventions/namespace-policy.md).
- **Analyzer suppressions.** Write the reason inline on the pragma: `#pragma warning disable MA0045 // <why>`. Never put it in a comment block above. Extend an existing pragma instead of adding a second one. Suppress only a false positive, or a case where the fix makes the code worse, and say which one in the pragma. For a false positive about a type rather than a line, add `[Namespace.Type]::Method` to `eng/analyzers/*.txt`. Those files reach every project through `AdditionalFiles`. A rule's severity in `.editorconfig` is a repository decision, not part of a code change.
- **JSON extension data.** Declare a `[JsonExtensionData]` property as `{ get; set; }`, never `init`. Every source-generated `JsonSerializerContext` whose models carry one must declare `[JsonSerializable(typeof(object))]` and `[JsonSerializable(typeof(JsonElement))]`. Either mistake throws only at runtime, on deserialization, while the build stays green.
- **Error codes.** `ProblemDetails` error codes use the `g:lower_snake_case` shape. To add one, change a `MessageDescriber` class, `Messages.resx`, and `Messages.ar.resx`. See [ProblemDetails error codes](docs/solutions/conventions/problem-details-error-codes.md).
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

- **`docs/solutions/`** stores past fixes, conventions, and decisions. Files sit in category folders (`api`, `concurrency`, `conventions`, `messaging`, and more) and carry YAML frontmatter (`module`, `tags`, `problem_type`). Search it before you implement, debug, or decide in an area it covers.
- **`docs/llms/`** is the canonical consumer contract: how an application chooses, wires, and calls each domain. [docs/llms/index.md](docs/llms/index.md) routes tasks to domain files. Each `docs/llms/<domain>.md` owns that domain's concepts, trade-offs, setup, and provider behavior. Read the relevant guide before you integrate with a domain. The large files (`messaging.md`, `jobs.md`) are ~190 KB each, about 50k tokens. Route through `index.md`, grep the domain file for the heading or symbol you need, and read only that section.
- **Package READMEs** are small NuGet landing pages. They say why the package exists, how to install it, and link to the root README and the domain guide. Keep setup and API reference out of them. Before you edit the index, a domain guide, or a package README, read [docs/authoring/AUTHORING.md](docs/authoring/AUTHORING.md).
- **`CONCEPTS.md`** holds the shared domain vocabulary: entities, named processes, and status concepts with a project-specific meaning.

### Update `docs/llms` with every package change

Consumers and their agents read `docs/llms/`, so a package change is not done until the owning `docs/llms/<domain>.md` describes it. Update the docs in the same change when a change under `src/Headless.*` does any of these:

- Changes the public API.
- Adds, renames, or removes a package.
- Changes consumer-visible behavior, such as defaults, ordering, retry, cancellation, or threading.
- Adds or removes a configuration option.

The "Change routing" table in [docs/authoring/AUTHORING.md](docs/authoring/AUTHORING.md#change-routing) lists every file each kind of change touches. For example, an added package also touches `README.md`, `README.ar.md`, and `eng/expected-packages.txt`. Internal refactors and perf-only, test-only, or formatting changes need no docs update.
