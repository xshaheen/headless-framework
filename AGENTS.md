# Repository Guidelines

## What this repository is

**headless-framework** is a modular .NET 10 framework for APIs and backend services. It ships 150+ NuGet packages grouped by domain (API, Blobs, Caching, Messaging, ORM, and more). It is unopinionated and has no lock-in.

Two facts decide most judgment calls:

- **It is a framework, not an application.** Downstream consumers and future providers call abstractions, extension points, and helpers that nothing in this repository calls. Keep a public member that has no local caller.
- **It is greenfield.** Prefer the simpler, cleaner API, even when it breaks consumers. A breaking change that improves correctness or performance beats a compatibility shim, unless the request says otherwise. The same applies to the database: no deployed schema or data exists. Change a schema in place and reset or rewrite its migrations. Write no migration, backfill, or compatibility shim for a schema nobody runs.

Coverage follows the testing diamond: integration tests carry most of the weight, so no floor asks unit tests alone to cover code whose real behavior sits behind a broker, a database, or an HTTP host. A package has such an external dependency when it owns a `tests/<Package>.Tests.Integration` project.

| Measure | Floor | Tests counted | Measured by |
| --- | --- | --- | --- |
| Changed lines | 80% line, 70% branch | unit tests; unit + integration for a package with an integration project | `make verify-affected` for packages without an integration project; `make test-affected-integration` for packages with one |
| Unit coverage of a package without an integration project | 60% line, whole assembly | unit tests | `make verify-affected` |
| Mutation score | target 70%, goal 85% | unit tests | `make mutation` and the weekly `mutation.yml`; reported, never gated |

Changed lines are the executable `src/` lines the branch adds or changes against the merge base, uncommitted work included, so a PR answers for its own code and not for older code in the same assembly. A package with an integration project is never held to a unit-only figure.

The floors are comply-or-explain, not a hard gate. The proof marks a miss as a warning and lists every uncovered changed line; the proof still passes. Meet the floor, or explain each miss in a "Coverage below floor" row of the PR's Verification table with one of these reasons:

- **Glue code**: registration, options wiring, or a one-line delegation whose failure every other test would expose.
- **Integration-only behavior**: code whose real behavior only a broker, database, or host shows, covered by an integration test that `make test-affected-integration` did not run here (name it).
- **Unreachable branch**: a defensive branch no test can reach without faking the runtime.
- **Cost above risk**: a test that would need more fake machinery than the code it checks; say what would break and why that is acceptable.

"Not done yet" is not a reason: add the tests. `COVERAGE_GATE=fail` turns a miss into a failed proof for anyone who wants the strict gate.

## Architecture

Each feature ships as one abstraction package plus one package per provider. `Headless.<Feature>.Abstractions` holds the interfaces, and `Headless.<Feature>.<Provider>` implements them. For example, `Headless.Caching.Redis` implements `Headless.Caching.Abstractions`.

Registration follows the same split. The feature's root package (`Headless.<Feature>`) owns `AddHeadless{Feature}(Action<Headless{Feature}SetupBuilder>)` and the provider gates. Each provider package adds `Use{Provider}` members to the builder, in a single static `Setup{Provider}` class in `Setup.cs` at the package root, with the same overload trio (`IConfiguration`, `Action<TOptions>`, `Action<TOptions, IServiceProvider>`). Options validate through FluentValidation via the `Headless.Hosting` `AddOptions<TOptions, TValidator>()` and `Configure<TOptions, TValidator>(...)` extensions, with the validator in the options file. Before you add a registration entry point, a provider package, or an options class, match the closest existing provider package.

Transactions are explicit. `IUnitOfWorkFactory` is a singleton with no `Current` property. The `IUnitOfWork` handle is the unit's only identity, and the receiver always decides whether to enlist. Before you change how Messaging, Jobs, or a storage provider joins a unit, read the contract in [docs/llms/unit-of-work.md](docs/llms/unit-of-work.md) and the design rationale in the contributor notes.

Relational storage features create their tables through one `SchemaRunner`, which trusts `headless_schema_history`. A table dropped by hand, or by a test reset that keeps the database, is not recreated until its feature's history rows go too. Never edit a released step: add a new one. Before a release no database runs the step, so change it in place. Put every configurable object name in `SchemaContribution.FeatureId`. Before you add or change a storage feature's DDL, match an existing feature's `SchemaContribution`.

