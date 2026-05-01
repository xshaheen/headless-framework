---
status: pending
priority: p1
issue_id: "008"
tags: [testing, messaging, multi-tenancy]
dependencies: []
---

# Add consume-side test coverage for `_ResolveTenantId` lenient policy and boundary

## Problem Statement

Two related coverage gaps remain on `Headless.Messaging.Core.Internal.ConsumeExecutionPipeline._ResolveTenantId` after PR #239:

1. **R6 oversized-on-consume → null** lenient mapping has no direct test. The original plan (`docs/plans/2026-05-01-001-feat-tenant-id-envelope-plan.md`, U4) listed `should_map_oversized_inbound_tenant_id_to_null_on_consume` but the test was deferred during implementation on the (incorrect) premise that it required `InternalsVisibleTo` violations or reflection. Reviewers (correctness, testing, api-contract, prev-comments, learnings, project-standards, maintainability, pragmatic-.NET, strict-.NET) all flagged this as the highest-priority residual.
2. **Boundary at exactly `PublishOptions.TenantIdMaxLength` (200 chars)** is unverified on the consume side. The publish-side has `should_allow_maximum_supported_tenant_id_length` (`DirectPublisherTests`); the parallel consume-side assertion is missing.

## Findings

- **File:** `src/Headless.Messaging.Core/Internal/IConsumeExecutionPipeline.cs:233-249` (the `_ResolveTenantId` static helper)
- **Status:** Identified during PR #239 code review (run `20260502-89e0ff79`)
- **Priority:** p1

## Proposed Solutions

`Headless.Messaging.Core.csproj` already declares `InternalsVisibleTo` for `Headless.Messaging.Core.Tests.Unit`, so direct testing is available without violating project test conventions:

1. **Use the existing `SubscribeInvokerTests._CreateMediumMessage` pattern** (in `tests/Headless.Messaging.Core.Tests.Unit/SubscribeInvokerTests.cs`). It already constructs `MediumMessage` instances with arbitrary header dictionaries and dispatches them through the public `IMessageDispatcher`, so the cached `ConsumeContext<T>` factory exercises `_ResolveTenantId` end-to-end. Add three tests:
   - `should_populate_tenant_id_from_inbound_header` (happy path; complements existing round-trip)
   - `should_handle_oversized_tenant_id_leniently_on_consume` (set raw header to `new string('x', PublishOptions.TenantIdMaxLength + 1)`, expect `ctx.TenantId == null`)
   - `should_handle_max_length_tenant_id_on_consume` (set raw header to `new string('x', PublishOptions.TenantIdMaxLength)`, expect `ctx.TenantId` equals the value)
2. **Alternative** (if pattern (1) is awkward): expose `_ResolveTenantId` as `internal static` and unit-test directly. Less preferred because project standards favor testing through public APIs.

## Acceptance Criteria

- [ ] Three new tests in `Headless.Messaging.Core.Tests.Unit` exercising `_ResolveTenantId` for happy path, oversized → null, and exact 200-char boundary.
- [ ] Tests pass without modifying production-code visibility.
- [ ] Total `Headless.Messaging.Core.Tests.Unit` count increases by 3.
