# CLAUDE.md

## What this repository is

**headless-framework** is a modular .NET 10 framework for APIs and backend services: 150+ NuGet packages grouped by domain (API, Blobs, Caching, Messaging, ORM, and more), unopinionated, with no lock-in.

Two facts drive most judgment calls here:

- **It is a framework, not an application.** Abstractions, extension points, and helpers that nothing in this repository calls are deliberate. Downstream consumers and future providers use them. Do not delete a public member because it has no local caller.
- **It is greenfield.** Prefer a simpler, cleaner API even when that breaks consumers. A breaking change that materially improves correctness or performance beats a compatibility shim, unless the request says otherwise. This covers the database too: there are no deployed schemas and no data to preserve, so change a schema in place and reset or rewrite its migrations rather than adding an upgrade path. Do not write a migration, a backfill, or a compatibility shim for a schema nobody is running.

Coverage targets: line ≥85% (floor 80%), branch ≥80% (floor 70%), mutation score ≥70% (goal 85%).

## Architecture

Each feature ships as an abstraction package plus one package per provider: `Headless.<Feature>.Abstractions` holds the interfaces, and `Headless.<Feature>.<Provider>` implements them. For example, `Headless.Caching.Abstractions` with `Headless.Caching.Redis`.

Registration follows that split. The feature's Core package owns `AddHeadless{Feature}(Action<Headless{Feature}SetupBuilder>)` plus the provider gates, and each provider package contributes `Use{Provider}` members on the builder. Read [provider setup and options](docs/solutions/conventions/provider-setup-and-options.md) before adding a registration entry point, a provider package, or an options class.

## Build and test

`make help` lists every target. Use the targets instead of raw `dotnet`; they pin configuration, results directories, and parallelism. What `make help` does not tell you:

- **Scope the command to the work.** `make build-project PROJECT=src/.../X.csproj` restores only that project graph, so prefer it over `make build` when the work sits in one project. Tests scope the same way: `make test-project TEST_PROJECT=…`, `test-class CLASS='*ClockTests'`, `test-method`, `test-namespace`, `test-trait`, `test-query`.
- **The dashboards need Node 22+ on `PATH`.** Building `Headless.Jobs.Dashboard` or `Headless.Messaging.Dashboard` runs `npm ci` and a Vite build (`eng/DashboardSpa.targets`), then embeds the generated `wwwroot/dist`, which is not committed. `make dashboards` rebuilds the SPAs. Pass `/p:BuildDashboardSpa=false` to skip the npm build when a `wwwroot/dist` is already there.
- **`make quality-fix` refuses to run unfiltered.** Pass one rule at a time in `QUALITY_DIAGNOSTICS` and run `make rebuild` between rules, because four fixers emit code that does not compile. See [dotnet format analyzer fixers](docs/solutions/tooling-decisions/dotnet-format-analyzer-fixers.md).
- **Before opening a PR**, run `make quality-analyzers` after the build, test, and format gates, and fix what it reports. Narrow a noisy run with `QUALITY_SEVERITY=warn` or `QUALITY_DIAGNOSTICS=MA0154`. The Headless SDKs turn warnings into errors in CI.
- `make test-integration` needs Docker. A fresh clone or new worktree needs `make bootstrap`.

The solution file is [headless-framework.slnx](headless-framework.slnx). CLI tools are pinned in [dotnet-tools.json](dotnet-tools.json): run `dotnet tool restore`, then `dotnet <tool>`.

## Tests

- `*.Tests.Unit` — isolated and mocked, no external dependencies.
- `*.Tests.Integration` — real dependencies through Testcontainers.
- `*.Tests.Harness` — shared fixtures, builders, and cross-provider conformance suites.

The stack is xUnit v3 on Microsoft Testing Platform, AwesomeAssertions (a FluentAssertions fork), NSubstitute, and Bogus.

Test classes derive from `TestBase` (`Headless.Testing.Tests`) and pass its `protected static CancellationToken AbortToken` to async calls. Never reference `TestContext.Current.CancellationToken` directly. [docs/llms/testing.md](docs/llms/testing.md) documents the rest of the `Headless.Testing` surface, including the Testcontainers fixtures.

Adding a second or later provider-integration project for one feature means extracting a shared harness first, rather than copying the fixture. See [Tests.Harness extraction](docs/solutions/best-practices/tests-harness-extraction.md).

