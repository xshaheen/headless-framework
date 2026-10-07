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

## Breaking changes

<!-- Delete if none. One row per broken surface: API,
     behavior or default, config, schema, package ID. -->
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
- Docs: <!-- docs/llms/<domain>.md, or "none: internal" -->

<!-- Paste artifacts/proof/<run>/summary.md below.
     Explain any changed assembly under 80% line coverage. -->

## Out of scope

<!-- Delete if none. Related problems you saw and left,
     so a reviewer doesn't ask. -->