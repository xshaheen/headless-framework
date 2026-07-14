---
title: "Startup validation gates: two-tier (correctness vs diagnostic) with Off/Warn/Strict mode and environment-aware defaults"
date: 2026-06-13
category: architecture-patterns
module: Hosting / startup validation (cross-cutting)
problem_type: architecture_pattern
component: service_class
severity: medium
applies_when:
  - Adding a host-startup gate that validates configuration and warns or throws
  - Deciding whether a startup check should fail-fast in production or only in development
  - Generalizing an ad-hoc per-package validation knob into a shared vocabulary
related_components:
  - background_job
  - testing_framework
tags:
  - startup-validation
  - options-validation
  - validateonstart
  - ihostedlifecycleservice
  - ihostenvironment
  - fail-fast
  - diagnostic-probe
  - hosting
---

# Startup validation gates: two-tier (correctness vs diagnostic) with Off/Warn/Strict mode and environment-aware defaults

## Context

The framework already validates a lot at host startup, but each package invented its own gate shape. Today there are **four incompatible shapes** and **no environment-awareness anywhere** (`grep IsDevelopment src/` returns hits, none of them a startup gate):

| Shape | Real example | Behavior |
| --- | --- | --- |
| Always-throw, no knob | `FeaturesEntityValidationStartupGate<TContext>` (`Headless.Features.Storage.EntityFramework/Internal/`) | Throws `InvalidOperationException` if a required EF entity type is absent from the model. No opt-out. |
| Per-gate `bool`, throw-when-on | `HeadlessServiceDefaultsValidationStartupFilter` (`Headless.Api.ServiceDefaults/`) | Reads `RequireUseHeadless` / `RequireMapHeadlessEndpoints` / `RequireStatusCodesRewriter` (all default `true`); throws if wiring was skipped. Two-state per gate. |
| Collect-then-throw + warn-log | `HeadlessTenancyStartupValidator` (`Headless.MultiTenancy/`) | Aggregates `IHeadlessTenancyValidator` diagnostics, throws if any `Error`, logs `Warning`/`Information`. Warn-vs-strict is baked into each diagnostic's severity, not operator-configurable. |
| **Real 3-state mode — but only one package has it** | `SqlServerCommitDiagnosticHostedService` (`Headless.CommitCoordination.SqlServer/`) | `SqlServerCommitDiagnosticProbeMode` = `Disabled \| Warn \| Strict`. The only gate with an explicit graduated knob. |

This scatter creates three frictions:

1. **No shared mode vocabulary.** Three packages, three escape hatches (a `bool`, a severity enum, a mode enum). A consumer learns each gate's opt-out separately.
2. **No environment-awareness.** Every gate's strictness is fixed across Dev and Prod. The SqlServer probe defaults to `Warn` *in production* — paying connection + transaction latency on every prod boot for a check that is really a development/CI concern.
3. **Cheap correctness checks and expensive network probes share one mental model.** Skipping the EF-entity check (cheap, catches a real misconfig) is categorically more dangerous than skipping the SqlClient diagnostic probe (expensive, catches a library-compat regression) — yet both look like "turn the gate off." One footgun knob for two risk classes.

This doc names the latent pattern across those instances and proposes the one missing piece: **tier the gates and resolve their defaults from `IHostEnvironment`.**

> **Net-new vs. existing — read before implementing.** The 3-state `Disabled/Warn/Strict` *shape* already ships and is tested in the SqlServer probe. The **environment-aware default** does **not** exist anywhere in the codebase today and was never discussed in prior design sessions (session history) — it is a genuine generalization, not a refactor of existing behavior. Do not describe env-defaulting as current behavior.

## Guidance

### The two tiers

| Tier | Definition | Default policy | Real gates |
| --- | --- | --- | --- |
| **Tier-1 — Correctness** | cheap, **no network I/O**: in-memory checks of options, EF model metadata, DI wiring flags, tenant posture | `Strict` in **all** environments | `FeaturesEntityValidationStartupGate<TContext>` (EF model-metadata only); `HeadlessServiceDefaultsValidationStartupFilter` (flag checks); `HeadlessTenancyStartupValidator` (Tier-1 *by convention* — see edge case); options `ValidateOnStart()` chains; `UseDefaultServiceProvider(ValidateOnBuild/ValidateScopes)` |
| **Tier-2 — Diagnostic** | network I/O, adds startup latency, can fail on transient blips | `Strict`/`Warn` in Dev, **`Off` in Prod** | `SqlServerCommitDiagnosticHostedService` (opens a live `SqlConnection`, runs a real txn under `DiagnosticProbeTimeout`) — currently the only one |