Startup does only what a host needs to serve requests correctly. Creating tables, broker topology, or seed data is a deploy step (`resources provision`, `seed`, or `deploy` for both); the host only verifies a resource, failing the start or holding readiness as its `IResourceProvisioner.StartupCheck` says, discovery happens at build time through the source generators, cache warm-up runs in the background behind the readiness check, and every startup check is one cheap read. A hosted service, initializer, or `IHeadlessStartupValidator` that creates or writes external state, or repeats a round trip per feature, queue, or consumer, breaks this rule. [docs/llms/utilities.md](docs/llms/utilities.md#resource-provisioning) describes the verbs.

## Hit every surface

The defect this repository ships most is a change that works on the provider or path you tested and is missing on the others. Before you call a change done, walk this list and say which entries applied:

- **Providers.** A feature is one abstraction and several providers: Messaging transports and storages, Blobs, Caching, DistributedLocks, Jobs, Sms, Emails, and the rest. A change to the abstraction or to provider-shaped behavior needs a decision per provider, even "not supported here", recorded in the feature's `*.Tests.Harness` conformance suite or its docs.
- **Database engines.** Relational storage ships for PostgreSQL and SQL Server, often with an EF Core variant beside it. A schema, query, or lock change lands in each, through its `SchemaContribution`.
- **Registration.** `AddHeadless{Feature}`, every provider's `Use{Provider}` overload trio, and the options validator change together.
- **Generators and analyzers.** The Jobs and Messaging source generators, `Headless.Generator.Primitives`, and `Headless.UnitOfWork.Analyzers` target `netstandard2.0` and match the API by name, so a renamed attribute or type breaks discovery with no compile error.
- **Dashboards.** A new Jobs or Messaging state or operation needs its dashboard view and endpoint, or a stated reason it has none.
- **Reverse states.** A way in needs a way out and a way to see it: pause and resume, quarantine and release, provision and its startup check.
- **Docs and locales.** The owning `docs/llms/<domain>.md`, the package README, and the files [Keep `docs/llms` current](#keep-docsllms-current) routes the change to. A user-facing `Messages.resx` string also goes in its `.ar` file (and `.ar-EG` where one exists).
- **Demo and sandboxes.** The `demo/` hosts and `sandboxes/` compile against the public API; update the one that shows the changed scenario.

## Build and test

Use the `make` targets instead of raw `dotnet`. They pin configuration, results directories, and parallelism. `make help` lists every target, `make doctor` checks the prerequisites with a fix for each, and `make check` runs the CI gates over the affected scope. The rules below cover what `make help` does not say.

### Work in the affected scope

The solution has ~430 projects. A full build takes a long time, and the integration suites take minutes per module. Build and test only the projects your change affects:

1. The projects you changed.
2. The projects whose behavior depends on the code you changed. For example, a change to `Headless.Caching.Abstractions` affects every `Headless.Caching.*` provider.
3. The unit and integration test projects of the projects in 1 and 2.

`make affected` prints that set, computed from the `ProjectReference` graph (`scripts/project-graph.py`) against the branch's upstream when that is another branch (a stacked branch), else `origin/main`, uncommitted and untracked files included. A change to a build file a project file `<Import>`s (such as `eng/DashboardSpa.targets`) selects only the importing projects. A change to a build-wide file (`global.json`, `Directory.*.props`, `.editorconfig`) selects every project below it, except a `Directory.Packages.props` change that only adds, removes, or moves `<PackageVersion>` versions: that selects the projects whose `packages.lock.json` resolves those packages (and any project without a lock file). CI's scope still runs everything for it. It adds only the direct dependents of a changed project. A pull request's CI run adds every transitive dependent (`scripts/project-graph.py affected --transitive`), so CI can build and test a dependent of a dependent that the local run skipped; `AFFECTED_TRANSITIVE=true` widens a local run to match.

Use the affected and project-scoped targets: `test-class` with `TEST_PROJECT`, `test-project`, `build-project`, `build-affected`, `test-affected`, `test-failed`, `verify-affected`, `test-affected-integration`, and `quality-analyzers-affected`. Before each commit, run `make lint-affected`: the gate's format, test-discipline, and analyzer stages over the changed projects, with no test run, so a finding the build does not report surfaces before the gate's suites run. Run a solution-wide target (`build`, `rebuild`, `test`, `test-unit`, `test-integration`, `coverage`, `quality-analyzers`) only when the user asks for it, or when the change touches shared build files such as `Directory.Build.props`, `Directory.Packages.props`, or `eng/`.

### Scope a run

- **Start with `make test-class` and `TEST_PROJECT`.** It runs the matching tests inside one project in seconds. Without `TEST_PROJECT`, `test-class`, `test-method`, `test-namespace`, `test-trait`, and `test-query` build all ~430 projects and pass the whole solution to the runner, integration modules included. Add `REPEAT=5` to chase a flaky test; it reports which runs failed:

  ```sh
  make test-class CLASS='*CultureHelperTests' TEST_PROJECT=tests/Headless.Extensions.Tests.Unit/Headless.Extensions.Tests.Unit.csproj
  ```

  To scope a build, use `make build-project PROJECT=…` or `make build-affected`.
- **Run `make test-affected` before the gate, not in the loop.** It builds the affected set in one solution-filter build, then runs every affected `*.Tests.Unit` project to completion: one failing project does not hide the rest. If the build fails, it skips the tests rather than run them against stale binaries. One module can dominate it: `Headless.Jobs.Composition.Tests.Unit` alone takes about four minutes. The affected test stages pass `--timeout $(TEST_TIMEOUT)` (15 minutes per module), so a hung suite fails its stage instead of running on.
- **Run `make test-affected-integration` before the PR whenever the affected set has integration projects.** It runs the affected integration suites, plus the unit suites of the edited packages that own an integration project, with coverage; it merges the reports and warns when those packages' changed lines fall below the changed-line floors, so it is the coverage check for them. With no such package edited it skips coverage. It analyzes the changed projects right after the build and skips the suites on a finding, because the fix re-runs them anyway. Run it once, after code review and `make lint-affected`: its selection follows the branch's whole diff, so a one-line review fix runs every selected suite again. It needs Docker and checks the daemon before it builds anything, and CI does not run it: the agent runs it locally on the head commit and condenses its `summary.md` into one row of the PR's Verification table, under `verify-affected`'s. Both gates reuse an earlier passing bundle of their kind when the inputs match: a message-only amend, or a rebase onto a `main` that touched nothing the selection builds, prints `Reused: commit <sha> proved the same inputs` and costs seconds. The reused `summary.md` names the commit that ran the stages; cite that commit in the row. Any change to the selection's files, the build-wide files, the branch's patch, or the gate's settings runs every stage; `PROOF_REUSE=false` always runs. The key leaves out the gate's own code (`Makefile`, `scripts/`) and counts the solution, the test-discipline baseline, and the central package versions only for the projects the selection compiles; a tooling change that can turn a verdict bumps `PROOF_GATE_VERSION` in the `Makefile`, which reruns every gate once. Both gates also reuse per module: a test module whose own `ProjectReference` closure, the repository-wide inputs, and the gate's settings match a module that passed in an earlier bundle of the same gate does not run again, and the summary lists it with the commit that ran it. A review fix in one package, or a rebase onto a `main` that changed one package, therefore reruns only the modules that compile that package; the build and analyzer stages still run. Cite the reused commits in the row too.
- **After a narrow fix, run `make test-failed`, not the whole gate.** It reads the latest `verify-affected`, `test-affected`, or `test-affected-integration` proof bundle and re-runs only the test projects whose modules failed, integration modules included. It names any other failed stage with the target that repeats it. Finish with one full run of the gate that failed. A bundle that selected no project while `HEAD` changes `src/` against `origin/main` fails its `affected-selection` stage: the base was wrong, not the tests.
- **Prove a fix with `make prove`.** A bug fix's proof is a test that fails on the base and passes on the fix: `make prove TEST='*SlugTests' TEST_PROJECT=tests/Headless.Slugs.Tests.Unit/Headless.Slugs.Tests.Unit.csproj` copies this branch's changed test files onto the merge base with `BASE` (default `origin/main`) in a detached worktree, runs the `--filter-class` match there and here (`PROVE_FILTER=--filter-method` selects by method), and prints a base and head table for the PR's Regression row. It exits 0 when the tests fail on the base and pass here, 3 when they also pass on the base, and 1 when they fail here. A purely visual fix (spacing, color, alignment) proves itself with before and after screenshots instead.
- **Fix a flaky test; never retry it into green.** No gate retries a failure, because a test that fails one run in twenty can be a real race in the code it checks. Find the cause with `make test-class ... REPEAT=20`: tests usually flake on a wall-clock wait, a fixed delay standing in for an ordering, or a process-wide listener or counter that hears tests running in parallel. If the fix cannot land in your change, open an issue and quarantine the test against it the way the messaging harness does (`tests/Headless.Messaging.Tests.Harness/README.md`), so it skips visibly until the issue closes.
- **Check a dashboard SPA with `make dashboard-jobs-test` or `make dashboard-messaging-test`.** They check the SPA's formatting with Prettier, then run its `lint:check`, `type-check`, `test:unit`, and `build-only` scripts through `npm run`, after `npm ci` when `node_modules` is missing or older than the lockfile. `make dashboards-format` fixes the formatting. Do not call `npx`: a wrapper on `PATH` can hide the tool's output. CI's dashboard jobs run the same targets. Before you change any dashboard front end, read [src/Headless.Dashboard/wwwroot/AGENTS.md](src/Headless.Dashboard/wwwroot/AGENTS.md).

### Restore and build

- **`make test-project` and `make build` assert restore and do not repeat it.** They run with `--no-restore`. If `obj/project.assets.json` is missing, or older than `Directory.Packages.props`, the project file, or the project lock file, they fail and print the exact `make restore-project` or `make restore` command to run. Run `make restore` after a package change; `rebuild` still restores. With warm outputs and no source change, `make test-project-fast` (`--no-build --no-restore`) costs less. A fresh clone or worktree needs `make bootstrap`.
- **`build-fast` and `build-project-fast` only type-check.** They drop analyzers and MinVer. Alternating them with a normal build recompiles the graph, so use one kind of build for the whole session. A later incremental `make build` also skips up-to-date projects without running their analyzers. Only `rebuild` and `quality-analyzers` build with `--no-incremental`. `verify-affected` and `quality-analyzers-affected` report every analyzer finding, info level included, on the changed projects: their build writes each compile's diagnostics to `obj/<configuration>/<tfm>/analyzers.sarif` (`eng/ProofErrorLog.targets`), and the analyzer stage reads those logs. A changed project whose log is older than its assembly, because something built it since without the log, is re-analyzed with `dotnet format analyzers`.
- **The dashboards need Node 22+ on `PATH`.** A build of `Headless.Jobs.Dashboard`, `Headless.Messaging.Dashboard`, `Headless.Cluster.Dashboard`, or `Headless.Leases.Dashboard` runs `npm ci` and a Vite build (`eng/DashboardSpa.targets`), then embeds the generated `wwwroot/dist`. That folder is not committed. `make dashboards` rebuilds the SPAs. If `wwwroot/dist` already exists, pass `/p:BuildDashboardSpa=false` to skip the npm build. `make dashboards-test` runs each SPA's CI gates (lint, type-check, Vitest, and the Vite build), and `make verify-affected` runs them for a SPA your change touches. To drive the dashboards in a browser, `make up` starts the dashboard sandbox (`sandboxes/README.md`).

### Gates

- **A green test run is not a clean build.** The test SDK's `DisableAnalyzersWhenRunningTests` target turns analyzers off for the test project itself during `dotnet test`, so analyzer findings in test code pass a test run. Referenced `src` projects still build with analyzers. `make verify-affected` and `make quality-analyzers-affected` re-check both.
- **Your builds already treat warnings as errors.** The SDK does this whenever it detects an agent CLI: `SupportDetectLlmContext.props` reads `CLAUDECODE`, `CODEX_CLI`, and similar variables. To see the human posture, pass `HeadlessIsLlmContext=false`.
- **The hooks gate formatting, not code.** Pre-commit formats staged C# files and lists only the files it changed; silence means nothing changed. Pre-push runs a CSharpier check on changed files. Neither compiles, runs analyzers, or runs tests. To format your change before that, run `make format-changed` (C#) or `make dashboards-format` (dashboard SPAs), not `dotnet csharpier` or `prettier` directly. To compile the solution by hand, run `make hook-build`.
- **Finish with `make verify-affected` and put its proof in the PR.** When all work is done, it checks the formatting of the changed C# files (the check the pre-commit hook would otherwise apply after the proof), builds the affected set, runs the analyzers on the changed projects (a finding skips the unit tests, since the fix re-runs them anyway), runs the affected unit tests with coverage, and runs the dashboard check of a SPA whose `wwwroot/` changed. It writes `artifacts/proof/<run>/summary.md` (condense it into one row of the PR's Verification table, as the template says; never paste it) and `summary.json`: stage results, failed tests with their first message line, compiler and analyzer findings grouped by rule, line and branch coverage of the changed assemblies, and the coverage gate. The gate warns below the unit floor or the changed-line floors for packages without an integration project, and lists every uncovered changed line; packages with one are named as deferred to `make test-affected-integration`. A warning leaves the proof passing: meet the floor or explain the miss, as the coverage section above says. The analyzer stage fails on info-level suggestions as well as warnings and errors. Resolve every finding:
  - Fix each valid finding.
  - Suppress each invalid finding with an inline reason, as the analyzer-suppression convention in [Conventions](#conventions) describes.

  Then run `make verify-affected` again. `make quality-analyzers-affected` runs the analyzer stage alone and checks only the projects you changed. `make quality-analyzers` is the whole-solution version. It is stricter than CI: CI's `Lint · .NET analyzers` job runs only `make rebuild`, so it fails on warnings and errors but not on info-level suggestions. To narrow a noisy run, set `QUALITY_SEVERITY=warn` or `QUALITY_DIAGNOSTICS=MA0154`. To check one dependent project, use `make quality-analyzers-project PROJECT=<csproj>`.
- **Test code may not add a real wait, a wall-clock read, a per-test timeout or retry, or a skip without an issue or capability.** `make test-discipline` holds every test file (C# under `tests/` and the dashboard SPAs' tests) to its count per rule in `eng/test-discipline-baseline.txt`, which froze the existing ones; `verify-affected` checks the changed test files, and CI checks all of them. A count that rises fails naming the file, rule, and fix; a count that falls fails until `make test-discipline-baseline` writes it back, which never raises a line. A legitimate case, such as a test of real timeout behavior or a subprocess or network fixture, takes a reason comment on the line or the line above (`// real-wait: <why>`, `// wall-clock:`, `// test-timeout:`, `// skip: <capability>`, `// quarantine: #123 <why>`). The messaging fast tier ignores the `real-wait` opt-out, because it runs on simulated time. `scripts/test-discipline.py` lists what each rule matches.
- **Run `make quality-references` after you add or change a reference.** It builds with ReferenceTrimmer, under `artifacts/reftrim` and with its own lock files, and lists every `PackageReference` or `ProjectReference` the compiled code does not use. It exits 3 on findings. Remove each finding, or mark a deliberate reference `TreatAsUsed="true"` with the reason in a comment. Turn the tool off for a project that compiles code at run time (`<EnableReferenceTrimmer>false</EnableReferenceTrimmer>`). Pass `PROJECT=<csproj>` to check one project. CI does not run it.
- **`make quality-fix` refuses to run without a filter.** Pass one rule at a time in `QUALITY_DIAGNOSTICS`, and run `make rebuild` between rules, because four fixers emit code that does not compile.

### CI

- **CI compiles twice, then runs the unit suite.** The `build` job uses `-p:RunAnalyzers=false`, and the `analyzers` job runs the full analyzer set. Both use `--no-incremental` and treat warnings as errors.
- **A pull request builds and tests only what it can affect.** `make ci-scope` selects the changed projects, every transitive dependent, and their test projects, and writes solution filters that both .NET jobs build. Pushes to `main`, releases, and dispatches build and test the whole solution. So does a pull request that changes anything outside a project, such as a workflow, a script, the `Makefile`, a build-wide props file, or package versions. A pull request that changes no project and only documentation skips the .NET jobs. A break that the project graph cannot see, such as a reflection-only or runtime-discovered dependency, surfaces only on the full run after merge.
- **Coverage runs outside the required check.** `coverage.yml` builds and runs the unit suite with coverage on each push to `main`, and uploads the `coverage-results` artifact. Nothing gates on it; the coverage floors are enforced by the local proof targets above.
- **Mutation testing runs weekly.** `mutation.yml` runs Stryker.NET (`make mutation`) on the foundation packages every Monday and reports each score in the run summary. It never gates. Stryker.NET's Microsoft Testing Platform runner is in preview, so check the report before you act on a score.
- **CodeQL does not compile.** It uses build-mode none, so it does not analyze code that source generators emit.
- **`main` requires only the `CI status` job in `ci.yml`.** A skipped job passes. Add every new CI job to the `needs` list of `CI status`. When you change a trigger on a required workflow, change branch protection in the same change.
- **The required checks run no integration suite.** See [Work in the affected scope](#work-in-the-affected-scope). `messaging-integration.yml` runs the messaging integration and chaos suites outside `CI status`, so its failures never block a pull request: every messaging integration suite on each pull request and push to `main` that reaches one through the project graph, and a full run with chaos weekly. Its result on a pull request is real-broker evidence beside the author's gate rows.
- **The Jobs chaos suite runs weekly.** `jobs-chaos.yml` runs `make test-jobs-chaos` (the `Headless.Jobs.Tests.Chaos` and `Headless.DistributedLocks.Tests.Chaos` projects) at full sizes every Sunday and on dispatch, outside `CI status`, and publishes each scenario's verdict and the side-effect log of any failure in the run summary. Replay a failure locally with the seed it printed: `HEADLESS_CHAOS_SEED=<n> make test-jobs-chaos`.
- **Every CI run checks layering.** The `changes` job runs `make check-layering` (an Abstractions package references only Abstractions or foundation packages; a family's root package never references that family's providers; no public namespace or source folder is named after a kind of type).
- **Some legs run only on a release.** Pack and SBOM, the Africa/Cairo test leg, and the messaging and R2 conformance legs run only for a published release or a `workflow_dispatch` with `release_checks`. Before you tag a change to packaging or time handling, rehearse that path.
- **Packages publish only from a published GitHub Release.** Release Drafter never publishes. A release pushes to GitHub Packages, then to nuget.org only while the repository variable `PUBLISH_NUGET_ORG` is not `false`; check it with `gh variable get PUBLISH_NUGET_ORG` before you publish. Set it to `false` to stop at GitHub Packages (`gh variable set PUBLISH_NUGET_ORG --body false`), and delete it to publish to nuget.org again. Before 1.0, Release Drafter resolves the `major` label to a minor bump.
- **A pull request's title sets its release labels.** The autolabeler maps the Conventional Commit type to a release-note category, and a `!` before the colon adds `major`. Mark every breaking change with `!`: without it, the release drafts as a patch.

The solution file is [headless-framework.slnx](headless-framework.slnx). [dotnet-tools.json](dotnet-tools.json) pins the CLI tools. Run `dotnet tool restore`, then `dotnet <tool>`.

## Tests

| Suffix | Contents |
| --- | --- |
| `*.Tests.Unit` | Isolated and mocked. No external dependencies. |
| `*.Tests.Integration` | Real dependencies through Testcontainers. |
| `*.Tests.Harness` | Shared fixtures, builders, and cross-provider conformance suites. |
| `*.Tests.Chaos` | Multi-host chaos and soak suites. Affected runs build them but never run them; their own `make` target and a scheduled workflow do. |

The stack is xUnit v3 on Microsoft Testing Platform, AwesomeAssertions (a FluentAssertions fork), NSubstitute, and Bogus.

Derive test classes from `TestBase` (`Headless.Testing.Tests`). Pass its `protected static CancellationToken AbortToken` to async calls, and never reference `TestContext.Current.CancellationToken` directly. [docs/llms/testing.md](docs/llms/testing.md) documents the rest of the `Headless.Testing` API, including the Testcontainers fixtures.

To add a second provider-integration project for one feature, extract a shared `*.Tests.Harness` project first instead of copying the fixture; match an existing harness project.

Messaging has a tiered suite: a fast in-process simulator tier in its unit projects, real-broker and real-database suites, and a workflow (`messaging-integration.yml`) that runs them on pull requests and after merge, adds weekly chaos runs, and reports simulator drift. Before you change a messaging package or its tests, read the tier map and the guarantee and quarantine rules in [tests/Headless.Messaging.Tests.Harness/README.md](tests/Headless.Messaging.Tests.Harness/README.md).

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
- **Record equality.** Never cache a computed member of a `record` in a `field` backing store: the cache joins the generated `Equals` and `GetHashCode` and breaks value equality. `ApiResultError.Code` and `Metadata` compute on each read for this reason.
- **Timestamp names.** Name a public or persisted instant with an `At` suffix (`CreatedAt`, `PublishedAt`) as a `DateTimeOffset`, and use an `On` suffix only for a `DateOnly` (`EffectiveOn`). Never use a `DateCreated`-style prefix.
- **Persisted enum values.** An enum with explicit backing values, such as `NullValueHandling`, keeps them: reordering or renumbering changes the meaning of stored and serialized values.
- **Clocks.** The registered `TimeProvider` is for times the app records and for schedules tests drive by advancing it. A retry back-off, reconnect, or I/O timeout waits real time: leave Polly on its default clock (setting `ResiliencePipelineBuilderBase.TimeProvider` fails RS0030), and use `ReconnectBackoff` for a reconnect loop, passing it no app clock. A pipeline that must follow the app clock suppresses RS0030 with the reason. [docs/llms/index.md](docs/llms/index.md) states the rule and its one standing exception.
- **Error codes.** `ProblemDetails` error codes use the `g:lower_snake_case` shape. To add one, change a `MessageDescriber` class, `Messages.resx`, and `Messages.ar.resx`.
- **Deliberate non-goals.** `ICache` implementations do not enforce key length. `CacheInvalidationMessage` and similar DTOs do not enforce payload size. Consuming applications own those limits, at their own boundaries and in their broker configuration. Add no enforcement here.

### Reuse before you write a utility

`Headless.Extensions` is the framework's base library, and it most likely already ships the helper you need. Before you write any general-purpose code, check [docs/llms/extensions.md](docs/llms/extensions.md). This covers string, collection, date, IO, and reflection helpers, result and error types, guards, domain primitives and value objects, pagination, constants, and validators.

- **Grep by capability, not by package name**, then read only the section you hit. The types span several namespaces (`Headless.Primitives`, `Headless.Collections`, `Headless.Threading`, `Headless.IO`). Many are extension methods that appear on BCL types in `System.*`.
- **Read the type's Design constraints before you use it.** They state the non-obvious behavior. For example, `Currency` `*` and `/` take a `decimal` scalar, the `KeyedAsyncLock` timeout overload returns `null` instead of throwing, and `ParallelForEachAsync` does not preserve order.
- **If the helper does not exist, add it** to `Headless.Extensions` or the matching foundational package instead of a local copy. Then update `docs/llms/extensions.md` as [Documentation](#documentation) describes. Update the package README only when the package's purpose or name changes.

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

| Surface | Reader | Owns |
| --- | --- | --- |
| `docs/llms/index.md` | A coding agent in a consumer app | Task routing and framework-wide invariants |
| `docs/llms/<domain>.md` and `docs/llms/<domain>/<topic>.md` | The same agent, using one domain | Package choice, setup, behavior, constraints, operations: the canonical consumer contract |
| `src/Headless.<Package>/README.md` | A NuGet visitor | Why the package exists, how to install it, links to the root README and the domain guide |
| `README.md`, `README.ar.md` | Evaluators | Why Headless, first setup, the package catalog |

Each fact lives on one surface; the others link to it. Before you add a guide, a topic file, or a package README, or edit `README.ar.md`, copy the template or follow the mirror rules in [docs/authoring/AUTHORING.md](docs/authoring/AUTHORING.md). The rest of this section holds the rules every `docs/llms` edit needs.

### Write `docs/llms` for an agent in a consumer app

The reader has the guide, the installed packages, and its own application's code. It has none of this repository's source, tests, scripts, or history, and it never changes the framework. So every sentence answers one of its questions:

- **Choose:** which package, provider, or API fits the task, and what each choice costs.
- **Wire:** what to install, register, configure, and deploy, in order.
- **Call:** how application code uses the API, with the tempting wrong call named beside the right one.
- **Observe:** what the application sees at run time: defaults, ordering, retries, exceptions, status codes, health checks, metrics, and deploy verbs.
- **Verify:** how the agent proves the wiring works.
- **Test:** how the application's own tests exercise the feature.

A sentence that answers none of them goes, or moves to this file. That covers rules for code under `src/`, analyzer policy, test-harness internals, internal type names, PR and issue numbers, and the history of a change.

- **Name the behavior, not the mechanism.** Keep one clause of why only when it changes a decision: "use `UseRedis`, because `UseInMemory` counts per process and N replicas admit N times the limit". Cut which internal tier a call takes or why the framework was built that way.
- **Document the provider contract for the families a consumer may implement:** messaging transports and storage, jobs persistence, cache and distributed-lock storage, blob storage, SQL and schema dialects, the Coordination dead-owner reclaimer, and `IProblemDetailsCreator`. Give the interface, the rules each member keeps, and the public helpers it may call. A contract that still needs an internal type is a framework defect: report it in an issue, and leave the internal type out of the guide.
- **End every setup recipe with a `**Verify:**` line** naming the observable proof: `check` or `describe` output, a readiness health check, a `resources provision` result, a log line, or a test assertion. A recipe without one says so: "**Verify:** no startup signal; the first call fails with `X` when the store is unreachable."
- **Write each section in one Diátaxis mode**: recipes are how-to, option tables and behavior lists are reference, a short concept is explanation. Address the agent as "you", condition first, one instruction per sentence, the public symbol name instead of a paraphrase, periods over semicolons and em dashes.
- **Prune for its context.** Cut what a capable model does by default and what it reads cheaply in the installed package: full member lists, dependency lists, XML doc text.
- **Keep examples free of secrets and payloads.** Metrics and dashboard examples use bounded dimensions and state the authorization boundary. Name a provider SDK type an option passes through; never invent a lossy wrapper to hide it. Never call direct transport or external side effects exactly once.

### Shape and split a guide

- **Frontmatter.** A guide starts with `domain:` and `packages:`. `packages` lists only the packages with a `## Headless.*` section in that file, and every first-party package has exactly one such section across all guides.
- **Order by the agent's decision path:** `# <Domain>` and a one-line summary, `## Orientation`, `## Agent Rules` (one exact rule per bullet, naming the right API and the tempting wrong one), concepts and decision tables, then one `## Headless.<Package>` section per package with setup, its Verify step, configuration, and runtime behavior.
- **Keep anchors stable.** Never rename a `## Headless.<Package>` heading, because package READMEs link to it. When you rename or move another heading, update every link to it in `docs/llms/`, `src/*/README.md`, `README.md`, and `README.ar.md`.
- **Split a guide past about 140 KB** into an entry file and topic files under `docs/llms/<domain>/`, each named by the task it serves. The entry keeps Orientation, Agent Rules, decision tables, the default setup with its Verify step, and a `## Topics` table whose **Read when** column names the task that triggers each file. A rule every task in the domain needs stays in the entry. Link into a topic from outside the domain by path and anchor; the index links to the entry only.
- **Keep the index a router.** One row per domain guide, routed by task, framework-wide invariants only. Domain rules stay in their guide, however important.

### Keep `docs/llms` current

Consumers and their agents read `docs/llms/`, so a package change is not done until the owning guide describes it. Update the docs in the same change when a change under `src/Headless.*` changes the public API, adds, renames, or removes a package, changes consumer-visible behavior (defaults, ordering, retry, cancellation, threading), or adds or removes an option. Internal refactors and perf-only, test-only, or formatting changes need no docs update.

| Change | Also update |
| --- | --- |
| Package added or removed | Package README, owning guide (frontmatter and `## Headless.*` section), catalog row and package count in `README.md` and `README.ar.md`, `eng/expected-packages.txt`, the index when routing changes |
| Package purpose or name | Package README, owning guide, catalog in `README.md` and `README.ar.md`, the index when routing changes |
| Setup entry point or provider member | Owning guide, the setup table in `README.md` and `README.ar.md` when the family is listed there |
| Registration, API, option, default, failure, ordering, or runtime effect | Owning guide |
| Framework-wide invariant | The index and each affected guide |

- **The installed source wins.** When a guide disagrees with the code, fix the guide in the owning file only; never patch the copy in a package README.
- **Carry `main`'s doc changes into a docs branch.** When a docs rewrite conflicts with `main`, keep the branch's structure and port each change `main` made to the old text into the file that now owns that fact. A conflict resolved by taking either side whole drops a fact.

Before you commit a docs change:

1. Search every removed or renamed public name across `docs/llms/`, package READMEs, and both root READMEs.
2. Check each changed link and anchor.
3. Confirm each frontmatter package has one matching `## Headless.*` section, and each `src/Headless.*/README.md` links to exactly one guide.
4. Compile changed C# samples. `tests/Headless.Docs.Examples.Tests.Unit` compiles every ```` ```csharp ```` block of the messaging, jobs, dashboards, and encryption guides and their topic files: run `make test-project TEST_PROJECT=tests/Headless.Docs.Examples.Tests.Unit/Headless.Docs.Examples.Tests.Unit.csproj`. Mark a block ```` ```csharp no-compile ```` only for pseudo-code, a deliberately wrong example, or one that needs another assembly; never to silence a wrong block. [AUTHORING.md](docs/authoring/AUTHORING.md#compiled-examples) covers how blocks compile and how to add a guide to the harness. Check samples in other guides against the current public API by hand.
5. Confirm `README.md` and `README.ar.md` still list the same setup rows, catalog sections, and package links in the same order.

## Pull requests

- **Fill `.github/PULL_REQUEST_TEMPLATE.md`:** a summary of one or two sentences, then Why, What changed (one Before/After row per behavior a user or caller can observe), and the sections that apply: Decisions, Left out (each item with its issue), Screenshots (every changed screen and state), Contract changes, Rollout, Review focus, and Test. Delete a section with nothing to say, and keep each heading's emoji. Test marks results ✅ passes, ❌ fails on the base, ⚠️ not verified. Keep prose outside tables and code under about 300 words (450 for a large PR). Review focus asks for the feedback you want and names where you are least sure. Test states what the tests hold on the head commit, and for a fix the test that fails without it, by behavior: never a local command, gate output, or path. Link each Review focus question to its hunk; a reviewer reads that and the hunks it points to, not every line. A public-API change shows the surface under What changed as one `diff` block per owning type. A screenshot says what it proves.
- **One PR per logical change, against `main`.** When a large mechanical part (package deletions, renames, lockfiles, generated code) would bury the behavior change, open it as its own PR against `main` and merge it first, so the behavior change reads on its own. Stack PRs only when the maintainer asks.
- **This repository is public.** A title, description, commit message, or comment never names a private repository, a client or consumer app, or a local path such as an agent's scratch folder. Write "a consumer app" and state the fact: the call, the input, and the wrong result.
