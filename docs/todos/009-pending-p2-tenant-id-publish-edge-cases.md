---
status: pending
priority: p2
issue_id: "009"
tags: [security, messaging, multi-tenancy]
dependencies: []
---

# Tighten `_ApplyTenantId` edge cases: case-variant header keys and whitespace-raw spec drift

## Problem Statement

Two design-level edge cases in `Headless.Messaging.Core.Internal.MessagePublishRequestFactory._ApplyTenantId` need a deliberate decision before tightening:

1. **Case-variant header key bypass.** When a caller's `PublishOptions.Headers` source dict uses a case-insensitive comparer (e.g., `OrdinalIgnoreCase`), the factory's copy step `new Dictionary<string,string?>(source, StringComparer.Ordinal)` preserves the original key casing. A caller can supply `Headers["Headless-Tenant-Id"] = "evil"` which the ordinal lookup `headers.TryGetValue("headless-tenant-id", ...)` misses. The variant-cased key then rides through `_ValidateCustomHeaders` (which also uses `StringComparer.Ordinal`) as a custom header. Whether downstream transports normalize the case — and therefore whether this is exploitable as a smuggled tenant header — varies by transport.
2. **Whitespace-raw spec drift.** Plan R5(b) states "raw header set → reject," but the implementation treats `Headers["headless-tenant-id"] = "  "` (whitespace) as unset (`rawSet = rawPresent && !IsNullOrWhiteSpace(raw)`). The whitespace path silently strips the key without throwing. This is intentional symmetry with R6 consume-side leniency, but the spec text contradicts it.

## Findings

- **File:** `src/Headless.Messaging.Core/Internal/IMessagePublishRequestFactory.cs:73-127` (header copy and `_ApplyTenantId`)
- **Status:** Identified during PR #239 code review (run `20260502-89e0ff79`); flagged by adversarial, correctness, strict-.NET reviewers
- **Priority:** p2 (header-injection-adjacent; bounded by transport behavior)

## Proposed Solutions

### Case-variant key bypass (security-leaning)

1. **Validate via case-insensitive lookup**: extend `_ValidateCustomHeaders` to scan keys with `StringComparer.OrdinalIgnoreCase` for reserved-header matches (rather than the current ordinal scan). Reject any case variant of a reserved key with the existing `InvalidOperationException`. Lowest blast-radius; consistent with the strict policy.
2. **Normalize the copy**: replace `new Dictionary<string,string?>(source, StringComparer.Ordinal)` with a manual loop that lowercases keys matching reserved-header values. More invasive; affects all reserved-header handling.
3. **Document and accept**: add a remark to `Headers.TenantId` and `PublishOptions.Headers` XML docs noting that callers must use ordinal/case-correct keys. Lowest cost; relies on caller discipline.

Recommend (1) — minimal change, correct semantics.

### Whitespace-raw spec drift (consistency-leaning)

1. **Tighten case (b) to fire on whitespace too**: change `rawSet = rawPresent && !IsNullOrWhiteSpace(raw)` to `rawSet = rawPresent` so any present raw header (even whitespace) without a typed property triggers rejection. Aligns code with spec text; loses the symmetry with R6 consume leniency.
2. **Update spec text and XML docs to describe the actual asymmetry**: keep the implementation; clarify that whitespace raw values are treated as unset on both publish and consume sides for symmetry. Document this is a deliberate design choice, not a bug.

Recommend (2) — the symmetry is the more valuable invariant, and the silent strip is harmless once documented.

## Acceptance Criteria

- [ ] Decide between options (1) and (2) for the case-variant bypass; implement.
- [ ] Decide between options (1) and (2) for the whitespace-raw drift; implement.
- [ ] Tests added covering whichever direction is chosen.
- [ ] If (2) for either, update the plan doc, brainstorm doc, and `Headers.TenantId` XML doc to match.
