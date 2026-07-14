---
title: Jobs middleware cross-assembly discovery uses assembly metadata
date: 2026-07-14
category: tooling-decisions
module: Jobs
problem_type: tooling_decision
component: source-generator
severity: medium
applies_when:
  - Implementing Jobs middleware registration in issue 305
  - Adding global or function-targeted middleware declarations
  - Changing generated JobFunction descriptor identity
tags: [jobs, middleware, source-generator, roslyn, metadata, incremental-generator]
related_components: [Headless.Jobs.SourceGenerator, Headless.Jobs]
---

# Jobs middleware cross-assembly discovery uses assembly metadata

## Decision

Implement issue [#305](https://github.com/xshaheen/headless-framework/issues/305) with assembly-level middleware metadata discovered by the application source generator. Do not add an explicit generated registration-hook mechanism.

`[JobFunction]` remains the sole handler authoring model. Function-targeted middleware refers to the durable function identity emitted in the generated `JobFunctionDescriptor`, coordinated with [#304](https://github.com/xshaheen/headless-framework/issues/304), rather than to a handler type.

## Evidence

The bounded prototype for [#302](https://github.com/xshaheen/headless-framework/issues/302) compiles a producer assembly, emits it to an in-memory metadata reference, then runs an incremental generator over a consumer compilation. The focused tests demonstrate that:

- `Compilation.Assembly.GetAttributes()` exposes declarations in the current assembly.
- `Compilation.GetAssemblyOrModuleSymbol(reference)` exposes assembly attributes and descriptor metadata from referenced assemblies without loading them.
- Global and function-targeted declarations from both boundaries compose into one generated call chain.
- Changing referenced priority or target identity changes the generated output; an unrelated consumer edit leaves it byte-identical.
- Generated output contains direct middleware calls. It performs no runtime assembly scan, reflection invocation, or expression compilation.

The explicit-hook candidate is technically derivable from a consumer marker and a well-known generated type. It is rejected because metadata already satisfies the complete discovery contract. A hook would add repeated application authoring, hide individual declarations behind another generated boundary, and complicate cross-assembly ordering and diagnostics without recovering a missing capability.

## Authoring and visibility contract

Middleware is declared as assembly metadata in the assembly that owns the middleware type. Referencing applications do not repeat those declarations. The application generator reads declarations from its current compilation and direct metadata references, normalizes them, then emits the application call chain.

Global declarations have no function target. Function-targeted declarations carry the generated descriptor identity value. The prototype uses the `[JobFunction]` function name, matching the durable identity direction in #304; #305 must consume #304's final generated descriptor representation rather than introduce a parallel identity.

The discovery boundary is compile time. Middleware added after the application compilation is not discovered dynamically, and runtime plugin scanning is intentionally unsupported.

## Ordering, identity, and diagnostics

Declarations are ordered by ascending `Priority`, then by stable middleware identity using ordinal comparison. Stable middleware identity is:

```text
<declaring assembly simple name>:<fully qualified middleware metadata type name>
```

This produces identical output when source or metadata-reference order changes.
For a targeted function, the generated function chain merges applicable global and targeted declarations before applying that total order; global scope is not an implicit higher-precedence bucket.

An exact duplicate of scope, target identity, priority, and stable middleware identity is a compile-time error and is emitted only once in the prototype call chain. A function-targeted declaration whose descriptor identity is absent is also a compile-time error and is excluded from generated calls. #305 should promote these prototype diagnostics into the production generator's diagnostic catalog.

## Consequences for issue #305

1. Define the production assembly metadata contract for global and function-targeted declarations.
2. Read current and referenced assembly attributes through Roslyn symbols.
3. Normalize descriptor identity using #304's generated contract.
4. Sort by priority and stable middleware identity before emitting direct calls.
5. Diagnose exact duplicates and unresolved targets at compile time.
6. Do not retain an explicit-hook fallback or introduce runtime discovery.

The prototype remains isolated in `Headless.Jobs.MiddlewareDiscovery.Spike.Tests.Unit`; it does not establish the final public attribute names or implement the production middleware pipeline.

## Executable evidence

- `MiddlewareDiscoverySpikeGeneratorTests` covers current/reference metadata, global and targeted calls, deterministic ordering, duplicates, missing targets, the rejected hook candidate, and forbidden runtime APIs.
- `IncrementalDiscoveryTests` covers referenced priority changes, target-identity changes, tracked discovery steps, and unrelated-edit output stability.

Run:

```bash
make test-project TEST_PROJECT=tests/Headless.Jobs.MiddlewareDiscovery.Spike.Tests.Unit/Headless.Jobs.MiddlewareDiscovery.Spike.Tests.Unit.csproj
```

## Related

- [#302](https://github.com/xshaheen/headless-framework/issues/302) — bounded discovery spike and decision contract.
- [#304](https://github.com/xshaheen/headless-framework/issues/304) — generated function descriptor identity.
- [#305](https://github.com/xshaheen/headless-framework/issues/305) — production middleware implementation that consumes this decision.
