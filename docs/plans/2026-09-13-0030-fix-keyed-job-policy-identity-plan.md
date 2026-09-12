---
title: Separate keyed job identity from execution policy
type: fix
date: 2026-09-13
artifact_contract: x-unified-plan/v1
artifact_readiness: implementation-ready
product_contract_source: github-issue-875
execution: code
origin: https://github.com/xshaheen/headless-framework/issues/875
---

# Separate keyed job identity from execution policy

## Goal Capsule

Callers can resubmit the same keyed job from differently configured hosts and observe the same generation without a policy-only conflict.

Means: exclude execution policy from business identity, retain the winning generation's captured policy, and support existing fingerprints through KTD2.

The user approved this design for issue #875 by invoking `x-autopilot` after reviewing it. Implementation, verification, commits, push, and an open PR are authorized. The issue supplies the acceptance criteria; repository contracts govern unchanged behavior. Implementation must stop if retained policy fields can change during execution, because the legacy comparison depends on their stability.

---

## Product Contract

### Summary and problem frame

The report is confirmed by source inspection at `6246ad639`. `JobScheduler.Keyed.cs` resolves host, function, and call options before creating a candidate. `JobIntentFingerprint.Compute` then hashes retries, intervals, and node-death policy. Both persistence implementations compare that hash with the retained generation. Consequently, identical contract version, payload bytes, and due instant can return `Conflict` solely because one host uses three retries and another uses five.

Recommendation: execution policy is operational configuration captured when a generation is created. A duplicate submission observes that generation. It does not renegotiate its execution policy.

### Key decisions

- KD1. Treat retry and node-death options as captured execution policy regardless of whether they came from host defaults, function defaults, or explicit call options. Governs R1, R2, R3. (session-settled: user-approved — chosen over policy-sensitive equality: host defaults must not break repeated submissions.)
- KD2. Apply the same observation semantics to retained `v1` generations through an explicit legacy comparison rule. Governs R5. (session-settled: user-approved — chosen over preserving strict legacy comparison: retained keys must receive the fix.)
- KD3. Observations retain the winning policy; explicit replacement captures a new policy under the existing generation fence. Governs R3, R4. (session-settled: user-approved — chosen over implicit policy edits on duplicates: a duplicate observes the existing generation.)

These decisions were approved through the subsequent `x-autopilot` request. A consumer requiring different business behavior must express it in the request, contract version, or business key. An operational policy change alone must not silently create another run.

### Requirements

| ID | Required behavior |
| --- | --- |
| R1 | Key lookup MUST remain scoped by final tenant/system scope, logical function name, and business key. Within that scope, identity MUST consist of contract version, exact durable request bytes after middleware, and the submitted UTC due instant normalized to microseconds. Null and empty requests remain distinct. |
| R2 | `Retries`, `RetryIntervals`, and `OnNodeDeath` MUST NOT affect `Existing` versus `Conflict`. Valid changes to host defaults, function defaults, and explicit call options all follow this rule. Candidate validation and atomic-enlistment requirements MUST still run; invalid options are not accepted merely because a key exists. |
| R3 | The first successful create MUST capture its resolved execution policy. Matching resubmissions MUST return the existing run ID, generation, and observed state without changing policy, fingerprint, metadata, or execution state. Concurrent matching creates with different policies MUST select one complete winning policy, never merge fields. |
| R4 | Explicit replacement MUST retain the existing generation fence and pending/unclaimed eligibility checks. A permitted replacement MUST create generation N+1 and capture the replacement call's resolved policy, even when its business identity equals generation N. Stale, missing, claimed, cancelled, and terminal cases retain their existing dispositions. Retained terminal keys MUST NOT rerun on observation. |
| R5 | New and replacement generations MUST use a distinct `v2` fingerprint. New code MUST observe retained `v1` generations under R1–R3 without modifying their stored hash or algorithm. Unknown algorithms MUST still reject ordinary observation explicitly; generation-fenced replacement retains its existing behavior. |
| R6 | Memory, PostgreSQL, and SQL Server MUST demonstrate the same contract through shared provider scenarios and actual scheduler calls from hosts with different defaults. Relational coordinated scheduling MUST use the same comparison and retain outer-transaction rollback semantics. |

### Observable examples

All rows below assume valid inputs, satisfied transaction requirements, and the same key scope.

