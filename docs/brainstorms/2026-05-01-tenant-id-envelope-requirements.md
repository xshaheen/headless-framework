---
date: 2026-05-01
topic: tenant-id-envelope
issue: https://github.com/xshaheen/headless-framework/issues/228
parent_epic: https://github.com/xshaheen/headless-framework/issues/217
---

# TenantId — First-Class Envelope Property

Phase 1 / U2 of the Headless.Messaging epic (#217). Promotes `TenantId` from an informal header convention to a typed, first-class envelope property with a strict publish-time integrity policy.

## Problem Frame

`ConsumeContext.Headers` already documents `TenantId` as a "common header key (by convention)" but the framework ships no typed surface and no integrity check. Every consumer threads `headers["TenantId"]` ad-hoc, with no shared key constant, no validation, and no defense against header injection at the publisher boundary. The cross-layer tenancy story (EF write guard #234, Mediator behavior #236, tenant-propagation filter #235, ProblemDetails handler #237) all assumes a typed envelope value to anchor on, but the envelope itself is missing.

Within #217's wider Phase 1 scope, U2 was originally specified to include `DeliveryKind`, an `AllowHeaderTenantHydration` opt-in, and the `TenantContextRequired` strict-mode validator. Brainstorming on 2026-05-01 narrowed the scope to envelope plumbing only — see Scope Boundaries below.

## Requirements

- **R1.** `PublishOptions.TenantId : string?` (init-set). When set, value is non-whitespace and ≤200 characters. When unset, the property is `null` and no header is written.
- **R2.** `ConsumeContext<TMessage>.TenantId : string?` (init-set, **not** `required`). Populated by the consume pipeline from `headers["headless-tenant-id"]`; `null` when the header is absent or empty.
- **R3.** `Headers.TenantId = "headless-tenant-id"` constant ships in `Headless.Messaging.Abstractions`. The wire-header key is canonical; no other key is recognized.
- **R4.** Validation matches the existing `MessageId` / `CorrelationId` pattern: max 200 chars, non-whitespace when set, no charset enforcement. Charset sanitization is documented as a consumer-app responsibility (TenantId values flow into URLs, SQL columns, OTel tags, log lines).
- **R5.** **Publish-time strict 4-case integrity policy**, enforced in the shared publish wrapper in `Headless.Messaging.Core` so all 11 transports inherit it without per-provider duplication:
  - **(a)** `PublishOptions.TenantId` set, raw header unset → wrapper stamps the header from the property and emits.
  - **(b)** `PublishOptions.TenantId` unset, raw header set → publish is **rejected** with `InvalidOperationException`. The raw header is reserved for transport-internal use; bypassing the typed property is treated as a caller bug or an injection attempt.
  - **(c)** Both set, equal → emit (no-op reconciliation).
  - **(d)** Both set, **disagree** → publish is **rejected** with `InvalidOperationException` carrying both values in the message.
- **R6.** Consume-side is lenient (cannot reject what is already on the wire): a malformed or oversized header value maps to `ConsumeContext.TenantId = null` rather than failing the whole message. (Phase 2 may revisit by surfacing malformed envelopes through `IDeadLetterObserver`.)
- **R7.** No new exception types, no new options, no new DI seams. Just three property additions, one header constant, and the wrapper logic.

## Success Criteria

- A caller setting `PublishOptions.TenantId = "acme"` produces `headers["headless-tenant-id"] = "acme"` on the wire (verified via `InMemoryStorage` round-trip).
- A consume pipeline wrapping a fake transport with `headers["headless-tenant-id"] = "acme"` yields `ConsumeContext.TenantId == "acme"`.
- A caller writing `headers["headless-tenant-id"]` directly without the typed property is rejected at publish with a clear `InvalidOperationException`.
- A caller with `PublishOptions.TenantId = "acme"` AND `headers["headless-tenant-id"] = "acme-evil"` is rejected at publish with both values reported.
- Publishing with `TenantId = null` succeeds; the header is not written; consume side observes `ConsumeContext.TenantId == null`.
- `grep -r '"headless-tenant-id"' src/Headless.Messaging.* | grep -v Headers.cs` returns zero results — only `Headers.cs` knows the key.

## Scope Boundaries

**Removed from #228 (moved or deferred):**

- **`DeliveryKind` property on `ConsumeContext`** — moves to U1 (Phase 2). Adding a single-valued enum now without the publisher split that gives it meaning is premature.
- **`AllowHeaderTenantHydration` opt-in** — killed. The `Security Considerations §1` section in #217 specs case (d) as "emit when `AllowHeaderTenantHydration = true`," which directly contradicts U2's strict-rejection policy. Resolved in favor of strict (no opt-in).
- **`TenantContextRequired` startup validator** — moves to a sibling follow-up issue. That validator is a *missing-tenant* enforcement (`MissingTenantContextException` from #234), distinct from #228's *header-injection / publisher-misuse* concern (`InvalidOperationException`). They are the same family but different failure semantics; bundling them blurs the boundary.
- **Per-provider composite-key `(TenantId, MessageId)` dedup migration** — Phase 2 (U3a/U3b). Phase 1 ships the envelope-visible `TenantId`; dedup-correctness arrives when the per-provider migrations land.
- **Inbound envelope signing / cross-tenant authenticity** — Phase 2 (`IConsumeBehavior<T>`). #228 trusts the publisher; consumer apps with cross-tenant trust boundaries layer their own check.

## Dependencies and Sibling Issues

- **No upstream dependency.** #228 is the foundation.
- **#235 depends on #228.** `TenantPropagationOutboxPublisher` must stamp the typed `PublishOptions.TenantId` (not a hand-rolled `headers["TenantId"]` key) and `TenantPropagationConsumeFilter` must read `ConsumeContext.TenantId`. Wire-header key conflict in #235's spec must be resolved when #235 lands.
- **Sibling: messaging strict-tenancy publish guard** (new follow-up issue) — sibling of Mediator's #236 and EF's #234. Throws `MissingTenantContextException` from #234 when ambient tenant is required but absent on publish.
- **Cross-layer exception**: `MissingTenantContextException` is defined in #234 (EF write guard) and reused by #236 (Mediator behavior), the new messaging strict-tenancy follow-up, and caught by #237 (ProblemDetails handler). #228 itself does **not** throw this exception — its publish-time mismatch is `InvalidOperationException` because the failure semantics are different (header injection, not missing context).

## Test Plan

Three test layers, all in existing projects:

- **`Headless.Messaging.Abstractions.Tests.Unit`** — TenantId property validation: length>200 throws, whitespace-when-set throws, null is allowed, equality round-trip preserved.
- **`Headless.Messaging.Core.Tests.Unit`** — 4-case publish-pipeline matrix: each of (a)-(d) verified independently; null-tenant publish writes no header.
- **`Headless.Messaging.InMemoryStorage.Tests.Unit`** — publish→consume round-trip: `ConsumeContext.TenantId == set value`; null when unset; oversized/malformed inbound header maps to null on consume.

## Files Touched

- `src/Headless.Messaging.Abstractions/PublishOptions.cs` — add `TenantId` init-set property with `_TenantIdMaxLength` const set to 200 and the same null/whitespace contract used by `CorrelationId`.
- `src/Headless.Messaging.Abstractions/ConsumeContext.cs` — add `TenantId` init-set property (not `required`).
- `src/Headless.Messaging.Abstractions/Headers.cs` — add `TenantId = "headless-tenant-id"` constant with XML doc.
- `src/Headless.Messaging.Core/` — add the 4-case wrapper. Exact wiring point selected during planning.
- Tests as listed above.

## Acceptance Criteria

- [ ] All success-criteria scenarios above pass.
- [ ] XML docs updated on every modified public type.
- [ ] No transport-specific code parses the tenant header directly (`grep` check from Success Criteria).
- [ ] #235 is updated in its body to depend on #228 and use the typed property.
- [ ] Sibling follow-up issue (messaging strict-tenancy publish guard) opened and cross-linked.
- [ ] #217 amended: `AllowHeaderTenantHydration` references removed; strict policy noted as the resolved decision.
