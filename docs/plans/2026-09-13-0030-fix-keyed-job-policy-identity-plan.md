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

Means: exclude execution policy from business identity, retain the winning generation's captured policy, and use one fingerprint format through KTD2.

The user approved this design for issue #875 by invoking `x-autopilot` after reviewing it. Implementation, verification, commits, push, and an open PR are authorized. The issue supplies the acceptance criteria; repository contracts govern unchanged behavior. The user subsequently confirmed that this is greenfield and directed removal of legacy compatibility.

---

## Product Contract

### Summary and problem frame

The report is confirmed by source inspection at `6246ad639`. `JobScheduler.Keyed.cs` resolves host, function, and call options before creating a candidate. `JobIntentFingerprint.Compute` then hashes retries, intervals, and node-death policy. Both persistence implementations compare that hash with the retained generation. Consequently, identical contract version, payload bytes, and due instant can return `Conflict` solely because one host uses three retries and another uses five.

Recommendation: execution policy is operational configuration captured when a generation is created. A duplicate submission observes that generation. It does not renegotiate its execution policy.

### Key decisions

- KD1. Treat retry and node-death options as captured execution policy regardless of whether they came from host defaults, function defaults, or explicit call options. Governs R1, R2, R3. (session-settled: user-approved — chosen over policy-sensitive equality: host defaults must not break repeated submissions.)
- KD2. Use one policy-independent `v1` fingerprint format. Governs R5. (session-settled: user-directed; chosen over legacy compatibility because this is greenfield.)
- KD3. Observations retain the winning policy; explicit replacement captures a new policy under the existing generation fence. Governs R3, R4. (session-settled: user-approved — chosen over implicit policy edits on duplicates: a duplicate observes the existing generation.)

KD1 and KD3 were approved through the `x-autopilot` request. KD2 follows the user's greenfield clarification. A consumer requiring different business behavior must express it in the request, contract version, or business key. An operational policy change alone must not silently create another run.

### Requirements

| ID | Required behavior |
| --- | --- |
| R1 | Key lookup MUST remain scoped by final tenant/system scope, logical function name, and business key. Within that scope, identity MUST consist of contract version, exact durable request bytes after middleware, and the submitted UTC due instant normalized to microseconds. Null and empty requests remain distinct. |
| R2 | `Retries`, `RetryIntervals`, and `OnNodeDeath` MUST NOT affect `Existing` versus `Conflict`. Valid changes to host defaults, function defaults, and explicit call options all follow this rule. Candidate validation and atomic-enlistment requirements MUST still run; invalid options are not accepted merely because a key exists. |
| R3 | The first successful create MUST capture its resolved execution policy. Matching resubmissions MUST return the existing run ID, generation, and observed state without changing policy, fingerprint, metadata, or execution state. Concurrent matching creates with different policies MUST select one complete winning policy, never merge fields. |
| R4 | Explicit replacement MUST retain the existing generation fence and pending/unclaimed eligibility checks. A permitted replacement MUST create generation N+1 and capture the replacement call's resolved policy, even when its business identity equals generation N. Stale, missing, claimed, cancelled, and terminal cases retain their existing dispositions. Retained terminal keys MUST NOT rerun on observation. |
| R5 | New and replacement generations MUST use one policy-independent `v1` fingerprint format. Unknown algorithms MUST reject ordinary observation explicitly; generation-fenced replacement retains its existing behavior. |
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

### KTD2. Use one fingerprint format

(session-settled: user-directed; legacy compatibility is unnecessary for this greenfield project.) Implements KD2 through R5.

Keep one internal comparison owner in `JobIntentFingerprint`, used by both the memory provider and the EF keyed transaction kernel. Hash only the normalized incoming business identity and compare it with the stored fingerprint. Never recompute identity from stored execution fields, which may change during retries.

The `v1` encoding uses SHA-256, signed length prefixes, exact payload bytes, little-endian integer encoding, and microsecond normalization. The domain tag `headless-jobs-intent-v1` precedes contract version, payload, and normalized due ticks. Function, tenant, and business key remain enforced by the lookup scope. Retry and node-death policy are excluded. Unknown algorithms remain explicitly unsupported.

### KTD3. Preserve the existing transaction and generation protocol

Replace only the observation comparison in `JobsInMemoryPersistenceProvider.Keyed.cs` and `JobsEFCorePersistenceProvider.Keyed.cs`. New-generation stamping selects `v1`. Do not introduce another store read, lock, transaction, or scheduler-level precheck.

The EF private kernel already serves direct and caller-owned transaction paths. Both must receive the same comparison rule. An `Existing` result is still provisional until the caller's transaction commits where the current API says so.

