# Residual Review Findings — xshaheen/jobs-tenants (#278 Jobs tenant propagation)

Source: x-code-review run `20260721-213859-79834ed6` (verdict: Ready with fixes) plus the planning-stage document review. All seven validated actionable findings were applied on-branch in `3d923c8ce` (and `24796da50` for the earlier plan-stage Codex findings); no actionable finding was deferred to a tracker.

## Settled-decision conflicts (proceeded and flagged)

- **Lateral tenant-to-tenant scheduling stays open by design.** An explicit `TenantId` is honored even when it differs from a present ambient tenant (settled "explicit wins" decision, `user-directed`, issue #278). The planning-stage security review flagged the lateral path (P1); it proceeded under the documented in-process trust model (any code in the process already holds `ICurrentTenant.Change`; Messaging publish middleware has the identical shape). Conflict note recorded on the capture Key Decision in `docs/plans/2026-07-21-001-feat-jobs-tenant-propagation-plan.md`. Revisit only if cross-tenant scheduling from tenant context should become a rejected contradiction.

## Validator-dropped findings (recorded for transparency)

- `#2` P1 `src/Headless.Jobs.Core/Managers/JobsManager.cs:783` — file crossed 1000 lines; partial-file split suggested. Dropped: file-organization preference, no project rule, no defect.
- `#4` P1 `src/Headless.Jobs.Core/MultiTenancy/TenantPropagationScheduleMiddleware.cs:43` — downstream middleware could clear a validated tenant before persistence. Dropped: requires a hypothetical consumer-authored middleware; covered by the documented in-process trust model.

## Informational residual risks

- **Off-by-default posture:** a multi-tenant app that registers Jobs but never calls `.Jobs(j => j.PropagateTenant())` schedules every job system-scope with no validator firing. Deliberate opt-in default (Messaging parity); mitigated by docs.
- **Split scheduler/worker topology:** no startup validator detects "rows will carry tenants but this host will not capture them." Partially mitigated by `fix(review)` making execute-side restoration data-driven (a persisted tenant is always restored).
- **Messaging sibling of the fixed fail-open predicate:** `TenantPropagationStartupValidator` in `src/Headless.Messaging.Core/SetupMessagingTenancy.cs` still treats any non-Messaging seam as a tenant source — the same fail-open shape fixed for Jobs in `24796da50`. Pre-existing on `main`; follow-up candidate.
- **Descriptor-missing compatibility branch:** `JobsExecutionTaskHandler` invokes `CachedDelegate` directly (bypassing execute middleware) when no descriptor exists; reachability for tenant-bearing jobs through the current descriptor-required API was not established (cross-model peer residual).
- **Chain projection depth:** pickup projections materialize three chain levels (root/child/grandchild); deeper descendants already lose projected fields — pre-existing limit, not tenant-specific.
