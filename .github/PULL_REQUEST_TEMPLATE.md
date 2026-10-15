<!-- Title: type(scope)!: the outcome, not the mechanism. The ! marks a breaking change.
     Write in plain words: no filler, no internal jargon, no plan, review, or finding IDs. -->

<!-- Two sentences a reviewer can stop after: what is different now, and what was wrong before. -->

> **Rollout risk:** <!-- what can go wrong when this deploys, and how to back out of it. Write "none" if nothing can. -->

Fixes #

## What changes

<!-- One row per behavior a consumer or operator can observe, in their words. Each row should map to a test, so a
     reviewer can check the claim. Add a diagram only for a mechanism a table cannot show. -->

| Situation | Before | After |
| --- | --- | --- |

## Decisions you're approving

<!-- Delete if none. One line each: the choice, and the alternative you rejected with the reason. These are what a
     reviewer most needs and cannot get from the diff. -->

## Review map

<!-- Delete for a small PR. Where to start, in order, and the question to check at each stop. Never a list of
     changed files: the diff already shows those. -->

## Contract changes

<!-- Delete if none. One row per surface a consumer depends on: public or protected API, default, config key,
     schema, metric or span name, package ID. -->

| Surface | Change | Who breaks, and the migration |
| --- | --- | --- |

<!-- Then, collapsed: each added, changed, or removed member as a bare declaration marked // Added, // Removed, or
     // Changed (with a before and an after line), and the registration and call a consumer writes, copied from a
     test or docs sample that compiles. -->

<details><summary><strong>Full public API</strong></summary>

```csharp
```

</details>

## Verification

- Regression: <!-- the test that fails on main and passes here, and the behavior it holds -->
- Unit: <!-- make verify-affected on the head commit: result and counts -->
- Integration: <!-- make test-affected-integration on the head commit, or why it was not run. CI runs none. -->
- Not verified: <!-- each claim no check covers, and why. Delete if none. -->
- Coverage below floor: <!-- each warning the proof's coverage gate lists, with its reason (glue code,
  integration-only behavior, unreachable branch, cost above risk; see AGENTS.md). Delete if none. -->
- Docs: <!-- docs/llms/<domain>.md, or "none: internal" -->

<!-- Paste each proof's artifacts/proof/<run>/summary.md below, inside its own collapsed <details> block,
     so the prose above stays the first thing a reviewer reads. -->

## Left out

<!-- Delete if none. Related problems you saw and deliberately left, so a reviewer doesn't ask. -->
