<!-- Title: type(scope)!: the outcome, not the mechanism. The ! marks a breaking change.
     Write in plain words: no filler, no internal jargon, no plan, review, or finding IDs.
     Budget: everything outside <details> stays under ~400 words, ~700 for a large PR. A reviewer reads the visible
     text; anything that only backs up a claim goes in a collapsed block. Mechanical changes (package deletions,
     renames, lockfiles, generated code) belong in their own PR, stacked under the behavior change. -->

<!-- TL;DR, at most 35 words: what is different now, and what was wrong before. -->

**Review:** <!-- ~N min · risk low, medium, or high · the files a reviewer must read line by line -->

## Problem

<!-- What was wrong or missing, and who it hurt, in the words of the person who hit it: the call, the input, and
     the wrong result. -->

Fixes #

## Solution

<!-- One sentence on what is different now. Then, when behavior changed, one row per behavior a consumer or operator
     can observe, each backed by a test. When the change moves a flow, a lifecycle, or a dependency direction, add a
     mermaid diagram of the new shape. Don't restate the diff. -->

| Situation | Before | After |
| --- | --- | --- |

> **Rollout risk:** <!-- what can go wrong when this deploys, and how to back out of it. Write "none" if nothing can. -->

<details><summary><h2>Review guide</h2></summary>

<!-- The questions you want the reviewer to answer, most consequential first, each linked to the hunk that carries
     it (Files changed, line number, Copy permalink). Ask for a decision, not a reading order: "Is a 30 s lease the
     right default for a cold start?", not "start in LeaseStore.cs". Three to six rows. -->

| Decide | Where |
| --- | --- |
| | |

**Skim:** <!-- what is mechanical and why it is safe to skim, with file counts: moved files, renames, tests that only
follow a signature change, generated code, lockfiles. Delete if nothing. -->

**Least sure about:** <!-- the one or two places the author would check first if this broke after merge. -->

</details>

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

<details><summary><h2>Verification</h2></summary>

<!-- The state on the head commit only. How the branch got there (checks run on earlier commits, reruns, merges
     from main) goes in the collapsed Branch history block, never in these lines. -->

| Check | Result on `<head commit>` |
| --- | --- |
| `make verify-affected` | |
| `make test-affected-integration` | <!-- or why it was not run; CI runs none --> |

- **Regression:** <!-- one line: the behavior the new tests hold that fails on main -->
- **Not verified:** <!-- each claim no check covers, and why. Delete if none. -->
- **Coverage below floor:** <!-- each warning the proof's coverage gate lists, with its reason (glue code,
  integration-only behavior, unreachable branch, cost above risk; see AGENTS.md). Delete if none. -->
- **Unrelated failures:** <!-- each test that failed, with the evidence it also fails on main. Delete if none. -->

<!-- Paste each proof's artifacts/proof/<run>/summary.md here as it is. Each one is already its own collapsed
     <details> block with the verdict in its summary line, so do not wrap it again. -->

<details><summary>Branch history</summary>

<!-- Delete if the branch has one commit worth describing. What each later commit added, and the checks run on it. -->

</details>

</details>

## Left out

<!-- Delete if none. Related problems you saw and deliberately left, so a reviewer doesn't ask. -->
