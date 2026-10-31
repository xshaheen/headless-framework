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

## Decisions

<!-- Delete if none. One line each: the choice, and the alternative you rejected with the reason. -->

## Left out

<!-- Delete if none. Related problems you saw and deliberately left, so a reviewer doesn't ask. -->

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

<details><summary><h2>Contract changes</h2></summary>

<!-- Delete the whole block if none. One row per surface a consumer depends on: public or protected API, default,
     config key, schema, metric or span name, package ID. -->

| Surface | Change | Who breaks, and the migration |
| --- | --- | --- |

<!-- Then each added, changed, or removed member as a bare declaration marked // Added, // Removed, or // Changed
     (with a before and an after line), and the registration and call a consumer writes, copied from a test or docs
     sample that compiles. Keep it expanded: this block is already collapsed. -->

**Full public API**

```csharp
```

</details>

<details><summary><h2>Verification</h2></summary>

<!-- Every line here describes the head commit. Run both gates on it, against the same base: a result from an
     earlier commit is not a result, and a gate whose inputs did not change reuses its earlier pass in seconds. How
     the branch got there goes in the collapsed Branch history block, never in these lines. -->

| Check | Result on `<head commit>` against `<base: origin/main, or the parent branch of a stack>` |
| --- | --- |
| `make verify-affected` | <!-- PASS or FAIL: N passed, N failed, N skipped; N analyzer findings; changed lines N% line, N% branch --> |
| `make test-affected-integration` | <!-- the same shape, naming the databases or brokers it ran on; or why it did not run (CI runs none) --> |

<!-- A FAIL row stays FAIL: add a `make test-failed` row under it with the rerun's result. When a skipped count
     falls in a suite this PR changes, say why in the row. -->

- **Regression:** <!-- one sentence: the behavior the new tests hold that fails or does not compile on the base. For
  new behavior with no base to fail, the one behavior that matters most. Not a list of tests: the diff has them. -->
- **Also checked:** <!-- evidence no gate shows, one line each, at most three: a parity corpus, an EXPLAIN plan, a
  run against a real service. Delete if none. -->
- **Not verified:** <!-- each claim in Solution or Contract changes that no check exercised (a database version the
  test image lacks, a provider that needs credentials, a race no test forces), and what would show it. Delete if
  none. -->
- **Coverage below floor:** <!-- only the warnings the proof's coverage gate prints; delete if it printed none, and
  don't explain uncovered lines that are above the floor. One sub-bullet per warning: the package, the files and
  lines, the figure against the floor, the reason in bold (**Glue code**, **Integration-only behavior**,
  **Unreachable branch**, **Cost above risk**; see AGENTS.md), and one sentence why. For integration-only behavior,
  name the integration test that covers it. For a whole-assembly unit warning on code this PR does not change,
  give this PR's changed-line figure and say the shortfall predates it. -->
- **Unrelated failures:** <!-- one sub-bullet per failed test: its first error line, why this PR cannot reach it, and
  the issue that tracks it as a flake (open one if none exists). A rerun that passes shows the test is flaky, not
  that this PR is innocent. Delete if none. -->

<!-- Paste each proof's artifacts/proof/<run>/summary.md here as it is. Each one is already its own collapsed
     <details> block with the verdict in its summary line, so do not wrap it again. -->

<details><summary>Branch history</summary>

<!-- Delete if the branch has one commit worth describing. What each later commit added, and the checks run on it. -->

</details>

</details>