| Submission | Result | Stored effect |
| --- | --- | --- |
| A creates with retries 3; B repeats identical identity with retries 5 | `Created`, then `Existing` | A's policy remains |
| B explicitly supplies different intervals or `OnNodeDeath` | `Existing` | No policy change |
| B changes request bytes, contract version, or normalized due instant | `Conflict` | No change |
| B replaces the observed eligible generation with identical identity and retries 5 | `Replaced` | New run, N+1, B's policy |
| B replaces with a stale expected generation | `StaleGeneration` | No change |
| B repeats after the current run succeeds or fails | `Existing` | No rerun |

No public overload, result disposition, policy-provenance field, live policy-update API, retention change, or schema change is proposed. Recurring jobs and unkeyed scheduling are outside this change.

---

## Planning Contract

### KTD1. Keep resolution and capture where they are

Keep `JobSchedulingPolicies.Resolve`, middleware execution, normalization, validation, and arbitration in their existing order. Policy is still needed to create or replace a generation. Removing policy from equality does not remove its validation or execution significance.

Keep the existing entity fields rather than introducing a public execution-policy record. The providers already retain the winning entity and return observation results without updating it. `RequireAtomicEnlistment` remains an invocation requirement, not a fingerprint component or a stored policy override.

### KTD2. Version the writer and centralize comparison

(session-settled: user-approved — chosen over changing the `v1` encoder in place or leaving retained keys policy-sensitive: distinct `v2` writes and read-only `v1` comparison preserve stored hashes while fixing observation.) Implements KD2 through R5.

Keep one internal comparison owner in `JobIntentFingerprint`, used by both the memory provider and the EF keyed transaction kernel. Providers continue to own atomic key selection and replacement, not hashing rules.

New `v2` encoding retains SHA-256, signed length prefixes, exact payload bytes, little-endian integer encoding, and microsecond normalization. Use a distinct domain tag, `headless-jobs-intent-v2`, followed by contract version, payload, and normalized due ticks. Remove only retries, interval count/values, and node-death policy. Function, tenant, and business key remain enforced by the lookup scope rather than duplicated in the hash.

For existing rows, branch on the stored algorithm:

- `v2`: hash the normalized incoming business identity and compare with the stored fingerprint.
- `v1`: preserve the original encoder byte-for-byte. Hash the incoming version, payload, and due instant together with the retained generation's retries, intervals, and node-death policy. Compare that result with the retained `v1` hash. This is an explicit change to comparison semantics, not a redefinition of `v1` encoding.
- Anything else: retain the explicit unsupported-algorithm exception.

The legacy projection MUST be read-only. Do not mutate either candidate or stored row to substitute policy. Do not validate the stored row as a new candidate; it may already be running or terminal. Do not recompute a hash from the stored row's current execution fields or deserialize and reserialize its request. The incoming due instant remains the caller's original scheduling instant.

This small compatibility path earns its place because keys and terminal generations are retained indefinitely. It avoids a data rewrite and fixes old keys as well as new ones. Its prerequisite is that stored one-shot policy fields retain their captured values. Current keyed mutation guards prohibit ordinary policy edits; implementation must check internal retry and recovery paths too.

### KTD3. Preserve the existing transaction and generation protocol

Replace only the observation comparison in `JobsInMemoryPersistenceProvider.Keyed.cs` and `JobsEFCorePersistenceProvider.Keyed.cs`. New-generation stamping selects `v2`. Do not introduce another store read, lock, transaction, or scheduler-level precheck.

The EF private kernel already serves direct and caller-owned transaction paths. Both must receive the same comparison rule. An `Existing` result is still provisional until the caller's transaction commits where the current API says so.

### Upgrade boundary

No database migration or background fingerprint conversion is required. New code reads `v1` and `v2`; all new generations use `v2`.

Old code rejects `v2` as unknown and still applies policy-sensitive equality to `v1`. Therefore, mixed-version keyed scheduling is unsupported. Deploy during a coordinated pause of keyed submissions and replacement, upgrade every process capable of those operations, then resume. Include worker processes that can schedule keyed jobs. A rollback to old scheduling code is unsafe after any retained `v2` generation is written; prefer a forward fix. Do not delete retained keys to enable rollback.

A reader-first, writer-second rollout would require a separate compatibility release or writer gate. It is not included because issue #875 does not require uninterrupted rolling upgrades. Confirm that operational requirement before implementation if a deployment needs it.

### Alternatives considered

- Retain policy as identity: keeps current semantics but makes host configuration part of the deduplication contract. Callers cannot retry reliably across configuration changes. Not recommended.
- Ignore inherited policy but compare explicit policy: requires provenance capture and creates different identities for equal effective settings. Not recommended.
- Change `v1` in place: invalidates stored hashes under the same algorithm label. Rejected.
- Write `v2` but preserve strict `v1` comparison: simpler comparison code, but old keys retain the reported problem indefinitely. Rejected.

