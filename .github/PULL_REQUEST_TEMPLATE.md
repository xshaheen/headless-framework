<!-- Title: type(scope)!: the outcome, not the mechanism. The ! marks a breaking change.
     Write in plain words: no filler, no internal jargon, no plan, review, or finding IDs. -->

<!-- Two sentences a reviewer can stop after: what is different now, and what was wrong before. -->

## Problem

<!-- What was wrong or missing, and who it hurt, in the words of the person who hit it: the call, the input, and
     the wrong result. -->

> **Rollout risk:** <!-- what can go wrong when this deploys, and how to back out of it. Write "none" if nothing can. -->

Fixes #

## Solution

<!-- One sentence on what is different now. Then, when behavior changed, one row per behavior a consumer or operator
     can observe, each backed by a test. Add a diagram only for a mechanism a table cannot show. Don't restate the
     diff. -->

| Situation | Before | After |
| --- | --- | --- |

## Decisions

<!-- Delete if none. One line each: the choice, and the alternative you rejected with the reason. -->

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

| Check | Result on `<head commit>` |
| --- | --- |
| `make verify-affected` | |
| `make test-affected-integration` | <!-- or why it was not run; CI runs none --> |

- **Regression:** <!-- one line: the behavior the new tests hold that fails on main -->
- **Not verified:** <!-- each claim no check covers, and why. Delete if none. -->
- **Coverage below floor:** <!-- each warning the proof's coverage gate lists, with its reason (glue code,
  integration-only behavior, unreachable branch, cost above risk; see AGENTS.md). Delete if none. -->

<!-- Paste each proof's artifacts/proof/<run>/summary.md here, each inside its own collapsed <details> block. -->

## Left out

<!-- Delete if none. Related problems you saw and deliberately left, so a reviewer doesn't ask. -->
