---
title: "feat: TenantId first-class envelope property"
type: feat
status: active
date: 2026-05-01
origin: docs/brainstorms/2026-05-01-tenant-id-envelope-requirements.md
issue: https://github.com/xshaheen/headless-framework/issues/228
parent_epic: https://github.com/xshaheen/headless-framework/issues/217
---

# feat: TenantId first-class envelope property

## Summary

Promote `TenantId` from an informal header convention to a typed envelope property on `PublishOptions` and `ConsumeContext`, with a strict 4-case publish-time integrity policy centralized in `Headless.Messaging.Core` so all 11 transports inherit it without per-provider duplication.

## Problem Frame

Today `ConsumeContext.Headers` documents `TenantId` only as a "common header key (by convention)" — there is no typed surface, no constant for the wire key, and no defense against header injection at the publisher boundary. Every consumer threads `headers["TenantId"]` ad-hoc with no shared key and no validation. The cross-layer tenancy stack (EF write guard #234, Mediator behavior #236, tenant-propagation filter #235, ProblemDetails handler #237) all assumes a typed envelope value to anchor on, but the envelope itself is missing.

This plan ships envelope plumbing only. Brainstorming on 2026-05-01 narrowed the original Phase 1/U2 scope (which bundled `DeliveryKind`, `AllowHeaderTenantHydration`, and `TenantContextRequired`) down to envelope properties + strict integrity (see origin: `docs/brainstorms/2026-05-01-tenant-id-envelope-requirements.md`).

## Requirements

- **R1.** `PublishOptions.TenantId : string?` (init-set). When set, value is non-whitespace and ≤200 characters. When unset, the property is `null` and no header is written.
- **R2.** `ConsumeContext<TMessage>.TenantId : string?` (init-set, **not** `required`). Populated by the consume pipeline from `headers["headless-tenant-id"]`; `null` when the header is absent or empty.
- **R3.** `Headers.TenantId = "headless-tenant-id"` constant ships in `Headless.Messaging.Abstractions`. The wire-header key is canonical; no other key is recognized.
- **R4.** Validation matches the existing `MessageId` / `CorrelationId` pattern: max 200 chars, non-whitespace when set, no charset enforcement. Charset sanitization is documented as a consumer-app responsibility.
- **R5.** Publish-time strict 4-case integrity policy enforced once in the shared publish wrapper in `Headless.Messaging.Core`:
  - **(a)** typed property set, raw header unset → wrapper stamps the header from the property and emits.
  - **(b)** typed property unset, raw header set → publish is **rejected** with `InvalidOperationException`. The raw header is reserved for transport-internal use.
  - **(c)** Both set, equal → emit (no-op reconciliation).
  - **(d)** Both set, **disagree** → publish is **rejected** with `InvalidOperationException` carrying both values in the message.
- **R6.** Consume-side is lenient: a malformed or oversized header value maps to `ConsumeContext.TenantId = null` rather than failing the whole message. (Phase 2 may revisit by surfacing malformed envelopes through `IDeadLetterObserver`.)
- **R7.** No new exception types, no new options, no new DI seams. Three property additions, one header constant, and the wrapper logic.

## Success Criteria

- A caller setting `PublishOptions.TenantId = "acme"` produces `headers["headless-tenant-id"] = "acme"` on the wire (verified via `InMemoryStorage` round-trip).
- A consume pipeline wrapping a fake transport with `headers["headless-tenant-id"] = "acme"` yields `ConsumeContext.TenantId == "acme"`.
- A caller writing `headers["headless-tenant-id"]` directly without the typed property is rejected at publish with a clear `InvalidOperationException`.
- A caller with `PublishOptions.TenantId = "acme"` AND `headers["headless-tenant-id"] = "acme-evil"` is rejected at publish with both values reported.
- Publishing with `TenantId = null` succeeds; the header is not written; consume side observes `ConsumeContext.TenantId == null`.
- `grep -r '"headless-tenant-id"' src/Headless.Messaging.* | grep -v Headers.cs` returns zero results — only `Headers.cs` knows the key.

## Scope Boundaries

- No `DeliveryKind` enum on `ConsumeContext` (deferred — see origin).
- No `AllowHeaderTenantHydration` opt-in. Strict 4-case rejection is the resolved decision.
- No `TenantContextRequired` startup validator (different failure semantics; sibling follow-up).
- No new exception types, options, or DI seams (R7).
- No per-transport implementations or per-transport tests for the 4-case policy — the policy lives in Core; transports remain pure round-trip pipes.
- No charset sanitization of TenantId values — documented consumer-app responsibility.

### Deferred to Separate Tasks

- **Sibling: messaging strict-tenancy publish guard**: separate follow-up issue. Throws `MissingTenantContextException` (defined in #234) when ambient tenant is required but absent on publish. Distinct from this plan's `InvalidOperationException` (header injection / publisher misuse).
- **Per-provider composite-key `(TenantId, MessageId)` dedup migration**: Phase 2 (U3a/U3b in #217).
- **Inbound envelope signing / cross-tenant authenticity**: Phase 2 (`IConsumeBehavior<T>`).
- **#235 code changes**: `TenantPropagationOutboxPublisher` / `TenantPropagationConsumeFilter` rewiring lands in #235's own PR. This plan only updates #235's issue body to record the dependency.

## Context & Research

### Relevant Code and Patterns

- **`src/Headless.Messaging.Abstractions/PublishOptions.cs`** — flat `init`-set properties, no in-setter validation. `MessageId` / `CorrelationId` (and `MessageIdMaxLength = 200` const) are the exact pattern to mirror for `TenantId`.
- **`src/Headless.Messaging.Abstractions/ConsumeContext.cs`** — uses `init` accessors with the `field` keyword and inline `if`+`throw new ArgumentException` for whitespace checks. `CorrelationId` (lines 90-105) is the mirror pattern: nullable-but-rejecting-empty-string-when-set. `TenantId` is **not** `required`, unlike `MessageId`.
- **`src/Headless.Messaging.Abstractions/Headers.cs`** — kebab-case `headless-*` prefix is the convention (12 existing constants). `TenantId = "headless-tenant-id"` matches.
- **`src/Headless.Messaging.Core/Internal/IMessagePublishRequestFactory.cs`** — the single header-projection seam. `MessagePublishRequestFactory.Create` calls `_CreateHeaders`, which reserves a small set of header keys via `_ValidateCustomHeaders` and writes envelope properties through helpers like `_ValidateMessageId` (uses `Argument.IsLessThanOrEqualTo` → `ArgumentOutOfRangeException`). Both `DirectPublisher` and `OutboxPublisher` call `IMessagePublishRequestFactory.Create`; **no transport-specific publishers exist**.
- **`src/Headless.Messaging.Core/Internal/IConsumeExecutionPipeline.cs`** — Expression-tree compiled `_CompileFactory(messageType)` builds `ConsumeContext<T>` from `headers`, cached per type. Existing static helper `_ResolveTimestamp(headers, added)` is bound via `Expression.Call` and is the model for a new `_ResolveTenantId(headers)` helper that performs lenient null/whitespace/length checks outside the Expression tree.
- **`tests/Headless.Messaging.Core.Tests.Unit/IntegrationTests/IDirectPublisherIntegrationTests.cs`** — `should_include_custom_headers_in_message` (lines 209-252) is the publish→consume round-trip template using `IntegrationTestBase` + `DirectTestConsumerWithHeaders`. Round-trip test for TenantId extends this file.
- **`tests/Headless.Messaging.Core.Tests.Unit/DirectPublisherTests.cs`** — `_ValidateMessageId` failure assertion (line 233) shows the assertion shape: `ThrowAsync<ArgumentOutOfRangeException>().WithParameterName(...).WithMessage("*...*")`.

### Institutional Learnings

- **`docs/solutions/guides/messaging-transport-provider-guide.md`** — documents the canonical envelope-header contract every transport must round-trip. Adding `TenantId` is a direct extension of this contract; the guide must be updated in the same PR (currently lists 6 mandatory + 4 optional headers; add `TenantId` and classify as optional given R6 lenient consume).
- **`docs/solutions/messaging/transport-wrapper-drift-and-doc-sync.md`** — flags multi-surface drift risk when envelope/API shapes change. Touch-points to keep in lockstep: package READMEs, `docs/llms/*` snippets, the transport-provider guide, dashboard display layers. No automated guardrail exists.
- **`docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md`** — adjacent precedent that "exception detail leaked to broker headers" caused real information disclosure. Reinforces R7's "no new exception types" stance and the case-(d) message format choice (carry both values for diagnostic clarity, no stack data).

No `docs/solutions/` entry exists yet for tenancy in messaging, strict-mode validators, or envelope-property additions. Capturing one after this plan lands is recommended (post-implementation).

### External References

External research skipped — local patterns for envelope properties (`MessageId`, `CorrelationId`) are strong, well-tested, and directly mirror what's needed. The 4-case integrity policy is a localized framework decision with no useful external precedent.

## Key Technical Decisions

- **Single insertion point for the 4-case policy**: extend `MessagePublishRequestFactory._CreateHeaders` with a private `_ApplyTenantId(headers, options)` helper invoked after `_ValidateCustomHeaders`. Honors R7 (no new DI seam) and inherits the existing reserved-header rejection idiom. **Rationale**: every transport composes with this factory via `IDirectPublisher`/`IOutboxPublisher`; centralizing here gives all 11 transports the policy for free.
- **Do not add `Headers.TenantId` to `_ReservedHeaders`**: the existing `_ReservedHeaders` set causes outright rejection of any user-supplied header. Case (c) (both set, equal) requires accepting an equal raw header, so reservation semantics don't fit. TenantId reconciliation gets its own helper.
- **Consume-side: static helper bound via `Expression.Call`**: add `private static string? _ResolveTenantId(IDictionary<string, string?> headers)` in `ConsumeExecutionPipeline`, parallel to the existing `_ResolveTimestamp`. R6's lenient semantics (oversized → null, whitespace → null, missing → null) are awkward inside an Expression tree; the helper keeps the binding readable and the rules in C#. **Rationale**: precedent already exists in the same file; cache key (`messageType`) is unchanged.
- **Validation via `Headless.Checks`**: `_ValidateTenantId` mirrors `_ValidateMessageId`'s length check **plus** an explicit non-whitespace assertion (`Argument.IsNotNullOrWhiteSpace(tenantId, paramName: nameof(tenantId))` followed by `Argument.IsLessThanOrEqualTo(tenantId.Length, PublishOptions.TenantIdMaxLength, ..., paramName: nameof(tenantId))`). Whitespace-typed must throw because `TenantId` has no auto-generated default to fall back on (unlike `MessageId`, which `_CreateHeaders` rescues with a generated id, and `CorrelationId`, where whitespace rescues to `null`). Honors R1's "non-whitespace when set" contract. Yields `ArgumentException` for whitespace and `ArgumentOutOfRangeException` for length. Setter validation in `ConsumeContext.TenantId` uses inline `if`+`throw new ArgumentException` to mirror `CorrelationId`'s `init` accessor.
- **`ConsumeContext.TenantId` is not `required`**: differs from `MessageId` because R2 mandates `null` is a valid state (no header on the wire). Mirrors `CorrelationId`'s declaration shape.
- **Round-trip test home**: extend `tests/Headless.Messaging.Core.Tests.Unit/IntegrationTests/IDirectPublisherIntegrationTests.cs` rather than physically locating the test in `Headless.Messaging.InMemoryStorage.Tests.Unit`. The latter is today a pure storage unit suite (no DI/transport bootstrap); the former already wires InMemoryQueue + InMemoryStorage and has the consumer-fixture pattern. Brainstorm's intent (round-trip via in-memory transport) is preserved; only the physical csproj differs.
- **Failure messages mirror existing reserved-header style** for consistency:
  - Case (b): `Header '{Headers.TenantId}' is reserved. Use {nameof(PublishOptions)}.{nameof(PublishOptions.TenantId)} to set the tenant identifier.`
  - Case (d): `PublishOptions.TenantId='{typed}' disagrees with header '{Headers.TenantId}'='{raw}'. Set the typed property only.`

## Open Questions

### Resolved During Planning

- **Where does the 4-case policy live?** Single helper `_ApplyTenantId` in `MessagePublishRequestFactory`. Resolved by research — no provider-specific publishers exist; one factory feeds all 11 transports.
- **Should TenantId be added to `_ReservedHeaders`?** No. Case (c) requires accepting equal raw headers; the policy is more nuanced than reservation.
- **Round-trip test physical location**: `Core.Tests.Unit/IntegrationTests` chosen over `InMemoryStorage.Tests.Unit`; intent preserved.
- **Failure message format for cases (b)/(d)**: defined inline as Key Technical Decisions above (mirrors existing reserved-header phrasing).

### Deferred to Implementation

- **Exact placement of `Headers.TenantId` in `Headers.cs`** — alphabetical vs grouped-by-purpose. Decide while editing the file based on neighboring constants' grouping.
- **Whether the existing `ConsumeContextTests.cs` already has an XML-doc convention test** that needs updating when `TenantId` is removed from the "common header keys (by convention)" XML list — if such a test exists, update it; otherwise no test change.
- **Whether to test `_ResolveTenantId` directly or only through round-trip** — the helper is private static. If `InternalsVisibleTo` is already declared for `Core.Tests.Unit`, a direct unit test of the helper adds defense-in-depth at low cost. Otherwise, round-trip coverage is sufficient.
- **Final wording of XML doc on each new property** — match the established voice (referencing the durable-storage 200-char column constraint on `PublishOptions.TenantId`, mirroring `MessageId`'s doc).

## Implementation Units

- U1. **Add envelope surface in `Headless.Messaging.Abstractions`**

**Goal:** Ship the typed surface (`Headers.TenantId` constant + `PublishOptions.TenantId` property + `ConsumeContext.TenantId` property) that downstream Core changes anchor on. No behavior change yet.

**Requirements:** R1, R2, R3, R4 (declarative validation only)

**Dependencies:** None.

**Files:**
- Modify: `src/Headless.Messaging.Abstractions/Headers.cs`
- Modify: `src/Headless.Messaging.Abstractions/PublishOptions.cs`
- Modify: `src/Headless.Messaging.Abstractions/ConsumeContext.cs`
- Modify: `tests/Headless.Messaging.Abstractions.Tests.Unit/ConsumeContextTests.cs`
- Test: `tests/Headless.Messaging.Abstractions.Tests.Unit/PublishOptionsTests.cs` (extend if exists; create if not — confirm during implementation)

**Approach:**
- Add `public const string TenantId = "headless-tenant-id";` to `Headers.cs` with XML doc.
- Add `public const int TenantIdMaxLength = 200;` and `public string? TenantId { get; init; }` to `PublishOptions.cs` (flat `init`, no setter validation; mirrors `MessageId`).
- Add `public string? TenantId { get { ...; } init { /* whitespace-when-set check */ } }` to `ConsumeContext<TMessage>` (mirrors `CorrelationId`'s `init` accessor — **not** `required`). The XML doc on this property must explicitly reference the wire header constant (`<see cref="Headers.TenantId"/>`, value `"headless-tenant-id"`) so readers can trace property → wire key in one hop.
- Remove `TenantId` from the "common header keys (by convention)" XML doc list on `ConsumeContext.Headers` — it's now first-class.

**Patterns to follow:**
- `Headers.cs` existing constants — kebab-case `headless-*` wire value, XML doc per constant.
- `PublishOptions.MessageId` (lines 13, 32 in `PublishOptions.cs`) — the max-length const + flat `init` property pair.
- `ConsumeContext.CorrelationId` (lines 90-105 in `ConsumeContext.cs`) — `init` accessor with inline whitespace-when-set check using the `field` keyword.

**Test scenarios:**
- Happy path: `PublishOptions { TenantId = "acme" }` round-trips through equality / record `with` / null-default behavior.
- Happy path: `ConsumeContext` constructed with `TenantId = "acme"` exposes the value via the property getter.
- Edge case: `PublishOptions { TenantId = null }` (default) — property returns `null`; no validation triggered (length validation lives downstream in `_ValidateTenantId`, not the setter).
- Edge case: `ConsumeContext { TenantId = null }` — accepted, property returns `null`.
- Error path: `ConsumeContext { TenantId = "" }` and `TenantId = "  "` — throws `ArgumentException` with paramName `value` and a message identifying TenantId. Mirrors `CorrelationId`'s assertion in existing tests.
- Edge case: oversized value (>200 chars) on `PublishOptions.TenantId` is **not** rejected by the setter (length is enforced downstream); the setter only stores the value. This documents the chosen seam.

**Verification:**
- All three new symbols exist with correct signatures and XML docs.
- `tests/Headless.Messaging.Abstractions.Tests.Unit` passes, including new TenantId tests.
- `Headless.Messaging.Abstractions` builds clean with no consumer breakage.

- U2. **Implement 4-case publish-time integrity policy in `MessagePublishRequestFactory`**

**Goal:** Centralize the strict (a)/(b)/(c)/(d) policy in the single shared header-projection seam so every transport inherits it.

**Requirements:** R5, R7 (no new DI seams)

**Dependencies:** U1 (uses `Headers.TenantId`, `PublishOptions.TenantId`, `PublishOptions.TenantIdMaxLength`).

**Files:**
- Modify: `src/Headless.Messaging.Core/Internal/IMessagePublishRequestFactory.cs`
- Modify: `tests/Headless.Messaging.Core.Tests.Unit/DirectPublisherTests.cs` (or a new `MessagePublishRequestFactoryTests.cs` if `_CreateHeaders` is exercised directly there — confirm during implementation)
- Test: `tests/Headless.Messaging.Core.Tests.Unit/MessagePublishRequestFactoryTests.cs` (new file if direct factory tests don't yet exist)

**Approach:**
- Add `_ApplyTenantId(Dictionary<string, string?> headers, PublishOptions? options)` private method called from `_CreateHeaders` immediately after `_ValidateCustomHeaders(headers)`.
- Add `_ValidateTenantId(string tenantId)` static helper that runs `Argument.IsNotNullOrWhiteSpace(tenantId, paramName: nameof(tenantId))` then `Argument.IsLessThanOrEqualTo(tenantId.Length, PublishOptions.TenantIdMaxLength, ..., paramName: nameof(tenantId))`. Both checks are required: whitespace-typed has no default to rescue to (unlike `MessageId`/`CorrelationId`).
- Inside `_ApplyTenantId`: read typed value from `options?.TenantId`; read raw via `headers.TryGetValue(Headers.TenantId, out var raw)`. Branch into the 4 cases. Cases (a) and (c) end with `headers[Headers.TenantId] = typed`. Cases (b) and (d) throw `InvalidOperationException` with the messages defined in Key Technical Decisions.
- **Whitespace raw-header treatment**: a raw header that is null/whitespace is treated as **unset** for case-detection purposes (so case (b) does not fire on a whitespace-only injection). This mirrors R6's lenient consume semantics where whitespace headers map to `null`, keeping publish and consume rules symmetric. Document this choice in a code comment above `_ApplyTenantId`.
- Do **not** add `Headers.TenantId` to the existing `_ReservedHeaders` set.

**Technical design:**

```text
_CreateHeaders(...)
  ...
  _ValidateCustomHeaders(headers)   // existing
  _ApplyTenantId(headers, options)  // new — see below
  // existing MessageId, CorrelationId, etc. blocks follow
  ...

_ApplyTenantId(headers, options):
  typed = options?.TenantId            // string?
  rawPresent = headers.TryGetValue(Headers.TenantId, out raw)
  rawSet = rawPresent && !IsNullOrWhiteSpace(raw)

  if typed is null:
    if rawSet: throw InvalidOperationException (case b — reserved-header msg)
    return                                                    (neither set — no header)

  // typed is non-null:
  _ValidateTenantId(typed)                                    (non-whitespace + length)
  if rawSet && !raw.Equals(typed, Ordinal):
    throw InvalidOperationException (case d — disagree msg)
  headers[Headers.TenantId] = typed                           (cases a, c)
```

**Patterns to follow:**
- `_ValidateCustomHeaders` and `_ValidateMessageId` in the same file.
- `_ReservedHeaders` rejection-message style for the case-(b)/(d) wording.

**Test scenarios:**
- Happy path (case a): `PublishOptions { TenantId = "acme" }`, no raw header → resulting headers contain `[Headers.TenantId] = "acme"`.
- Happy path (case c): `PublishOptions { TenantId = "acme", Headers = { [Headers.TenantId] = "acme" } }` → emits with `[Headers.TenantId] = "acme"` (no error).
- Error path (case b): `PublishOptions { TenantId = null, Headers = { [Headers.TenantId] = "evil" } }` → throws `InvalidOperationException` whose message names `headless-tenant-id` and references `PublishOptions.TenantId`.
- Error path (case d): `PublishOptions { TenantId = "acme", Headers = { [Headers.TenantId] = "acme-evil" } }` → throws `InvalidOperationException` whose message contains both `acme` and `acme-evil`.
- Edge case: `PublishOptions { TenantId = null }`, no raw header → no `Headers.TenantId` key written; existing headers untouched.
- Error path: `PublishOptions { TenantId = " " }` (whitespace-only typed value) → throws `ArgumentException` from `Argument.IsNotNullOrWhiteSpace` with paramName `tenantId`. R1 mandates "non-whitespace when set" and TenantId has no default to rescue to.
- Edge case: `PublishOptions { TenantId = null, Headers = { [Headers.TenantId] = " " } }` (whitespace-only raw header, typed unset) → treated as no-op (raw-whitespace == unset). No exception; no header written. Mirrors R6 consume-side leniency.
- Error path: `PublishOptions { TenantId = new string('x', 201) }` → throws `ArgumentOutOfRangeException` with paramName `tenantId` and the "200 characters or fewer" message (mirrors `_ValidateMessageId`).
- Integration: covered by U4 (round-trip).

**Verification:**
- All four cases plus null/oversized cases assert the exact exception type, parameter name, and message-substring expected.
- Existing tests for `MessagePublishRequestFactory` / `DirectPublisher` / `OutboxPublisher` continue to pass (no regression in `MessageId`/`CorrelationId` paths).

- U3. **Wire consume-side `_ResolveTenantId` helper in `ConsumeExecutionPipeline`**

**Goal:** Populate `ConsumeContext.TenantId` from `headers["headless-tenant-id"]` with R6's lenient null-on-malformed semantics, leveraging the existing cached factory.

**Requirements:** R2, R6

**Dependencies:** U1 (uses `Headers.TenantId`, `ConsumeContext.TenantId`, `PublishOptions.TenantIdMaxLength`).

**Files:**
- Modify: `src/Headless.Messaging.Core/Internal/IConsumeExecutionPipeline.cs`
- Test: round-trip coverage lands in U4. Direct unit tests of `_ResolveTenantId` are optional and depend on whether `InternalsVisibleTo` is configured for `Core.Tests.Unit` (resolve at implementation time).

**Approach:**
- Add `private static string? _ResolveTenantId(IDictionary<string, string?> headers)` parallel to the existing `_ResolveTimestamp` static helper in the same file.
- The helper performs: `TryGetValue(Headers.TenantId, out var raw)` → return `null` if missing/`IsNullOrWhiteSpace(raw)` → return `null` if `raw.Length > PublishOptions.TenantIdMaxLength` → otherwise return `raw`.
- In `_CompileFactory(messageType)`, build `tenantIdProperty` and `tenantIdBinding` using `Expression.Call(typeof(ConsumeExecutionPipeline), nameof(_ResolveTenantId), null, headersProperty)` and append to the `Expression.MemberInit` arguments.
- Cache shape (`_compiledConsumeContextFactories` keyed on `Type messageType`) is unchanged.

**Patterns to follow:**
- The existing `_ResolveTimestamp` helper and its `Expression.Call` binding in the same file.
- The `correlationIdBinding` block (lines 164-184) for how an envelope binding is structured into the `MemberInit`.

**Test scenarios:**
- Covered transitively by U4 round-trip tests. If direct testing is feasible:
  - Happy path: headers contain `[Headers.TenantId] = "acme"` → returns `"acme"`.
  - Edge case: header missing → returns `null`.
  - Edge case: header empty / whitespace → returns `null`.
  - Edge case: header oversized (>200 chars) → returns `null` (lenient — does **not** fail the message).

**Verification:**
- After this unit, U4's round-trip test passes for "set value", "null when unset", and "oversized → null" scenarios.
- No change to the public `ConsumeContext` factory cache shape; existing tests for the pipeline still pass.

- U4. **Round-trip integration test (publish → consume) via in-memory transport**

**Goal:** Lock in the end-to-end behavior across publish + consume + transport for set / unset / malformed-inbound TenantId.

**Requirements:** All Success Criteria.

**Dependencies:** U1, U2, U3.

**Files:**
- Modify: `tests/Headless.Messaging.Core.Tests.Unit/IntegrationTests/IDirectPublisherIntegrationTests.cs`

**Approach:**
- Add three tests to the existing `IDirectPublisherIntegrationTests` class, using the established `IntegrationTestBase` fixture and `DirectTestConsumerWithHeaders` capture pattern:
  - `should_round_trip_tenant_id_when_set_via_publish_options`
  - `should_observe_null_tenant_id_when_publish_options_tenant_id_is_unset`
  - `should_map_oversized_inbound_tenant_id_to_null_on_consume` (publish via a path that bypasses `_ApplyTenantId` — e.g., directly writing to the in-memory transport's underlying queue, or constructing a `MediumMessage` in the test's storage shim if the existing harness exposes that hook; resolve approach during implementation if neither is straightforward).
- The first two tests use `IDirectPublisher.PublishAsync(message, new PublishOptions { TenantId = "acme" })` and `new PublishOptions()`, then assert on `DirectTestConsumerWithHeaders.ReceivedContexts.First().TenantId`.

**Patterns to follow:**
- `should_include_custom_headers_in_message` (lines 209-252) for the publish→consume capture template.
- `DirectTestConsumerWithHeaders` (lines 428-450) for the consumer fixture.

**Test scenarios:**
- Happy path: `PublishOptions { TenantId = "acme" }` → `ctx.TenantId == "acme"` and `ctx.Headers[Headers.TenantId] == "acme"`.
- Edge case: `PublishOptions { TenantId = null }` (default) → `ctx.TenantId == null` and `Headers.TenantId` key is absent from `ctx.Headers`.
- Edge case (R6 lenient consume): inbound header value of length 201 → `ctx.TenantId == null` (test injects via the test-only path noted above; not via the typed property, since publish-time validation would reject 201).

**Verification:**
- All three tests pass.
- The `grep` Success Criterion (`grep -r '"headless-tenant-id"' src/Headless.Messaging.* | grep -v Headers.cs`) returns zero results — surfacing in tests is fine but production source must reference the constant only.

- U5. **Documentation and operational follow-ups**

**Goal:** Bring docs into sync with the new envelope surface and complete the brainstorm's coordination acceptance criteria.

**Requirements:** Acceptance criteria items in origin doc — XML docs updated, transport-provider guide includes TenantId, sibling issues coordinated.

**Dependencies:** U1, U2, U3 (so docs reference real symbols).

**Files:**
- Modify: `src/Headless.Messaging.Core/README.md` (line ~143 — replace `["tenant-id"] = "demo"` example with typed `TenantId = "demo"`).
- Modify: `docs/llms/multi-tenancy.md` (line ~132 — replace `context.Headers["TenantId"]` with `context.TenantId`).
- Modify: `docs/solutions/guides/messaging-transport-provider-guide.md` — add `TenantId` to the envelope-header contract; classify as optional given R6 lenient consume; note publish-time strict policy lives in Core.
- Modify: `src/Headless.Messaging.Abstractions/README.md` and any other affected package READMEs — review and update envelope-property listings if present.
- Modify: `llms.txt` and `llms-full.txt` at the repo root — these capture API surface for AI-assisted consumers. Confirm during implementation whether they are hand-written (manual edit required) or generated by a build/script step (regenerate). If the latter and the regen step exists, run it; if neither, hand-edit only the `Headless.Messaging.{Abstractions,Core}` sections to mention `PublishOptions.TenantId` / `ConsumeContext.TenantId` / `Headers.TenantId`.
- XML docs: ensure all new public symbols (`Headers.TenantId`, `PublishOptions.TenantId`, `PublishOptions.TenantIdMaxLength`, `ConsumeContext<T>.TenantId`) carry doc comments matching repo voice. The `ConsumeContext.TenantId` doc must `<see cref="Headers.TenantId"/>` to anchor the property → wire-key relationship.

**Approach (operational, not in this PR's diff but tracked here):**
- Edit issue body of #235 to record the dependency on #228 and switch its tech notes to the typed property.
- Open the sibling follow-up issue for the messaging strict-tenancy publish guard (`MissingTenantContextException` family). Cross-link to #228, #234, #236.
- Edit issue body of #217 to remove `AllowHeaderTenantHydration` references and note "strict 4-case rejection (no opt-in)" as the resolved decision in the Phase 1/U2 entry.

**Patterns to follow:**
- Existing READMEs' envelope-property documentation style.
- `messaging-transport-provider-guide.md` mandatory/optional table format.

**Test scenarios:**
- Test expectation: none — documentation and issue housekeeping. Verified by code review (XML doc rendering, README diff readability, issue-body cross-link presence).

**Verification:**
- Every modified public type has up-to-date XML docs.
- Transport-provider guide table includes `TenantId` with classification and rationale.
- `llms.txt` / `llms-full.txt` mention the new envelope property in their Messaging sections.
- Issue #235 body links to #228 and references the typed property.
- Sibling strict-tenancy issue exists and is cross-linked to #228, #234, #236.
- Issue #217's Phase 1/U2 entry no longer mentions `AllowHeaderTenantHydration`.

## System-Wide Impact

- **Interaction graph:** every transport provider in `src/Headless.Messaging.{InMemoryQueue,InMemoryStorage,RabbitMq,Kafka,Nats,Pulsar,RedisStreams,AwsSqs,AzureServiceBus,...}` inherits the new policy through `IMessagePublishRequestFactory.Create`. No transport-side change required, but each transport's tests should continue to pass — sanity-check by running the full `Headless.Messaging.*` test suite.
- **Error propagation:** publish-time `InvalidOperationException` propagates out of `IDirectPublisher.PublishAsync` / `IOutboxPublisher.PublishAsync` / `PublishDelayAsync` to the caller. No retry / no message produced. This is consistent with how reserved-header violations surface today.
- **State lifecycle risks:** outbox-stored messages were never written (publish-time rejection happens before persistence in `OutboxPublisher.PublishAsync`'s `Create` call). No dangling outbox rows.
- **API surface parity:** `ConsumeContext.TenantId` is an init-set property; consumers that construct `ConsumeContext` manually (test fixtures) gain a new optional field. No `required` keyword, so existing constructions remain compilable.
- **Integration coverage:** U4's round-trip is the canonical cross-layer assertion. A `dev:code` execution should run the full `Headless.Messaging.*` test suite (`dotnet test` across all `Headless.Messaging.*.Tests.{Unit,Integration}` projects) to confirm no transport regressed.
- **Unchanged invariants:** `MessageId`, `CorrelationId`, `CorrelationSequence`, `CallbackName`, and all other existing envelope properties retain their exact behavior. The set of `_ReservedHeaders` is unchanged. No new DI registration. No new exception types.

## Risks & Dependencies

| Risk | Mitigation |
|------|------------|
| User publishes via `PublishOptions.Headers` directly (the case-(b) failure mode) and surfaces a runtime exception in production. | Failure message references the typed property by name and is symmetrical to existing reserved-header errors; XML docs on `PublishOptions.TenantId` document the strict policy explicitly. |
| Charset bleed: TenantId values flow into URLs, SQL columns, OTel tags, log lines downstream. | R4 documents charset sanitization as consumer-app responsibility. README and XML doc both call this out. Out of scope for this PR. |
| Transport provider guide drifts from code (per drift learning). | U5 updates the guide in the same PR; the sole envelope-contract source is the guide + `Headers.cs` constants. |
| Future Phase-2 work (signing, dedup migration) wants to reuse `_ResolveTenantId`'s null-on-malformed logic but the helper is `private static`. | Documented in code comment: helper is intentionally private; if future units need it, promote to `internal` then. |
| `MessagePublishRequestFactory._CreateHeaders` complexity grows incrementally as more envelope properties get strict-policy treatment. | Acceptable for now (one helper per property is the existing idiom). If 3+ such helpers accumulate, consider extracting a `IEnvelopePolicy` collection (deferred — outside this plan). |
| `ConsumeExecutionPipeline._CompileFactory` Expression-tree changes could break the cached `_compiledConsumeContextFactories` for existing message types if a type-loading order edge case exists. | The cache is keyed on `Type messageType` and built lazily; appending one new `MemberBinding` to the `MemberInit` arguments doesn't change the cache key or invalidate prior compilations across builds (cache lives in process memory). Verified by running existing pipeline tests post-change. |

## Documentation / Operational Notes

- README diffs in `Headless.Messaging.Core` and `Headless.Messaging.Abstractions` should call out the new envelope property in the changelog or "What's new" if such a pattern exists in this repo.
- Update `docs/solutions/guides/messaging-transport-provider-guide.md`'s envelope-contract table.
- Post-implementation, capture a `docs/solutions/messaging/` entry recording the strict 4-case decision, the single-seam approach, and rationale for not adding TenantId to `_ReservedHeaders` (per learning-researcher gap analysis — this fills a documented hole).
- Issue body updates (#235, #217, sibling strict-tenancy issue) are non-code coordination work but are part of the brainstorm's acceptance criteria.

## Sources & References

- **Origin document:** [docs/brainstorms/2026-05-01-tenant-id-envelope-requirements.md](../brainstorms/2026-05-01-tenant-id-envelope-requirements.md)
- Related code:
  - `src/Headless.Messaging.Abstractions/{Headers.cs, PublishOptions.cs, ConsumeContext.cs}`
  - `src/Headless.Messaging.Core/Internal/{IMessagePublishRequestFactory.cs, IConsumeExecutionPipeline.cs}`
  - `tests/Headless.Messaging.Core.Tests.Unit/IntegrationTests/IDirectPublisherIntegrationTests.cs`
- Related issues: #228 (this work), #217 (parent epic), #234 (`MissingTenantContextException`), #235 (tenant propagation filter), #236 (Mediator behavior), #237 (ProblemDetails handler).
- Institutional learnings:
  - `docs/solutions/guides/messaging-transport-provider-guide.md`
  - `docs/solutions/messaging/transport-wrapper-drift-and-doc-sync.md`
  - `docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md`