---

## Implementation Units

### U1. Define versioned identity and legacy observation

**Covers R1, R2, R5.** Modify `src/Headless.Jobs.Core/JobIntentFingerprint.cs` and `tests/Headless.Jobs.Composition.Tests.Unit/KeyedJobSchedulingTests.cs`.

Introduce the shared read-only comparison and `v2` writer under KTD2. Preserve the existing `v1` golden vector. Add a `v2` golden vector derived independently of the production encoder. Separate identity-changing inputs from ignored policy inputs.

Test version/payload/due differences; each policy field independently and combined; null versus empty payload; normalized equivalent instants; absent versus empty intervals; unknown algorithms; legacy policy substitution; and unchanged candidate/stored objects. Include a stored non-idle row to prove the comparison does not apply new-candidate validation to it.

### U2. Integrate both providers and prove generation behavior

**Covers R2–R6. Depends on U1.** Modify the two keyed provider files named in KTD3 and extend `tests/Headless.Jobs.Tests.Harness/JobsKeyedSchedulingScenarios.cs`.

Wire shared comparison into observation and `v2` into creation/replacement. Run the shared matrix through `tests/Headless.Jobs.Composition.Tests.Unit/KeyedJobSchedulingTests.cs` and `tests/Headless.Jobs.EntityFramework.Tests.Harness/JobsKeyedSchedulingConformanceTests.cs`, inherited by the PostgreSQL and SQL Server leaf integration projects.

Add concurrent same-identity/different-policy submissions. Assert exactly one `Created`, all others `Existing`, one run/generation, and the winner's complete policy. Read storage after a duplicate and assert no field changed. Cover policy-only replacement, stale fences, claim races, and terminal observation. Seed genuine legacy `v1` rows through test-owned setup and assert identical behavior without changing their hashes. Add coverage in `tests/Headless.Jobs.EntityFramework.Tests.Harness/JobsTransactionalKeyedConformanceTests.cs` for coordinated observation/replacement and rollback.

### U3. Prove differently configured hosts and publish the contract in local docs

**Covers R1–R6. Depends on U2.** Extend `tests/Headless.Jobs.Composition.Tests.Unit/KeyedJobSchedulingTests.cs`, `tests/Headless.Jobs.EntityFramework.Tests.Harness/JobsKeyedSchedulingConformanceTests.cs`, and the existing provider-neutral scenario helper as needed.

Build two independently configured scheduler service providers targeting the same store. Exercise host defaults, function defaults, and explicit overrides through `IJobScheduler`, using the same descriptor, serialization, tenant, request, and fixed due instant. For memory, explicitly share the persistence instance; two independent memory stores cannot prove deduplication. For relational providers, use separate hosts connected to the same database. Reverse which host submits first, then exercise concurrent submission and restart observation. Assert the resolved winning policy persisted, not just the returned disposition. Reuse the existing fixtures rather than duplicate backend setup.

Update `src/Headless.Jobs.Abstractions/Models/JobOptions.cs`, keyed XML documentation in `src/Headless.Jobs.Abstractions/Interfaces/IJobScheduler.cs`, `src/Headless.Jobs.Core/README.md`, `docs/llms/jobs.md`, and `docs/solutions/guides/jobs-keyed-scheduling.md`. Explain explicit overrides on duplicates, replacement as the policy-change mechanism, `v1` comparison, `v2` encoding, and the upgrade boundary. Search other keyed documentation for statements that policy participates in identity and update affected statements only.

---

## Verification Contract

Implementation verification must run the focused composition tests and keyed integration suites for PostgreSQL and SQL Server through the repository's Makefile targets. Include the coordinated transaction suites because they share the changed EF kernel. Build the affected projects and run the required formatting/analyzer checks before shipping.

The acceptance matrix is memory, PostgreSQL, and SQL Server crossed with host-default differences, function-default differences, explicit overrides, matching identity, conflicting identity, and replacement. Include persisted `v1` and new `v2` observation. Shared tests must be discovered and executed by both relational leaf projects. A provider-only candidate test cannot substitute for the two-host scheduler tests required by R6.

No tests or builds ran during this design task. Source inspection confirms the current behavior and integration points; the proposed behavior remains unimplemented.

## Definition of Done

All R1–R6 scenarios pass at their affected boundaries. Stored policy and fingerprint remain unchanged on observation, replacement retains its existing fences, and all named consumer documents describe the same contract. Record provider-specific failures or unavailable environments explicitly. The authorized shipping workflow commits and pushes the change, opens a PR, and watches CI to a decided outcome. Merge and deployment remain outside this request.