### Alternatives considered

- Retain policy as identity: keeps current semantics but makes host configuration part of the deduplication contract. Callers cannot retry reliably across configuration changes. Not recommended.
- Ignore inherited policy but compare explicit policy: requires provenance capture and creates different identities for equal effective settings. Not recommended.
- Legacy compatibility: unnecessary for the confirmed greenfield scope. No migration or coordinated-upgrade process is required.

---

## Implementation Units

### U1. Define policy-independent identity

**Covers R1, R2, R5.** Modify `src/Headless.Jobs.Core/JobIntentFingerprint.cs` and `tests/Headless.Jobs.Composition.Tests.Unit/KeyedJobSchedulingTests.cs`.

Introduce shared read-only comparison and the policy-independent `v1` writer under KTD2. Pin its golden vector independently of the production encoder. Separate identity-changing inputs from ignored policy inputs.

Test version/payload/due differences; each policy field independently and combined; null versus empty payload; normalized equivalent instants; absent versus empty intervals; unknown algorithms; and unchanged candidate/stored objects. Include a stored non-idle row to prove the comparison does not apply new-candidate validation to it.

### U2. Integrate both providers and prove generation behavior

**Covers R2–R6. Depends on U1.** Modify the two keyed provider files named in KTD3 and extend `tests/Headless.Jobs.Tests.Harness/JobsKeyedSchedulingScenarios.cs`.

Wire shared comparison into observation and `v1` into creation/replacement. Run the shared matrix through `tests/Headless.Jobs.Composition.Tests.Unit/KeyedJobSchedulingTests.cs` and `tests/Headless.Jobs.EntityFramework.Tests.Harness/JobsKeyedSchedulingConformanceTests.cs`, inherited by the PostgreSQL and SQL Server leaf integration projects.

Add concurrent same-identity/different-policy submissions. Assert exactly one `Created`, all others `Existing`, one run/generation, and the winner's complete policy. Read storage after a duplicate and assert no field changed. Cover policy-only replacement, stale fences, claim races, and terminal observation. Add coverage in `tests/Headless.Jobs.EntityFramework.Tests.Harness/JobsTransactionalKeyedConformanceTests.cs` for coordinated observation/replacement and rollback.

### U3. Prove differently configured hosts and publish the contract in local docs

**Covers R1–R6. Depends on U2.** Extend `tests/Headless.Jobs.Composition.Tests.Unit/KeyedJobSchedulingTests.cs`, `tests/Headless.Jobs.EntityFramework.Tests.Harness/JobsKeyedSchedulingConformanceTests.cs`, and the existing provider-neutral scenario helper as needed.

Build two independently configured scheduler service providers targeting the same store. Exercise host defaults, function defaults, and explicit overrides through `IJobScheduler`, using the same descriptor, serialization, tenant, request, and fixed due instant. For memory, explicitly share the persistence instance; two independent memory stores cannot prove deduplication. For relational providers, use separate hosts connected to the same database. Reverse which host submits first, then exercise concurrent submission and restart observation. Assert the resolved winning policy persisted, not just the returned disposition. Reuse the existing fixtures rather than duplicate backend setup.

Update `src/Headless.Jobs.Abstractions/Models/JobOptions.cs`, keyed XML documentation in `src/Headless.Jobs.Abstractions/Interfaces/IJobScheduler.cs`, `src/Headless.Jobs.Core/README.md`, `docs/llms/jobs.md`, and `docs/solutions/guides/jobs-keyed-scheduling.md`. Explain explicit overrides on duplicates, replacement as the policy-change mechanism, and the single fingerprint format. Search other keyed documentation for statements that policy participates in identity and update affected statements only.

---

## Verification Contract

Implementation verification must run the focused composition tests and keyed integration suites for PostgreSQL and SQL Server through the repository's Makefile targets. Include the coordinated transaction suites because they share the changed EF kernel. Build the affected projects and run the required formatting/analyzer checks before shipping.

The acceptance matrix is memory, PostgreSQL, and SQL Server crossed with host-default differences, function-default differences, explicit overrides, matching identity, conflicting identity, and replacement. Include observation of retained generations. Shared tests must be discovered and executed by both relational leaf projects. A provider-only candidate test cannot substitute for the two-host scheduler tests required by R6.

## Definition of Done

All R1–R6 scenarios pass at their affected boundaries. Stored policy and fingerprint remain unchanged on observation, replacement retains its existing fences, and all named consumer documents describe the same contract. Record provider-specific failures or unavailable environments explicitly. The authorized shipping workflow commits and pushes the change, opens a PR, and watches CI to a decided outcome. Merge and deployment remain outside this request.