The test for tier is **I/O at runtime, not object allocation.** `FeaturesEntityValidationStartupGate` opens a `DbContext` via `IDbContextFactory` but only reads `context.Model.FindEntityType(...)` — in-memory model metadata, no DB round-trip — so it is correctly Tier-1 despite "creating" a context.

### Shared 3-state mode enum

Generalize the existing `SqlServerCommitDiagnosticProbeMode` into a shared enum in `Headless.Hosting`, renaming `Disabled` → `Off` for a consistent vocabulary:

```csharp
// proposed: Headless.Hosting (shared)
[PublicAPI]
public enum HeadlessValidationMode
{
    /// <summary>Skip the gate entirely.</summary>
    Off = 0,
    /// <summary>On failure, log and continue (record degraded state).</summary>
    Warn = 1,
    /// <summary>On failure, throw and fail host startup.</summary>
    Strict = 2,
}
```

> The rename `Disabled` → `Off` touches a `[PublicAPI]` enum — a breaking change, acceptable under this greenfield repo's "prefer clean APIs over compatibility layers" stance, but call it out in the PR.

> **Open decision — where the shared enum lives.** Putting `HeadlessValidationMode` in `Headless.Hosting` forces any Tier-2 provider that adopts it (e.g. `Headless.CommitCoordination.SqlServer`) to reference `Headless.Hosting` — which the rejected validator finding (see below) deliberately avoided: *"a Tier-2 package isn't obligated to take a Hosting dependency."* Resolve this before implementing: either place the enum in a low-level neutral package (e.g. `Headless.Abstractions`) so no provider gains a Hosting dependency, or accept the dependency. Only the enum needs the neutral home — the `ResolveValidationMode` helper below (which needs `IHostEnvironment`) can still live in `Headless.Hosting`.

### Canonical gate shape: resolve → short-circuit → check → branch

Lifted from `SqlServerCommitDiagnosticHostedService._RunProbeAsync`:

```csharp
if (Interlocked.Exchange(ref _probeRan, 1) == 1) return; // run-once guard for IHostedService gates

var mode = ResolveMode(options, hostEnvironment);   // env-aware default + explicit override
if (mode == HeadlessValidationMode.Off) { state.MarkSkipped("..."); return; }

var result = await RunCheckAsync(ct).ConfigureAwait(false);
if (result.Succeeded) { state.MarkSucceeded(result.Message); return; }

if (mode == HeadlessValidationMode.Strict)
{
    LogGateFailedStrict(logger, result.Exception, result.Message);
    throw new InvalidOperationException(result.Message, result.Exception);
}

LogGateDegraded(logger, result.Exception, result.Message); // Warn: log, don't throw
```

The `Interlocked.Exchange` run-once guard matters for any gate hosted as `IHostedService` (whose `StartAsync` can fire more than once); preserve it in the generalized shape.

### Environment-aware default resolution

`Headless.Hosting/Environments/HostEnvironmentExtensions.cs` already exposes `IsTest()` and `IsDevelopmentOrTest()` as C# 14 extension members. Prefer `IsDevelopmentOrTest()` as the pivot so **test hosts inherit the dev policy** (the framework already favors that combined check elsewhere).

The resolution is a one-line ternary — exactly what the real probe inlines (see the Tier-2 example). The only rule is *explicit operator value wins; the env default fills the unset (`null`) case*:

```csharp
var mode = explicitMode ?? (env.IsDevelopmentOrTest() ? devDefault : prodDefault);
```

The dev/prod defaults are fixed per tier, so carry them on the tier rather than re-typing them at each gate — as an extension member next to the existing env helpers:

```csharp
// extension member on HostEnvironmentExtensions
public HeadlessValidationMode ResolveValidationMode(HeadlessValidationMode? explicitMode, ValidationTier tier)
    => explicitMode ?? tier switch
    {
        ValidationTier.Correctness => HeadlessValidationMode.Strict,                                    // Strict everywhere
        ValidationTier.Diagnostic  => IsDevelopmentOrTest() ? HeadlessValidationMode.Strict : HeadlessValidationMode.Off,
        _                          => HeadlessValidationMode.Strict,
    };
```