## Conventions

- **File header.** Every `.cs` file starts with `// Copyright (c) Mahmoud Shaheen. All rights reserved.`
- **Argument validation.** Use `Headless.Checks` (`Argument.*`, `Ensure.*`), not `ArgumentNullException.ThrowIfNull`, `ArgumentOutOfRangeException.ThrowIfGreaterThan`, or their siblings.
- **Package versions.** Every version lives in `Directory.Packages.props`. Never put a `Version` attribute in a `.csproj`.
- **Namespaces.** The anchor is the project's `<RootNamespace>`, and sibling packages may share one family root as long as type names stay unique across them. Types live in the owning namespace, the registration surface in the family root, and helpers on foreign types in the augmented type's namespace behind a `Headless`-prefixed holder. Read the [namespace policy](docs/solutions/conventions/namespace-policy.md) before placing a type, an extension holder, or a new package's namespace.
- **Analyzer suppressions.** Put the reason inline on the pragma — `#pragma warning disable MA0045 // <why>` — never in a comment block above it, and extend an existing pragma instead of adding a second one. Suppress only for a false positive, or where the fix would make the code worse, and say which in the pragma. A false positive about a *type* rather than a line goes in `eng/analyzers/*.txt` as `[Namespace.Type]::Method`. Those files reach every project through `AdditionalFiles`. Changing a rule's severity in `.editorconfig` is a repository decision, not a code change.
- **Error codes** in `ProblemDetails` responses use the `g:lower_snake_case` shape. Adding one touches a `MessageDescriber` class plus `Messages.resx` and `Messages.ar.resx`. See [ProblemDetails error codes](docs/solutions/conventions/problem-details-error-codes.md).
- **Deliberate non-goals.** `ICache` implementations do not enforce key length, and `CacheInvalidationMessage` and similar DTOs do not enforce payload size. Consuming applications own those limits, at their own boundaries and in their broker configuration. Do not add enforcement here.

### Reuse before reinventing

Before writing any general-purpose utility — a string, collection, date, IO, or reflection helper, a result or error type, a guard, a domain primitive or value object, pagination, a constant, a validator — check [docs/llms/extensions.md](docs/llms/extensions.md). `Headless.Extensions` is the framework's base library and almost certainly already ships it.

- Search by capability, not by package name. These types span several `Headless.*` namespaces (`Headless.Primitives`, `Headless.Collections`, `Headless.Threading`, `Headless.IO`), and many are extension methods that surface on BCL types in `System.*`.
- Read the type's **Design constraints** before using it, not just to confirm it exists. They carry the non-obvious behavior: `Currency` `*` and `/` take a `decimal` scalar, `KeyedAsyncLock`'s timeout overload returns `null` instead of throwing, `ParallelForEachAsync` does not preserve order.
- If the helper genuinely does not exist, add it to `Headless.Extensions` or the matching foundational package rather than duplicating it locally, then update `docs/llms/extensions.md` per [docs/authoring/AUTHORING.md](docs/authoring/AUTHORING.md). Update the package README only when the package's purpose or name changes.

### New projects

Give every new `.csproj` one of the Headless MSBuild SDKs, not a stock `Microsoft.NET.Sdk`. [global.json](global.json) pins the versions in its `msbuild-sdks` block, so the declaration omits the version: `<Project Sdk="Headless.NET.Sdk.Web">`.

| Project type | SDK |
| --- | --- |
| Library, console app | `Headless.NET.Sdk` |
| ASP.NET Core / Web API | `Headless.NET.Sdk.Web` |
| Test project (xUnit v3, MTP) | `Headless.NET.Sdk.Test` |
| Razor class library | `Headless.NET.Sdk.Razor` |
| Blazor WebAssembly | `Headless.NET.Sdk.BlazorWebAssembly` |
| WPF / Windows Forms | `Headless.NET.Sdk.WindowsDesktop` |

Then attach the project to [headless-framework.slnx](headless-framework.slnx). These SDKs apply the strict baseline — nullable references, current analyzers, banned `Newtonsoft.Json`, deterministic builds, CI-aware warning handling — so do not disable a default without documenting why. The configuration switches and `Disable*` properties are listed at <https://raw.githubusercontent.com/xshaheen/headless-sdk/refs/heads/main/README.md>.

