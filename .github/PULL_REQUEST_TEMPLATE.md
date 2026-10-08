<!-- Title: type(scope)!: summary. The ! marks a breaking change. -->
<!-- Write it in noslop way, avoiding unnecessary jargon and keeping it clear and concise -->

## Problem

<!-- What is wrong or missing, and who it hurts. Name the
     call, the input, and the wrong result. -->

## Solution

<!-- The behavior a consumer gets now, and why this approach
     instead of the alternative you rejected. Don't restate
     the diff. No plan, review, or finding IDs. -->

Fixes #

## Public API

<!-- Delete if no public or protected API was added, changed,
     or removed. Show what a consumer sees, not the
     implementation, in up to three csharp blocks:

     1. Surface. Each added, changed, or removed member as a
        bare declaration (no body, no XML docs), grouped by
        type. Mark each line // Added, // Removed, or
        // Changed with a before and an after line.
     2. Usage. The registration and the call a consumer
        writes for the main scenario, and what it returns.
        Copy it from a test or docs sample that compiles.
     3. Contract. What a signature can't show, as bullets:
        result statuses, exceptions, defaults, ordering,
        thread safety, and the docs/llms section that owns it.

     Example:

     ```csharp
     // Added
     public enum LeaseTakeover { Allowed = 0, AfterSweep = 1 }

     public interface IFencedLeases
     {
         // Added
         ValueTask<LeaseFenceStatus> GetStatusAsync(FencedLease lease, CancellationToken cancellationToken = default);
     }

     public interface IUnitOfWorkLeases
     {
         // Changed
         // before: ValueTask<LeaseGrantResult> GrantAsync(IUnitOfWork unitOfWork, string kind, string resource, TimeSpan duration, CancellationToken cancellationToken = default);
         // after:  ValueTask<LeaseGrantResult> GrantAsync(IUnitOfWork unitOfWork, string kind, string resource, TimeSpan duration, LeaseTakeover takeover, CancellationToken cancellationToken = default);
     }
     ```

     ```csharp
     services.AddHeadlessFencing(setup => setup.UsePostgreSql(connectionString));

     var grant = await leases.GrantAsync("run", runId, TimeSpan.FromSeconds(30), LeaseTakeover.AfterSweep, ct);
     // grant.Status: Granted, Held, or Expired (the last attempt expired and waits for the sweep)
     ```
-->

## Breaking changes

<!-- Delete if none. One row per broken surface: API,
     behavior or default, config, schema, package ID. An
     API row points to its Public API entry instead of
     repeating the signature. -->
| Change | Consumer impact and migration |
| --- | --- |

## Verification

<!-- Only what CI and the proof below don't show: -->
- Regression: <!-- the test that fails on main -->
- Integration: <!-- make test-affected-integration
  result, or why it wasn't run. CI runs none. -->
- Manual: <!-- sandbox or browser walk, before → after.
  Delete if none. -->
- Unrelated failures: <!-- test name and evidence it
  fails on main. Delete if none. -->
- Coverage below floor: <!-- each warning the proof's coverage gate
  lists, with its reason (glue code, integration-only behavior,
  unreachable branch, cost above risk; see AGENTS.md). Delete if none. -->
- Docs: <!-- docs/llms/<domain>.md, or "none: internal" -->

<!-- Paste artifacts/proof/<run>/summary.md below.
     When the affected set has integration projects, paste
     make test-affected-integration's summary.md too: it checks
     those packages' changed lines against the coverage floors. -->

## Out of scope

<!-- Delete if none. Related problems you saw and left,
     so a reviewer doesn't ask. -->