(`ValidationTier` is a two-value enum — `Correctness`, `Diagnostic` — mirroring the tier table.) This makes the tier table above the literal source of the switch arms, and keeps the policy in one place instead of being re-typed at every call site (the "no shared vocabulary" friction this pattern is arguing against). A standalone (non-extension) helper would carry `Argument.IsNotNull(env)` per the `Headless.Checks` convention; the extension-member receiver covers it. If implemented, unit-test the resolution: explicit mode wins; `null` + Development → dev default; `null` + Production → prod default; `null` + Test → dev default (test inherits dev policy).

### Alignment with repo conventions

- **Options validators.** Companion knobs (timeouts, sizes) keep an `internal sealed class {Options}Validator : AbstractValidator<{Options}>` in the same file, registered via `services.AddOptions<TOptions, TValidator>()` / `Configure<TOption, TValidator>(...)` from `Headless.Hosting`. Real example: `SqlServerCommitCoordinationOptionsValidator` enforces `DiagnosticProbeTimeout > TimeSpan.Zero` — itself a Tier-1 rule running via `ValidateOnStart()`. The mode enum needs no validator (every value is valid); its companion timeout does.
- **`LoggerMessage` partials at file bottom.** Use the bottom-of-file `internal static partial class {Gate}Logger` form (as `HeadlessTenancyStartupValidator` does) when logging methods are shared.
- **`g:snake_case` problem-detail codes.** Today's gates throw raw `InvalidOperationException` with a human message and **no `g:` code** — they fail *startup*, not a *request*, so no `ProblemDetails` is produced. If a generalized Strict failure ever surfaces a structured code, use the `g:lower_snake_case` shape (e.g. `g:startup_gate_failed`). It is not done today; do not assume codes exist.

### A note on `ValidateOnStart` for diagnostic options (settled)

A prior review (session history) flagged `SqlServerCommitCoordinationOptions` for lacking a FluentValidation validator + `ValidateOnStart`, and the finding was **rejected**: the SqlServer package doesn't reference `Headless.Hosting`/FluentValidation, all probe options carry safe defaults, and CLAUDE.md's "validate only when needed" qualifier applies. Don't re-add that validator on the strength of this pattern alone — the mode enum is self-validating, and a Tier-2 package isn't obligated to take a Hosting dependency just for it.

## Why This Matters

- **Disabling Tier-1 in prod = bad config serves live traffic.** Skip `FeaturesEntityValidationStartupGate` and a DbContext missing `FeatureValueRecord` boots fine, then faults at the first feature read — a runtime failure under load instead of a startup failure. `RequireUseHeadless = false` lets an app start without the Headless middleware pipeline. These are deterministic, cheap-to-detect misconfigurations with no upside to deferring → Tier-1 stays `Strict` even in prod.
- **Disabling Tier-2 in prod avoids two distinct prod hazards:** (1) **startup latency** — the SqlServer probe opens a connection and runs a transaction (bounded by a default 5 s timeout) on every boot; (2) **transient-blip startup failures** — a momentary DB hiccup during a rolling deploy would, under `Strict`, abort the new instance's startup and stall the rollout. The probe verifies a *library-compatibility regression* (SqlClient still emitting the diagnostic payloads out-of-band commit detection relies on) — a dev/CI concern, not a per-boot prod concern. Hence `Off` in prod.
- **The existing SqlServer 3-mode gate proves the shape works.** The `Off`-short-circuit / `Warn`-log / `Strict`-throw branch already ships and is tested. The generalization is "lift this one good shape into a shared vocabulary and add the env-default it's missing" — not invent a mechanism. The choice of a 3-state enum over a `bool` was deliberate (session history): `Warn` is the safe default because the SqlServer provider is an *acceleration* path and a missed signal degrades to polling rather than violating correctness.

## When to Apply

When a package adds **any** host-startup gate, run this decision procedure:

1. **Does the check touch the network / external process / disk, or add measurable boot latency?**
   - **No** (options, in-memory EF model metadata, DI wiring flags, config-shaped posture) → **Tier-1 Correctness.** Default `Strict` everywhere. Prefer `ValidateOnStart()` for options, or `IHostedLifecycleService.StartingAsync` for cross-cutting checks that must run *before* other hosted services.
   - **Yes** (opens a connection, calls a remote service, probes a live transaction) → **Tier-2 Diagnostic.** Default `Strict`/`Warn` in Dev, `Off` in Prod via `IHostEnvironment`. Always bound it with a timeout option (validated `> 0`).
