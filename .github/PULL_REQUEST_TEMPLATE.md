<!-- Title: type(scope)!: the outcome, not the mechanism. The ! marks a breaking change.
     Write in plain words: no filler, no internal jargon, no plan, review, or finding IDs.
     Budget: everything outside <details> stays under ~400 words, ~700 for a large PR. A reviewer reads the visible
     text; anything that only backs up a claim goes in a collapsed block. One PR per logical change, against main.
     When a large mechanical part (package deletions, renames, lockfiles, generated code) would bury the behavior
     change, open it as its own PR against main and merge it first. -->

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

### Screenshots

<!-- Delete when nothing a user sees changed. Required when a dashboard screen or other UI changes. Drag files into
     this box, or reference `![alt](./after.png)` in the body and pass `gh pr create|edit --attach './after.png#alt'`;
     never commit them. Take Before and After from the same route, data, and window size. Look at names what the
     screenshot proves: the element, state, or value a reviewer should check, not a description of the screen. -->

| Screen | Before | After | Look at |
| --- | --- | --- | --- |
| <!-- route or view · state --> | | | |

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

<!-- Then one diff block per owning type: each affected member as a bare declaration (no body or doc comments)
     inside its unchanged enclosing declaration, + for added, - for removed, and a changed member as a - line then a
     + line. Then the registration and call a consumer writes, copied from a test or docs sample that compiles. Keep
     it expanded: this block is already collapsed. -->

**Surface**

```diff
```

</details>

<details><summary><h2>Verification</h2></summary>

<!-- One collapsed block holding one table: nest no other collapsible or table inside it. When any check failed, add
     `open` to the tag above. Open with a line naming the head commit and the base: only checks on the head commit
     count, and an unchanged gate reuses its earlier pass in seconds. Condense each gate run to one row; never paste
     its summary.md, whose stage timings, whole-assembly coverage, and module lists bury what a reviewer approves on.
     The row gives the command, verdict, tests passed, failed, and skipped (with each skip's reason), compiler and
     analyzer findings, and changed-line coverage against the floor. A stage gets its own row only when it failed,
     warned, or fell below a floor, quoting its first error line or figure. Then a row each, leaving out any that
     does not apply:
     - `make test-affected-integration` when the affected set has integration projects, condensed the same way and
       naming the databases, brokers, or services it used; or why it did not run. CI runs no integration suites.
     - Regression: for a fix, `make prove`'s base and head results and the behavior the new tests hold. For new
       behavior with no base to fail, the one behavior that matters most. Name behavior, not test files.
     - Also checked: evidence no gate shows, at most three rows: a parity corpus, an EXPLAIN plan, a benchmark, or a
       run against a real service.
     - Not verified: each claim in Solution or Contract changes that no check exercised, and what would prove it.
     - Coverage below floor: per warning, the package, lines, figure against the floor, and the AGENTS.md reason with
       one sentence of rationale (name the integration test for integration-only behavior).
     - Unrelated failure: per failed test, its first error line, why this PR cannot reach it, and the issue tracking
       the flake. A passing rerun establishes a flake, not that this PR is innocent.
     A FAIL row stays FAIL: add the `make test-failed` row on the head commit under it, then the final run's row. -->

| Check | Result |
| --- | --- |
| | |

### Branch history

<!-- Delete if the branch has one commit worth describing. What each later commit added, and the checks run on it. -->

</details>
