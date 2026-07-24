# Residual Review Findings — xshaheen/jobs-tenants (#278 Jobs tenant propagation)

Source: x-code-review run `20260721-213859-79834ed6` (verdict: Ready with fixes) plus the planning-stage document review. All seven validated actionable findings were applied on-branch in `3d923c8ce` (and `24796da50` for the earlier plan-stage Codex findings); no actionable finding was deferred to a tracker.

## Settled-decision conflicts (proceeded and flagged)

- **Lateral tenant-to-tenant scheduling — resolved with the user (2026-07-22).** An explicit `TenantId` is honored even when it differs from a present ambient tenant (settled "explicit wins" decision, `user-directed`, issue #278); the planning-stage security review had flagged the lateral path (P1). Resolution: the default now logs a warning on the mismatch (`JobCrossTenantEnqueue` / `JobChainDescendantCrossTenant`), and hosts opt into hard rejection with `RejectCrossTenantEnqueue()` on the tenancy seam (posture Enforcing, clobber validator `HEADLESS_TENANCY_JOBS_REJECT_CROSS_TENANT_DISABLED`). Explicit values from system scope stay honored, so cron fan-out is unaffected. Messaging's publish middleware still has the unguarded explicit-wins shape — the opt-in twin belongs in the queued Messaging follow-up below.

## Validator-dropped findings (recorded for transparency)

- `#2` P1 `src/Headless.Jobs.Core/Managers/JobsManager.cs:783` — file crossed 1000 lines; partial-file split suggested. Dropped: file-organization preference, no project rule, no defect.
- `#4` P1 `src/Headless.Jobs.Core/MultiTenancy/TenantPropagationScheduleMiddleware.cs:43` — downstream middleware could clear a validated tenant before persistence. Dropped: requires a hypothetical consumer-authored middleware; covered by the documented in-process trust model.

## Informational residual risks

- **Off-by-default posture:** a multi-tenant app that registers Jobs but never calls `.Jobs(j => j.PropagateTenant())` schedules every job system-scope with no validator firing. Deliberate opt-in default (Messaging parity); mitigated by docs.
- **Split scheduler/worker topology:** no startup validator detects "rows will carry tenants but this host will not capture them." Partially mitigated by `fix(review)` making execute-side restoration data-driven (a persisted tenant is always restored).
- **Messaging sibling of the fixed fail-open predicate:** `TenantPropagationStartupValidator` in `src/Headless.Messaging.Core/SetupMessagingTenancy.cs` still treats any non-Messaging seam as a tenant source — the same fail-open shape fixed for Jobs in `24796da50`. Pre-existing on `main`; follow-up candidate.
- **Descriptor-missing compatibility branch:** `JobsExecutionTaskHandler` invokes `CachedDelegate` directly (bypassing execute middleware) when no descriptor exists; reachability for tenant-bearing jobs through the current descriptor-required API was not established (cross-model peer residual).
- **Chain projection depth:** pickup projections materialize three chain levels (root/child/grandchild); deeper descendants already lose projected fields — pre-existing limit, not tenant-specific.