## Documentation

- `docs/solutions/` is the searchable store of past fixes, conventions, and decisions, filed by category (`api`, `concurrency`, `conventions`, `messaging`, and more) with YAML frontmatter (`module`, `tags`, `problem_type`). Search it before implementing, debugging, or deciding in an area it covers.
- `docs/llms/` is the canonical consumer contract: how an application chooses, wires, and calls each domain. [docs/llms/index.md](docs/llms/index.md) is a small task router; each `docs/llms/<domain>.md` file owns that domain's concepts, trade-offs, setup, and provider behavior. Read the relevant domain guide before integrating with it.
- Package READMEs are deliberately small NuGet landing pages: why the package exists, how to install it, and links to the root README and canonical domain guide. Do not copy setup or API reference into them. Read [docs/authoring/AUTHORING.md](docs/authoring/AUTHORING.md) before editing the index, a domain guide, or a package README.
- `CONCEPTS.md` holds the shared domain vocabulary: entities, named processes, and status concepts that carry a project-specific meaning.
- **Docs sync trigger.** A change under `src/Headless.*` needs a docs update when the public API surface changes, a package is added, renamed, or removed, consumer-visible behavior changes (defaults, ordering, retry, cancellation, threading), or a configuration option is added or removed. Internal refactors and perf-only, test-only, or formatting changes do not.

## Learnings

One line per durable trap, newest first. A learning that needs an investigation written out — evidence, alternatives, a resolution — is a `docs/solutions/<category>/` document instead, and this list links to it rather than summarizing it.

