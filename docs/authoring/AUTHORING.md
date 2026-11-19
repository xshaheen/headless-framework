# Documentation Templates

The rules for every `docs/llms` change live in [AGENTS.md](../../AGENTS.md#documentation): the reader, what stays out, guide shape and splitting, change routing, and the checks before commit. This file holds what only some changes need: the root README mirror, the compiled-examples harness, and the templates for a new guide, topic file, or package README.

## Root READMEs

`README.ar.md` is the one deliberate mirror. It carries every section of `README.md` in the same order: the introduction, the setup table, "What is in the box", and every package catalog section with the same packages, order, and links. Write its prose in Egyptian Arabic, keep technical terms and public names in English, and separate setup-table members with `،`. Code blocks, identifiers, links, and package names stay byte-identical to `README.md`.

The catalog summary states the package count. Keep it equal to the catalog rows and to the IDs in [`eng/expected-packages.txt`](../../eng/expected-packages.txt).

## Compiled examples

`tests/Headless.Docs.Examples.Tests.Unit` compiles every ```` ```csharp ```` block of `docs/llms/messaging.md`, `docs/llms/jobs.md`, and `docs/llms/dashboards.md`, and of every topic file under `docs/llms/messaging/`, `docs/llms/jobs/`, and `docs/llms/dashboards/`, as part of the unit suite, one test case per block, with the Messaging and Jobs source generators running. A failure names the guide and line, such as `docs/llms/jobs.md:556: CS1026: ) expected`.

- **Each block compiles alone.** Blocks never share declarations, so two blocks may declare the same type. Write using directives, assembly attributes, types, class members, and statements in the order a reader needs them; the harness moves statements and loose members into a method and class of their own.
- **Ambient names need no declaration.** A block may use the host objects and placeholder types its guide's prelude declares: `builder`, `services`, `app`, `configuration`, `provider`, `cancellationToken`/`ct`, plus per-guide names such as `setup` (inside `AddHeadlessMessaging`), `options` (inside `AddHeadlessJobs`), `scheduler`, and application types such as `OrderPlaced` or `AppDbContext`. The preludes live in the test project's `Preludes/` folder; add a name there rather than declaring setup the reader does not need. A block that declares a name itself uses its own.
- **Usings.** The prelude imports what an ASP.NET Core project imports implicitly and the namespaces of the guide's Headless packages. Write every other namespace in the block (`Microsoft.EntityFrameworkCore`, `Polly`, `OpenTelemetry.Trace`, a broker client), because a reader copying the block needs it.
- **`AddModule<X.MessagingModule>()` and `AddModule<X.JobsModule>()`.** The first module a block names sets the compilation's assembly name. When the block declares consumers or jobs, the generator must emit that module from them, and the block fails when it does not. Every other module the block names, and the first one when the block declares none, comes from an assembly it does not show and compiles against a stub.
- **Opting out.** ```` ```csharp no-compile ```` is the only recognized marker, and the test reports each marked block as skipped. [AGENTS.md](../../AGENTS.md#keep-docsllms-current) says when a block may use it.

To compile another guide, add it to the test project's `None` items and to the guide table in `DocExamplesCompileTests`, with a prelude for its ambient names.

## Domain guide template

Copy this block into `docs/llms/<domain>.md`, then remove sections the domain does not need.

````markdown
---
domain: <Domain Name>
packages: <Package.Suffix.One, Package.Suffix.Two>
---

# <Domain Name>

> <One-line factual summary.>

## Orientation

<Name the default path, entry-point abstractions, composition-root packages, and provider choices.>

## Agent Rules

- <One exact rule per bullet.>
- <Name the preferred API and the tempting incorrect alternative.>
- <State failure, ordering, cancellation, tenancy, or durability constraints that alter implementation.>

## Core Concepts

### <Concept>

<Define only vocabulary needed to choose or use the API correctly.>

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.<Provider>` | <Condition> | <Anti-condition> | <Accepted cost> |

---

## Headless.<Package>

<One sentence describing the package and its consumer.>

### Setup

```bash
dotnet add package Headless.<Package>
```

```csharp
// Minimal compiling registration and representative use.
```

**Verify:** <The observable proof the wiring works: `check` or `describe` output, a readiness health check, a `resources provision` result, or a test assertion.>

### Configuration

<Only options and defaults that affect consumer decisions.>

### Design and runtime behavior

<What the application observes: guarantees, side effects, exceptions, status codes, ordering, and operational constraints. A reason only when it changes a decision.>
````

A topic file of a split guide uses the same shape without Orientation and Agent Rules, and starts with `> Part of [<Domain>](../<domain>.md). <One-line summary.>`.

## Package READMEs

A package README is a landing page, not a second manual. Keep it under 35 lines unless a NuGet-specific warning must be visible before installation.

### Template

````markdown
# Headless.<Package>

<One sentence describing the package.>

## Why use this package

<The problem it removes and when this package is the right choice.>

## Install

```bash
dotnet add package Headless.<Package>
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [<Domain> guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/<domain>.md#headlesspackage)
````

Put setup, options, API lists, runtime effects, and provider limits in the domain guide. Use absolute GitHub links because nuget.org cannot reliably resolve repository-relative paths.