2. **Edge — "creates a DbContext but does no I/O"** (`FindEntityType`): still Tier-1. The test is *runtime I/O*, not allocation.
3. **Edge — aggregating injected validators** (the tenancy case): tier follows what the validators actually do. If any registered validator does I/O, the host service is effectively Tier-2 — document the expectation that validators stay cheap.
4. **Run-ordering.** If a misconfiguration must block *other* hosted services from starting (e.g. tenant posture gating background consumers), implement `IHostedLifecycleService` — its `StartingAsync` runs before any `IHostedService.StartAsync`. Plain `IHostedService.StartAsync` (the SqlServer probe's choice) interleaves with other services and is fine for self-contained diagnostics.

## Examples

### Tier-1 stays always-throw — don't add an `Off` switch (real, `FeaturesEntityValidationStartupGate.cs`)

```csharp
private static void _EnsureEntity(DbContext context, Type entityType, string entityName)
{
    if (context.Model.FindEntityType(entityType) is not null) return;
    throw new InvalidOperationException(/* "...does not contain X. Call AddHeadlessFeatures..." */);
}
```

Correct as-is: skipping a correctness check is never the right answer, so the generalization for Tier-1 is mostly *not* exposing an `Off` switch.

### Tier-1 stays binary — the `bool` opt-out is the right shape (real, `HeadlessServiceDefaultsValidationStartupFilter.cs`)

```csharp
if (options.Validation.RequireUseHeadless && !options.UseHeadlessCalled)
    throw new InvalidOperationException("Call UseHeadless before the application starts.");
```

A per-gate `bool` (default `true`) is correct for Tier-1: the only states that make sense are *enforce* (`Strict`) and *opt-out* (`Off`) — there is no `Warn` middle ground for a correctness check. "Log that the pipeline is mis-wired, then serve traffic anyway" is exactly the footgun the tiering exists to prevent. So `Warn` is a Tier-2-only state by convention: a Tier-1 gate that adopts `HeadlessValidationMode` for uniform vocabulary simply never resolves `Warn` (the `Correctness` arm of `ResolveValidationMode` always yields `Strict`), and an existing `bool` opt-out is just the `Off`/`Strict` pair under a different name — no need to migrate it.

### The user's `UseDefaultServiceProvider` snippet is a canonical Tier-1 gate (real, `Headless.Api.ServiceDefaults/Setup.cs`)

```csharp
if (options.Validation.ValidateServiceProviderOnStartup) // default: true
{
    builder.Host.UseDefaultServiceProvider(serviceProviderOptions =>
    {
        serviceProviderOptions.ValidateOnBuild = true;   // unresolvable deps fail at build
        serviceProviderOptions.ValidateScopes = true;    // captive-dependency bugs caught
    });
}
```

Both checks are cheap, deterministic, and `true` in all envs. A single `bool` is acceptable here precisely *because* it is Tier-1 — there is no meaningful "warn" for a container that won't build.

### Options `ValidateOnStart()` is the framework's reference Tier-1 pipeline (real, `Headless.Hosting/Options/HeadlessOptionsServiceCollectionExtensions.cs`)

```csharp
return services
    .AddOptionValidator<TOptions, TValidator>()
    .AddOptions<TOptions>(optionName)
    ._ValidateFunc(validation)
    .ValidateFluentValidation()   // FluentValidationValidateOptions<TOptions>
    .ValidateOnStart();           // fail-fast at host start, every environment
```

Treat options validation as the *reference* Tier-1 implementation rather than reinventing it. New Tier-1 gates that aren't expressible as an options validator (EF model presence, wiring flags) are the ones that need the `IHostedLifecycleService` shape above.

### Tier-2 after — mode-aware, env-defaulted (generalized from `SqlServerCommitDiagnosticHostedService._RunProbeAsync`)

```csharp
// BEFORE: var mode = _options.Value.DiagnosticProbeMode;  // unconditional, defaults Warn (even in prod)
// AFTER:  nullable mode + env-resolved Tier-2 default (Off in prod, Strict in dev/test)
var mode = _options.Value.DiagnosticProbeMode
    ?? (_hostEnvironment.IsDevelopmentOrTest()
            ? HeadlessValidationMode.Strict
            : HeadlessValidationMode.Off);

if (mode == HeadlessValidationMode.Off) { _probeState.MarkSkipped("..."); return; }

var result = await _probe.ProbeAsync(ct).ConfigureAwait(false);
if (result.Succeeded) { _probeState.MarkSucceeded(result.Message); return; }

if (mode == HeadlessValidationMode.Strict)
{
    _probeState.MarkFailed(result.Message, result.Exception);
    LogDiagnosticProbeFailedStrict(_logger, result.Exception, result.Message);
    throw new InvalidOperationException(result.Message, result.Exception);
}

_probeState.MarkDegraded(result.Message, result.Exception);
LogDiagnosticProbeDegraded(_logger, result.Exception, result.Message);
```

The only deltas from shipping code: `DiagnosticProbeMode` becomes `HeadlessValidationMode?` (so "unset" means "use env default") and the default flips from a flat `Warn` to `Off`-in-prod. The branch body is unchanged — that is the proof the shape already works.

> The snippet above is only the *run path*. A real Tier-2 gate that opens connections also needs the teardown the shipping SqlServer service has — `StopAsync`/`DisposeAsync` (mirrored by a synchronous `Dispose`) that drain in-flight work, using `CancellationToken.None` on the disposal path so a cancelled host shutdown still flushes. Don't copy just the probe body.

### Tier-2 exception — a correctness-adjacent gate stays `Warn` in all envs (real, `CommitInterceptorStartupGate.cs`)

`CommitInterceptorStartupGate<TContext>` opens a real transaction at boot (an empty commit through the context's execution strategy), so by the I/O test it is **Tier-2**. Yet it deliberately defaults to a flat `Warn` in **all** environments — *not* the Tier-2 `Off`-in-prod default above — and it is correct to do so. Two properties separate it from the SqlServer diagnostic:

- **What it verifies has production value.** The SqlServer diagnostic checks a *library-compatibility regression* (SqlClient still emitting the out-of-band payloads), a dev/CI concern with no per-boot prod relevance → `Off` in prod. This gate checks that the **transactional outbox is actually wired** for the consumer's `DbContext`; a mis-wire in production silently drops publish/commit atomicity. That is exactly a prod-relevant signal, so suppressing it in prod would hide the failure where it matters most.
- **`Warn` carries no rollout hazard.** The "transient-blip aborts the rolling deploy" hazard that drives Tier-2 to `Off`-in-prod only exists under `Strict`. This gate's default is `Warn` (log + continue) and its probe treats any infra failure — unreachable DB, retrying-strategy rejection, unresolvable context — as *inconclusive* and lets the host start. The only residual prod cost is one empty-commit round-trip at boot, which is negligible.

So the tier taxonomy classifies by *cost/IO*, but the env-default is ultimately a function of **whether the check is correctness-adjacent**. Tier-2 gates whose signal has prod value (outbox atomicity here) may legitimately run `Warn`-everywhere; reserve `Off`-in-prod for purely dev/CI diagnostics. Do not "correct" this gate to `Off`-in-prod to match the generic Tier-2 rule.

## Related

- [`best-practices/storage-initializer-lifecycle-correctness.md`](../best-practices/storage-initializer-lifecycle-correctness.md) — concrete Tier-1 correctness-gate instances (the EF `*ValidationStartupGate` files) and the fail-closed-on-misconfig rationale. **Primary sibling.**
- [`concurrency/startup-pause-gating-and-half-open-recovery.md`](../concurrency/startup-pause-gating-and-half-open-recovery.md) — "validate cross-option invariants in the FluentValidation validator; don't silently redefine invalid config as primary behavior" — the Tier-1 source-of-truth rule.
- [`architecture-patterns/unified-provider-setup-builder-pattern.md`](unified-provider-setup-builder-pattern.md) — where gates attach to the host (`Setup{Feature}` registration + `IHostedLifecycleService.StartingAsync`).
- [`architecture-patterns/coordination-register-establishes-durable-liveness.md`](coordination-register-establishes-durable-liveness.md) — `JobsCoordinationStartupGate` is another real correctness-gate example.
</content>
</invoke>
