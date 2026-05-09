---
title: "feat: Mediator TenantRequiredBehavior + [AllowMissingTenant] attribute"
type: feat
status: completed
date: 2026-05-09
origin: docs/brainstorms/2026-05-09-mediator-tenant-required-behavior-requirements.md
---

# feat: Mediator `TenantRequiredBehavior` + `[AllowMissingTenant]` attribute

## Summary

Ship a new `Headless.Mediator` package containing an `IPipelineBehavior` that enforces ambient tenant context at the Mediator request boundary, plus an opt-out marker attribute. Type-load attribute caching keeps the per-request cost to a single field read. Reuses the existing `MissingTenantContextException` (in `Headless.Core/Abstractions`) so the already-shipped HTTP 400 handler (#237) catches Mediator-boundary failures without further wiring.

---

## Problem Frame

Headless consumers using Mediator + multi-tenancy hand-roll the same primitive (zad-ngo: 95 request types tagged across Console / Public / Authenticated / Notifications dispatch surfaces). The framework's existing Mediator behaviors live in `src/Headless.Api/Mediator/` and are HTTP-coupled via `IRequestContext`. A tenancy guard must work for non-HTTP consumers (background workers, console hosts) too, so it cannot land there. (See origin: [docs/brainstorms/2026-05-09-mediator-tenant-required-behavior-requirements.md](../brainstorms/2026-05-09-mediator-tenant-required-behavior-requirements.md))

---

## Requirements

- R1. Ship new `Headless.Mediator` package (HTTP-agnostic Mediator opinions; references `Headless.Core` + `Mediator.Abstractions`)
- R2. `AllowMissingTenantAttribute` — sealed, `AttributeTargets.Class | Struct`, `Inherited=false`, `AllowMultiple=false`, no ctor params (origin R2)
- R3. `TenantRequiredBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>` — sealed, ctor takes `ICurrentTenant` only (origin R3)
- R4. Attribute presence evaluated **once per closed generic** via `static readonly bool _AllowMissingTenant = Attribute.IsDefined(typeof(TRequest), typeof(AllowMissingTenantAttribute), inherit: false);` (origin R4)
- R5. Throws `MissingTenantContextException` (reused from `Headless.Core/Abstractions`, no new exception type) when `_AllowMissingTenant == false` AND tenant is null/whitespace; sets `ex.Data["Headless.Mediator.FailureCode"] = "MissingTenantContext"` (origin R5, R6)
- R6. `services.AddTenantRequiredBehavior()` registration extension — no options, idempotent, registers the open-generic exactly once (origin R7)
- R7. `[AllowMissingTenant]` is the only enrollment surface; no runtime opt-out (origin R8)
- R8. Pipeline ordering documented (Auth → TenantRequired → Idempotency, per zad-ngo precedent), not enforced (origin R9)
- R9. Update `docs/llms/multi-tenancy.md` — add a Mediator-boundary section. (`docs/multi-tenancy.md` does not exist; the `llms.txt` root index already references this file, so no index update is needed.)
- R10. Coverage targets per `CLAUDE.md`: ≥85% line, ≥80% branch on the new package

---

## Scope Boundaries

- EF-direct write guard (companion issue #234) — separate plan
- Migrating `Headless.Api/Mediator/` HTTP-coupled behaviors into `Headless.Mediator` — separate, larger refactor
- Splitting `MissingTenantContextException` into per-layer derived types — resolved as "single type" in brainstorm
- Runtime / dynamic opt-out (`IOptions`, per-handler override, policy seam)
- Source-generator opt-in or bulk-tagging tooling for downstream consumers
- Pipeline-position enforcement (no validation that the behavior is registered between Auth and Idempotency)
- HTTP 400 mapping (`TenantContextExceptionHandler` shipped in #237)
- Demo project for `Headless.Mediator` (`demo/` directory not seeded; package is exercised via tests)
- Streaming-request support (`Mediator`'s `IStreamRequest` / `IStreamPipelineBehavior`). The repo currently has zero streaming usage; add a streaming variant only if a consumer surfaces a need.

---

## Context & Research

### Relevant Code and Patterns

- `src/Headless.Api/Mediator/ApiCriticalRequestLoggingBehavior.cs` — canonical `IPipelineBehavior<TMessage, TResponse>` implementation for this repo (signature, `ValueTask<TResponse> Handle(...)`, `MessageHandlerDelegate<,>` next-delegate). Mirror this shape; do **not** mirror the `IRequestContext` dependency.
- `src/Headless.Api/Mediator/ApiValidationRequestPreProcessor.cs` — adjacent behavior using `MessagePreProcessor<,>` (different lifecycle hook; not the model for U3, but useful as a structure reference).
- `src/Headless.FluentValidation/` — closest analog for a flat cross-cutting package (no `Abstractions` sibling): minimal csproj, `RootNamespace`, single `PackageReference`, package-root `README.md`. Slnx folder is `/Validations/`.
- `src/Headless.Core/Abstractions/ICurrentTenant.cs` — the contract this behavior depends on. `IsAvailable` returns `Id is not null` (does not guard whitespace); the behavior must guard whitespace itself.
- `src/Headless.Core/Abstractions/MissingTenantContextException.cs` — exception type; default message documents the remediation. The XML-doc remarks block already documents the `Data` failure-code convention as cross-layer.
- `tests/Headless.Checks.Tests.Unit/` and `tests/Headless.FluentValidation.Tests.Unit/` — flat-layout test project precedent. `tests/Directory.Build.props` already pulls in `xunit.v3`, `AwesomeAssertions`, `NSubstitute`, `Bogus` as global usings.
- `headless-framework.slnx` — XML solution-folder format. Folders for cross-cutting flat packages: `/Validations/` (FluentValidation), `/Kernel/` (Extensions, Core, Checks). Add `/Mediator/` as a peer.

### Institutional Learnings

- `docs/brainstorms/2026-05-01-tenant-id-envelope-requirements.md` (shipped as #228/#239) — same `MissingTenantContextException` chosen as the cross-layer signal there. Confirms the single-exception decision is consistent with the framework's recent direction.
- `Headless.Api/Setup.cs` shows the modern extension-block style (`extension(WebApplicationBuilder builder) { ... }`); registration extensions in this repo are lower-ceremony than I expected — the `Headless.FluentValidation` package ships **no** `Setup.cs` because it has nothing to register. `Headless.Mediator` does need one (the open-generic behavior).

### External References

None gathered. Pattern is fully covered by local references; `Mediator.Abstractions 3.0.2` is already in use (`src/Headless.Api/Mediator/`).

---

## Key Technical Decisions

- **Doc strategy: update `docs/llms/multi-tenancy.md` only.** Verification during planning revealed `docs/` contains no top-level `.md` files — `docs/llms/multi-tenancy.md` (220 lines, exists) is the single canonical tenancy doc, indexed from root `llms.txt`, and it already references this issue (#236) in the failure-mapping section. The brainstorm's "update both" framing was based on a verification miss; collapsed here to a single doc update. No `llms.txt` change needed.
- **Whitespace tenant treated as missing.** Origin flagged "null/empty" vs. "null/whitespace" as open. The behavior uses `string.IsNullOrWhiteSpace(currentTenant.Id)` to match the spirit of "ambient tenant is missing" and avoid surprising consumers who set `Id = ""` defensively.
- **`AddTenantRequiredBehavior()` is an `IServiceCollection` extension, not a `MediatorBuilder` hook.** `Mediator.SourceGenerator` registers `IPipelineBehavior<,>` open-generics on `IServiceCollection` directly (used the same way in `Headless.Api/Setup.cs`'s call sites). No builder seam needed; keeps the surface minimal.
- **Idempotent registration via `TryAddEnumerable`.** `services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IPipelineBehavior<,>), typeof(TenantRequiredBehavior<,>)))` ensures a second call doesn't double-register the open-generic.
- **Behavior lifetime: transient.** Matches the existing `Headless.Api/Mediator/` behaviors. The behavior holds no state and constructs cheaply.
- **`ICurrentTenant` is a precondition, not a co-registration.** `Headless.Mediator` does not register `ICurrentTenant`; the consumer's tenancy bootstrap (e.g., `Headless.Api`'s `MultiTenancySetup`) owns that. README documents the precondition.
- **Slnx folder: new `/Mediator/`.** Mirrors `/Validations/` shape (flat package, no `Abstractions` sibling). Add the `<Folder>` block by hand-editing `headless-framework.slnx`; `dotnet sln add` does not fully cover slnx.
- **No `Setup.cs` rename.** `Headless.FluentValidation` ships no Setup; this package ships one (the registration extension). Name the file `MediatorSetup.cs` to match `ApiSetup.cs` / `MessagingSetup.cs` convention.

---

## Open Questions

### Resolved During Planning

- Doc landing path → `docs/llms/multi-tenancy.md` only. (User selected "Update both" earlier in planning, but verification surfaced that `docs/multi-tenancy.md` does not exist — `docs/llms/multi-tenancy.md` is the single canonical doc, and it already references #236. Single-doc update is the only viable shape.)
- Whitespace tenant treatment → **treated as missing** (`string.IsNullOrWhiteSpace`).
- Slnx folder name → `/Mediator/`.
- Registration seam → `IServiceCollection` extension with `TryAddEnumerable`.
- New exception type? → no, reuse `MissingTenantContextException` from `Headless.Core/Abstractions` (origin R5/R6).

### Deferred to Implementation

- Caching invariant test technique — call-counting fake `ICurrentTenant`, vs. reflection on the `static readonly` field, vs. covering it implicitly via behavior tests. Pick during U4 implementation; the directional intent is "verify per-closed-generic single-evaluation."
- Exact phrasing of the pipeline-ordering guideline in the README — write during U5 once the surface is final.
- Whether `Headless.Mediator` needs an `internal sealed` cached delegate to avoid the closure allocation in the throw path. Defer; profile if it matters.

---

## Output Structure

    src/Headless.Mediator/
        Headless.Mediator.csproj
        AllowMissingTenantAttribute.cs
        TenantRequiredBehavior.cs
        MediatorSetup.cs
        README.md

    tests/Headless.Mediator.Tests.Unit/
        Headless.Mediator.Tests.Unit.csproj
        TenantRequiredBehaviorTests.cs
        AllowMissingTenantAttributeTests.cs
        MediatorSetupTests.cs

---

## Implementation Units

### U1. Scaffold `Headless.Mediator` package + `AllowMissingTenantAttribute`

**Goal:** Land the package skeleton with a non-empty surface (the trivial attribute) so subsequent units can build incrementally.

**Requirements:** R1, R2

**Dependencies:** None

**Files:**
- Create: `src/Headless.Mediator/Headless.Mediator.csproj`
- Create: `src/Headless.Mediator/AllowMissingTenantAttribute.cs`
- Create: `src/Headless.Mediator/README.md` (placeholder; finalized in U5)
- Modify: `headless-framework.slnx` (add `<Folder Name="/Mediator/">` containing the new csproj path)

**Approach:**
- Mirror `src/Headless.FluentValidation/Headless.FluentValidation.csproj`: minimal `<Project Sdk>`, `<TargetFramework>net10.0</TargetFramework>`, `<RootNamespace>Mediator</RootNamespace>` (matches the `Headless.Mediator` namespace via `Headless.` global prefix in `src/Directory.Build.props`).
- `PackageReference Include="Mediator.Abstractions"` (version flows from `Directory.Packages.props`; already 3.0.2).
- `ProjectReference Include="..\Headless.Core\Headless.Core.csproj"`.
- Attribute file: `namespace Headless.Mediator;` with `[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]`. `public sealed class AllowMissingTenantAttribute : Attribute;` — primary-constructor-less, no body. `[PublicAPI]` per repo convention.
- Slnx edit: insert a `/Mediator/` folder in alphabetical order (between `/MediaIndexing/` and `/Messaging/`).

**Patterns to follow:**
- `src/Headless.FluentValidation/Headless.FluentValidation.csproj` (csproj shape)
- `src/Headless.Core/Abstractions/MissingTenantContextException.cs` (XML-doc style for the new type)

**Test scenarios:**
- Test expectation: none — pure scaffolding + a marker attribute with no behavior. Coverage arrives via U4 (the attribute is exercised through `TenantRequiredBehavior` matrix tests and a single direct `AttributeUsage` reflection check in U4's `AllowMissingTenantAttributeTests.cs`).

**Verification:**
- `dotnet build src/Headless.Mediator --no-incremental -v:q -nologo /clp:ErrorsOnly` succeeds with zero warnings.
- `Headless.Mediator.dll` exposes `Headless.Mediator.AllowMissingTenantAttribute` as `public sealed`.
- Solution loads in IDE; new package visible under `/Mediator/` solution folder.

---

### U2. `TenantRequiredBehavior<TRequest, TResponse>`

**Goal:** Implement the pipeline behavior with type-load attribute caching and the missing-tenant throw path.

**Requirements:** R3, R4, R5, R7

**Dependencies:** U1

**Files:**
- Create: `src/Headless.Mediator/TenantRequiredBehavior.cs`

**Approach:**
- `[PublicAPI] public sealed class TenantRequiredBehavior<TRequest, TResponse>(ICurrentTenant currentTenant) : IPipelineBehavior<TRequest, TResponse>` (primary constructor per repo convention).
- `private static readonly bool _AllowMissingTenant = Attribute.IsDefined(typeof(TRequest), typeof(AllowMissingTenantAttribute), inherit: false);` — evaluated once per closed generic by the runtime.
- `Handle` body: if `_AllowMissingTenant` is true OR `!string.IsNullOrWhiteSpace(currentTenant.Id)`, call `next.Invoke(message, cancellationToken)` and return. Otherwise, build and throw `MissingTenantContextException`.
- Throw site: `var ex = new MissingTenantContextException(); ex.Data["Headless.Mediator.FailureCode"] = "MissingTenantContext"; throw ex;`. Use the parameterless ctor — the type's default message already documents the remediation; no per-throw message string allocation.
- Constructor argument validated via `Argument.IsNotNull(currentTenant)` (per `CLAUDE.md` convention to use `Headless.Checks`).
- XML doc on the type covers: precondition (`ICurrentTenant` registered), opt-out attribute reference, layer Data-key, and explicitly notes the remediation message lives on `MissingTenantContextException`'s default constructor (so callers don't need to construct a message).

**Execution note:** Test-first. The 4-quadrant matrix is the validation.

**Patterns to follow:**
- `src/Headless.Api/Mediator/ApiCriticalRequestLoggingBehavior.cs` — pipeline-behavior signature, `ValueTask<TResponse>`, `MessageHandlerDelegate<TMessage, TResponse> next` parameter shape.
- Constructor argument validation: any recent file using `Argument.IsNotNull` from `Headless.Checks` (e.g., `src/Headless.Api/Setup.cs`).

**Test scenarios** *(land alongside U2 per test-first execution note; physical files created in U4):*
- Happy path: `[AllowMissingTenant]` absent + `ICurrentTenant.Id == "acme"` → `next` invoked exactly once; behavior returns `next`'s response.
- Happy path: `[AllowMissingTenant]` present + `ICurrentTenant.Id == "acme"` → `next` invoked (attribute is permissive, not authoritative).
- Happy path: `[AllowMissingTenant]` present + `ICurrentTenant.Id == null` → `next` invoked.
- Error path: `[AllowMissingTenant]` absent + `ICurrentTenant.Id == null` → throws `MissingTenantContextException`; `next` not invoked.
- Error path: `[AllowMissingTenant]` absent + `ICurrentTenant.Id == ""` → throws (whitespace-as-missing).
- Error path: `[AllowMissingTenant]` absent + `ICurrentTenant.Id == "   "` → throws (whitespace-as-missing).
- Error path / `Data` key: thrown exception carries `Data["Headless.Mediator.FailureCode"] == "MissingTenantContext"`.
- Edge case: `currentTenant` ctor argument is null → `Argument.IsNotNull` throws `ArgumentNullException` at construction.
- Caching invariant: dispatch the same closed-generic request type twice with two different `ICurrentTenant` substitutes; assert the type's `_AllowMissingTenant` field was read once (via reflection on the field, OR by counting `Attribute.IsDefined` calls via a deterministic test that constructs two distinct closed-generic instances and observes identical attribute-presence resolution without re-evaluation). Pick approach during implementation; the invariant under test is "no per-request attribute reflection."

**Verification:**
- All test scenarios above pass.
- `dotnet build` clean (zero warnings); `TenantRequiredBehavior<,>` is `public sealed` and `internal`-method-free.
- Manual review confirms `_AllowMissingTenant` is `static readonly`, not `static` field with deferred init, not `ConcurrentDictionary` lookup.

---

### U3. `AddTenantRequiredBehavior()` registration extension

**Goal:** Provide the consumer-facing DI surface; idempotent open-generic registration.

**Requirements:** R6

**Dependencies:** U2

**Files:**
- Create: `src/Headless.Mediator/MediatorSetup.cs`

**Approach:**
- File header: `namespace Headless.Mediator;` (standard repo convention).
- Use the modern `extension(IServiceCollection services)` extension-block style per `src/Headless.Api/Setup.cs`:
  ```
  extension(IServiceCollection services)
  {
      public IServiceCollection AddTenantRequiredBehavior() { ... }
  }
  ```
  *(Directional only — exact framing during implementation.)*
- Body: `Argument.IsNotNull(services); services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IPipelineBehavior<,>), typeof(TenantRequiredBehavior<,>))); return services;`.
- Class is `public static class MediatorSetup` with `[PublicAPI]` per the `ApiSetup` precedent.
- XML doc: notes that `ICurrentTenant` must be registered separately (typically by `Headless.Api`'s `MultiTenancySetup`); calling `AddMediator(...)` is the consumer's responsibility.

**Patterns to follow:**
- `src/Headless.Api/Setup.cs` — extension-block style, `[PublicAPI]`, `Argument.IsNotNull` validation.
- `Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddEnumerable` — open-generic-safe, idempotent for distinct (service, implementation) pairs.

**Test scenarios** *(physical file in U4):*
- Happy path: `services.AddTenantRequiredBehavior()` results in exactly one `ServiceDescriptor` for `(IPipelineBehavior<,>, TenantRequiredBehavior<,>)` with `Transient` lifetime.
- Idempotent: calling twice still yields exactly one descriptor for that pair.
- Returns the same `IServiceCollection` instance (fluent chaining).
- Edge case: passing null `services` → `ArgumentNullException` from `Argument.IsNotNull`.

**Verification:**
- All scenarios above pass.
- Resolving `IEnumerable<IPipelineBehavior<TestRequest, TestResponse>>` from the built provider returns one instance whose runtime type is `TenantRequiredBehavior<TestRequest, TestResponse>`.

---

### U4. Test project + behavior/attribute/setup tests

**Goal:** Land `Headless.Mediator.Tests.Unit` with the U2 + U3 + attribute scenarios as concrete xUnit tests.

**Requirements:** R10 (coverage); validates R3-R7 from U2/U3.

**Dependencies:** U1, U2, U3

**Files:**
- Create: `tests/Headless.Mediator.Tests.Unit/Headless.Mediator.Tests.Unit.csproj`
- Create: `tests/Headless.Mediator.Tests.Unit/TenantRequiredBehaviorTests.cs`
- Create: `tests/Headless.Mediator.Tests.Unit/AllowMissingTenantAttributeTests.cs`
- Create: `tests/Headless.Mediator.Tests.Unit/MediatorSetupTests.cs`
- Modify: `headless-framework.slnx` (add the test csproj path inside the `/Mediator/` folder block from U1)

**Approach:**
- Csproj mirrors `tests/Headless.Api.Tests.Unit/Headless.Api.Tests.Unit.csproj`: `<TargetFramework>net10.0</TargetFramework>`, `<RootNamespace>Tests</RootNamespace>`, `<OutputType>Exe</OutputType>`, single `xunit.v3.mtp-v2` PackageReference, `ProjectReference` to `..\..\src\Headless.Mediator\Headless.Mediator.csproj`. `tests/Directory.Build.props` already provides `Bogus`, `AwesomeAssertions`, `NSubstitute`, `Xunit` global usings.
- Test request types live as nested types or top-of-file types in `TenantRequiredBehaviorTests.cs`:
  - `private sealed record TestRequest : IRequest<TestResponse>;`
  - `[AllowMissingTenant] private sealed record AllowMissingTestRequest : IRequest<TestResponse>;`
  - `private sealed record TestResponse;`
- `ICurrentTenant` is mocked with `NSubstitute.Substitute.For<ICurrentTenant>()`.
- `next` delegate (`MessageHandlerDelegate<TRequest, TResponse>`) is a small lambda or `Substitute.For<...>()` to assert call counts.
- Test method names follow `should_{action}_{expected_behavior}_when_{condition}` (per `dotnet.md`).
- `AllowMissingTenantAttributeTests.cs` covers a single reflection check: `AttributeUsage` is `Class | Struct`, `Inherited == false`, `AllowMultiple == false`.
- `MediatorSetupTests.cs` covers the U3 scenarios.

**Patterns to follow:**
- `tests/Headless.Api.Tests.Unit/Headless.Api.Tests.Unit.csproj` — csproj shape.
- `tests/Headless.Checks.Tests.Unit/EnsureTests.cs` — xUnit + AwesomeAssertions style.
- `tests/Headless.FluentValidation.Tests.Unit/CollectionValidators_*.cs` — flat-file test layout for a flat-package test project.

**Test scenarios:**
- All scenarios listed under U2 and U3 are physically realized here.
- Covers AE-level: the behavior end-to-end matrix proves origin R3-R7.

**Verification:**
- `Skill(compound-engineering:dotnet-test)` runs the test project to green.
- Coverage report shows ≥85% line / ≥80% branch on `src/Headless.Mediator/`.
- `dotnet build` of the solution is clean.

---

### U5. `src/Headless.Mediator/README.md` + tenancy doc updates

**Goal:** Document the package surface, opt-out usage, pipeline-ordering guidance; extend the canonical tenancy doc with a Mediator-boundary section.

**Requirements:** R8, R9

**Dependencies:** U1, U2, U3

**Files:**
- Modify: `src/Headless.Mediator/README.md` (replace U1's placeholder)
- Modify: `docs/llms/multi-tenancy.md` (extend with a Mediator-boundary section; update frontmatter `packages` field to include `Mediator`)

**Approach:**
- Package README mirrors `src/Headless.FluentValidation/README.md` shape: title, "Problem Solved", "Key Features", "Installation" (`dotnet add package Headless.Mediator`), "Quick Start" (DI registration + minimal request example), "Usage" (opt-out attribute use, pipeline-ordering guidance, `ICurrentTenant` precondition).
- `docs/llms/multi-tenancy.md` update: the file already references this issue (#236) in the failure-mapping section (line 81) and elsewhere. Add a new top-level section — likely `## Mediator-Boundary Enforcement` — between "HTTP Failure Mapping" and "Tenant Semantics" (or wherever the existing section flow places it best). Include: the `Headless.Mediator` package surface, when `[AllowMissingTenant]` applies (Console / system / public-endpoint commands), the failure-code Data key, recommended pipeline order (Auth → TenantRequired → Idempotency, observational based on zad-ngo precedent), and that ordering is the consumer's responsibility (not framework-enforced). Add `Mediator` to the frontmatter `packages` line. Add a TOC entry. Keep the doc's existing rule-heavy / Agent-Instructions voice — no prose-heavy expansion.

**Patterns to follow:**
- `src/Headless.FluentValidation/README.md` — package README structure.
- `docs/llms/multi-tenancy.md` (existing) — rule-heavy voice, frontmatter style, TOC + "Agent Instructions" + section-per-concern shape.

**Test scenarios:**
- Test expectation: none — documentation. Manual verification: another agent reading just the README can wire up the behavior end-to-end without referencing the source. Manual verification: another agent reading just `docs/llms/multi-tenancy.md` can decide whether a given request type needs `[AllowMissingTenant]` from the rules alone.

**Verification:**
- `src/Headless.Mediator/README.md` covers: install, quick-start, opt-out usage, pipeline-ordering, `ICurrentTenant` precondition.
- `docs/llms/multi-tenancy.md` has a Mediator-boundary section, updated frontmatter `packages` field, and a TOC entry. Existing references to #236 (line 81 area) remain consistent.
- Root `llms.txt` is unchanged — the file was already indexed.

---

## System-Wide Impact

- **Interaction graph:** New behavior slots into `Mediator.SourceGenerator`'s pipeline. Consumer registration order matters (Auth → TenantRequired → Idempotency); not enforced.
- **Error propagation:** `MissingTenantContextException` flows through the existing HTTP 400 handler (`TenantContextExceptionHandler`, #237) unchanged — non-breaking. Non-HTTP consumers see the exception bubble through their own dispatch loop; the failure-code Data key disambiguates layer for log aggregation.
- **State lifecycle risks:** None — behavior is stateless and constructs cheaply per request. `static readonly bool` field is initialized once per closed-generic at runtime type-load.
- **API surface parity:** The same `MissingTenantContextException` is the cross-layer signal already used by the messaging strict-tenancy publish guard (#238/#241, shipped) and will be by EF write guard (#234). Layer disambiguation via `Exception.Data` is consistent across all three.
- **Integration coverage:** End-to-end HTTP 400 round-trip is already covered by `Headless.Api.Tests.Integration` for the existing exception. No new integration test required.
- **Unchanged invariants:**
  - `ICurrentTenant` API in `Headless.Core/Abstractions/ICurrentTenant.cs` — untouched.
  - `MissingTenantContextException` in `Headless.Core/Abstractions/MissingTenantContextException.cs` — untouched.
  - `Headless.Api/Mediator/` 4 existing behaviors — untouched (HTTP-coupled; stay where they are).
  - `TenantContextExceptionHandler` (#237) — untouched; catches the new throw site automatically via base-type catch.

---

## Risks & Dependencies

| Risk | Mitigation |
|------|------------|
| Pipeline registration order misuse (TenantRequired registered after Idempotency) leads to idempotency-cache poisoning with cross-tenant entries. | README and `docs/multi-tenancy.md` explicitly call out the recommended order. Not enforced — escalate to a startup validator if the issue surfaces in practice. Not in scope for this plan. |
| `static readonly bool` field initialized via attribute-presence check at type-load time may surprise consumers who add `[AllowMissingTenant]` via runtime weaving (Fody, etc.). | Acceptable: the framework's contract is compile-time attribute usage; runtime weaving falls outside contract. Documented in README. |
| `Mediator.Abstractions` major version bump could break the `IPipelineBehavior<,>` signature. | Version flows from `Directory.Packages.props` (currently 3.0.2). Bumps are caught by the same review process that audits the existing `Headless.Api/Mediator/` behaviors. No additional mitigation. |
| Streaming-pipeline tenant guard missing if the codebase later adds `IStreamRequest` consumers. | Listed as an explicit non-goal in Scope Boundaries; add a streaming variant only when a real consumer surfaces. The same `MissingTenantContextException` + Data key would apply. |

---

## Documentation / Operational Notes

- `docs/llms/` is the existing canonical AI-readable doc tree (26 files, indexed from root `llms.txt`). This plan extends `docs/llms/multi-tenancy.md` in place; no doc-tree convention is being introduced or changed.
- No rollout/feature-flag concerns — net-new package, opt-in via `services.AddTenantRequiredBehavior()`.
- No monitoring signals to add — failures surface via existing logging through the HTTP 400 path.

---

## Sources & References

- **Origin document:** [docs/brainstorms/2026-05-09-mediator-tenant-required-behavior-requirements.md](../brainstorms/2026-05-09-mediator-tenant-required-behavior-requirements.md)
- Issue: [#236](https://github.com/xshaheen/headless-framework/issues/236)
- Companion issue: [#234](https://github.com/xshaheen/headless-framework/issues/234) (EF write guard)
- Shipped HTTP handler: commit `dccf1ef1` (#237 → #242 `TenantContextExceptionHandler`)
- Precedent: [xshaheen/zad-ngo#152](https://github.com/xshaheen/zad-ngo/pull/152)
- Existing behavior pattern: `src/Headless.Api/Mediator/ApiCriticalRequestLoggingBehavior.cs`
- Existing flat-package shape: `src/Headless.FluentValidation/`
