---
title: "dotnet format fixer behavior behind make quality-fix"
date: 2026-09-18
last_updated: 2026-09-18
category: tooling-decisions
module: headless-framework
problem_type: tooling
component: build_tooling
severity: medium
tags:
  - dotnet-format
  - analyzers
  - csharpier
  - makefile
related_components:
  - Makefile
  - quality-fix
applies_when:
  - "Applying analyzer fixes in bulk"
  - "A quality-fix run leaves the tree not compiling"
---

# dotnet format fixers behind `make quality-fix`

`dotnet format` splits its fixers across two subcommands: `format analyzers` handles `MA*`, `RCS*`, and `CA*`,
and `format style` handles `IDE*`. It matches IDE rules only at `--severity hidden`, even for sites the report
lists as `info`. `make quality-fix` therefore runs both at `hidden` and bounds the blast radius with
`QUALITY_DIAGNOSTICS` instead of with severity. It refuses to run unfiltered.

Take one rule at a time and run `make rebuild` between rules.

## Four fixers to apply by hand

| Rule | What the fixer emits |
| --- | --- |
| `FAA0001` | `await` inserted into non-async lambdas |
| `FAA0002` | `await` inserted into non-async lambdas |
| `MA0045` | `.` applied to lambda expressions |
| `IDE0042` | PascalCase locals and unused variables |

None of those compile. Fix those four by hand.

Every fixer leaves mangled line breaks, so CSharpier runs last.