- `IUnitOfWorkFactory` is a singleton with no `Current`: the `IUnitOfWork` handle is the unit's only identity and propagation is keyed on the resource, never a scope: `RunAsync(db, …)` / `RunAsync(connection, …)` on a `DbContext`/`DbConnection` that already carries a live unit joins it (owner's handle, no commit), `BeginAsync`/`Enlist` on one are refused, a joined block that ends the owner's unit is refused once it returns, and any EF entry point on a context whose connection a raw-ADO unit owns is refused (EF cannot join an ADO transaction; begin the EF unit first and let the ADO code join), and a callee can also read the unit via `db.UnitOfWork()`, `connection.UnitOfWork()` (EF binds its context's connection too, so Dapper-style helpers join), or `ConsumeContext.UnitOfWork`. No child views. Enlistment is the receiver, for both domains: `unit.Outbox` and `unit.Jobs` / `unit.TimeJobs<T>()` / `unit.CronJobs<T>()` always write inside the unit's transaction and refuse when it cannot host the write; the injected `IBus`/`IQueue`/`IJobScheduler`/managers are autonomous singletons that never enlist, and `TransactionEnlistment.Required` only makes those autonomous receivers throw. Enlisted writes call `PreventRetry()` only into an observed-mode unit (the EF save pipeline's own save, which replays without re-running domain-event handlers), skipped for the EF integration-event dispatcher's own publishes; a write issued directly inside your own `RunAsync(db, …)` block leaves it replayable, and a caller-owned `SaveChangesAsync` that dispatched events marks the block itself, because its clear leaves a replay nothing to re-dispatch. A binding evicts an *owned* unit whose transaction ended behind its back (a transaction disposed by hand, a pooled context reset): it is abandoned and the next entry point begins fresh, so a `RunAsync` never runs writes on a dead transaction and reports success. `IUnitOfWorkFeature` services must be singletons (the factory resolves them from the host container). Full contract: [docs/llms/unit-of-work.md](docs/llms/unit-of-work.md). (2026-09-20)
- ASP.NET Core link generation drops a leading `{tenant}` route segment for `Url.Action` / `GetPathByAction` / `GetPathByName` because the endpoint-name scheme passes null ambient values; `AddRouteSource` wraps `LinkGenerator` and falls back to `Request.RouteValues` to keep the segment, at the cost of `?tenant=` on links to routes without it. Host-matching regexes need `RegexOptions.CultureInvariant` alongside `IgnoreCase`, or literal labels containing `i` stop matching under tr-TR. (2026-09-17)
- `main` requires only the `CI status` gate job in `ci.yml`, so add every new CI job to its `needs`. A skipped job passes. Pack and SBOM, the Africa/Cairo unit-test leg, and the messaging and R2 conformance legs run only for a published release or a `workflow_dispatch` with `release_checks`; rehearse that path before tagging a change that touches packaging or time handling. The test build skips analyzers (`-p:RunAnalyzers=false`), `Lint · .NET analyzers` is the analyzer gate, and pull requests collect no coverage. (2026-09-11)
- Messaging graceful retry release must fence the exact store-returned `(row, lane, Owner, LockedUntil)` generation: quiesce pickup before dispatcher drain, release only completed or pre-execution-abandoned attempts, and retain running or crashed leases for normal expiry. (2026-08-05)
- ApiResult is the value-based counterpart to the API exception path: conflict and authorization errors preserve `ErrorDescriptor` data, validation preserves field-keyed descriptors, validation-only aggregates map to 422, and Minimal API result wrappers publish the full response set as endpoint metadata. (2026-07-27)
- Cache events are best-effort observability: preserve `AsyncEvent<T>` handlers, capture the copy-on-write handler array at emission, and feed one lazy bounded FIFO shared by the cache root and tier hubs; producers never block, accepted signals retain FIFO, and a full buffer drops the incoming signal. (2026-07-25)
- CI runs unit tests only (`make ci-test`), so no integration suite gates a merge and a semantics change can break provider-integration tests unnoticed for weeks. A Redis membership test contradicted the #643 heartbeat contract that way. Run the affected `*.Tests.Integration` projects locally when you touch provider behavior. (2026-07-21)
- PostgreSQL materializes `DateTime` at microsecond granularity while SQL Server `datetime2(7)` keeps ticks; conformance asserts on round-tripped values must use `BeCloseTo(1µs)` (messaging-harness precedent). Exact `.Be()` may still pass nondeterministically when the read hits the EF identity map instead of a fresh context — passing once proves nothing. (2026-07-21)
- Broker conformance fixtures need protocol-specific anti-flake controls: Kafka container reuse stays disabled because stale log readiness can false-pass, while Pulsar.Client requires a trailing-slash-free broker URL, an explicit short negative-ack delay in tests, and cancellation of the in-flight receive when pausing. (2026-07-16)
- Workflow triggers and `main` branch protection must change together: removing the CodeQL `pull_request` trigger while `Analyze (csharp)` stays required leaves every PR waiting forever. Package publication is gated on a published GitHub Release. Release Drafter updates the draft and never publishes it. (2026-07-16)
- Jobs generated registration freezes once only after the `AddHeadlessJobs` options callback has loaded every `AddJobsDiscovery` assembly; public descriptors remain configuration-independent, while all live runtime and Dashboard reads must use the immutable per-`IHost` registry. (2026-07-15)
- Messaging retry ceilings require an atomic durable attempt reservation before transport or consumer invocation; persisting progress only after failure lets a crash reset an inline burst. Recovery of a consumed reservation advances Messaging-owned persisted state without applying domain exception classification. (2026-07-10)
- Jobs retry recovery depends on carrying `RetryCount` through every EF and in-memory pickup projection and persisting it before Polly waits; omitting it from a projection silently restores a fresh retry budget after process restart. (2026-07-10)
- A green MTP run is not a clean build: `dotnet test --project` compiles and runs test code that `dotnet build` rejects with an analyzer error, as seen with AsyncFixer04. Verify a changed project with `dotnet build -c Release -v:minimal` before CI. (2026-07-10)
- `[JsonExtensionData]` properties must be `{ get; set; }` (never `init`) and every source-gen `JsonSerializerContext` whose models carry extension data needs `[JsonSerializable(typeof(object))]` + `[JsonSerializable(typeof(JsonElement))]`. `init` binding throws on EVERY deserialization; missing object metadata throws on any unknown response field — both at runtime only, build stays green. Found via Paymob CashOut/CashIn unit tests. (2026-07-07)
- Kafka concurrent consumers must commit offsets by per-partition contiguous completed watermark; committing a high completed offset directly can acknowledge lower in-flight messages and lose them after a crash. (2026-07-06)
- `Range<T>` uses `null` bounds as infinities; range-to-range operations must compare lower and upper bounds with side-specific semantics instead of reusing value containment. (2026-07-04)
