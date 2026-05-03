---
status: pending
priority: p2
issue_id: "010"
tags: [testing, messaging]
dependencies: []
---

# Test quality improvements for TenantId envelope round-trip and unit tests

## Problem Statement

PR #239's new tests are functionally correct but have three quality concerns flagged across reviewers:

1. **Static-state coupling in round-trip tests.** `IDirectPublisherIntegrationTests` reuses the static `DirectTestConsumerWithHeaders.ReceivedContexts` collection across tests. `InitializeAsync` resets it, but xUnit v3's parallel test collections + shared static state remains a known flake source. The two new round-trip tests assert `HaveCount(1)` against this collection without per-test isolation.
2. **No round-trip coverage of case (c).** Round-trip tests cover `TenantId = "acme"` (case a) and `TenantId = null` (no header). Missing: `TenantId = "acme"` AND `Headers[Headers.TenantId] = "acme"` set together (case c — both equal). Publisher-side unit test covers it; the end-to-end stamping invariant is unverified.
3. **Hardcoded magic strings.** The 9+ new tests in `DirectPublisherTests` use `"acme"`, `"acme-evil"`, `"evil"`, `"demo"` directly. No shared test constants; refactoring test data is more tedious than it needs to be.

## Findings

- **Files:**
  - `tests/Headless.Messaging.Core.Tests.Unit/IntegrationTests/IDirectPublisherIntegrationTests.cs` (round-trip tests; static state)
  - `tests/Headless.Messaging.Core.Tests.Unit/DirectPublisherTests.cs` (9 new tests with hardcoded strings)
- **Status:** Identified during PR #239 code review (run `20260502-89e0ff79`); flagged by strict-.NET, testing, maintainability reviewers
- **Priority:** p2

## Proposed Solutions

### Static-state isolation

1. **Use xUnit `ICollectionFixture` or `[Collection]` attribute** to force serial execution of integration tests that share `DirectTestConsumerWithHeaders.ReceivedContexts`. Lowest cost.
2. **Refactor `DirectTestConsumerWithHeaders` to instance-state** with DI-resolved capture. Higher cost but eliminates the shared mutable state.

Recommend (1) — small ceremony, no behavior change.

### Round-trip case-(c) coverage

Add `should_round_trip_tenant_id_when_typed_property_and_raw_header_agree` to `IDirectPublisherIntegrationTests`:

- Publish with `PublishOptions { TenantId = "acme", Headers = { [Headers.TenantId] = "acme" } }`.
- Assert `ctx.TenantId == "acme"` and `ctx.Headers[Headers.TenantId] == "acme"` (single-stamping; no duplicate header).

### Magic strings

Introduce a private nested constants class in `DirectPublisherTests`:

```csharp
private static class TestTenants
{
    public const string Valid = "acme";
    public const string Conflicting = "acme-evil";
    public const string RawOnly = "evil";
}
```

Replace inline literals across the 9 new tests.

## Acceptance Criteria

- [ ] Static-state isolation applied (collection fixture or refactor).
- [ ] Case-(c) round-trip test added.
- [ ] Test constants extracted; no `"acme"` / `"acme-evil"` / `"evil"` literals in the test bodies.
- [ ] All existing tests still pass.
