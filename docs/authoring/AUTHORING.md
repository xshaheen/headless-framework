# Authoring Consumer Documentation

Headless has five documentation surfaces. Each has one job:

| Surface | Audience | Owns |
| --- | --- | --- |
| [`README.md`](../../README.md) | Evaluators and first-time users | Why Headless, first setup, package catalog |
| [`README.ar.md`](../../README.ar.md) | Arabic-reading evaluators | The Arabic mirror of `README.md` |
| [`docs/llms/index.md`](../llms/index.md) | Coding agents | Task routing and framework-wide invariants |
| `docs/llms/<domain>.md` | Coding agents using one domain | Package choice, setup, behavior, constraints, operations |
| `src/Headless.<Package>/README.md` | NuGet visitors | Why this package exists, installation, canonical links |

Do not mirror reference content between surfaces. The domain guide is the canonical consumer contract. Package READMEs point to it; the index routes to it; the root README explains the framework and catalogs packages.

`CONCEPTS.md` (contributor vocabulary) and `docs/solutions/` (past fixes and decisions) serve people working on this repository, not consumers, and are outside this contract.

## Root READMEs

`README.ar.md` is the one deliberate mirror. It carries every section of `README.md` in the same order: the introduction, the setup table, "What is in the box", and every package catalog section with the same packages, order, and links. Write its prose in Egyptian Arabic, keep technical terms and public names in English, and separate setup-table members with `،`. Code blocks, identifiers, links, and package names stay byte-identical to `README.md`.

The catalog summary states the package count. Keep it equal to the catalog rows and to the IDs in [`eng/expected-packages.txt`](../../eng/expected-packages.txt).

## Domain guides

Every first-party package has exactly one canonical domain guide. A guide starts with:

```yaml
---
domain: <Human-readable domain>
packages: <comma-separated package suffixes>
---
```

The `packages` field lists only packages with a `## Headless.*` section in that file. Do not list related packages owned by another guide.

Order content by the agent's decision path:

1. `# <Domain>` and a factual one-line summary.
2. `## Orientation`: entry points, package roles, and the default path.
3. `## Agent Rules`: exact constraints and high-cost footguns.
4. Concepts, decision tables, recipes, or operational reference needed across packages.
5. One `## Headless.<Package>` section per owned package.

Use exact public names. Keep examples compilable and version-free. State observable behavior, ordering, failure modes, and provider trade-offs where they change implementation decisions. Omit source-level inventories, direct dependency lists, and facts an agent can read cheaply from a project file.

Long guides are acceptable when the domain is intrinsically complex, but keep branches discoverable through descriptive H2/H3 headings. Split a guide only when tasks can load the new file independently through an explicit pointer.

### Compiled examples

`tests/Headless.Docs.Examples.Tests.Unit` compiles every ```` ```csharp ```` block of `docs/llms/messaging.md` and `docs/llms/jobs.md` as part of the unit suite, one test case per block, with the Messaging and Jobs source generators running. A failure names the guide and line, such as `docs/llms/jobs.md:556: CS1026: ) expected`. Run it with `make test-project TEST_PROJECT=tests/Headless.Docs.Examples.Tests.Unit/Headless.Docs.Examples.Tests.Unit.csproj`.

- **Each block compiles alone.** Blocks never share declarations, so two blocks may declare the same type. Write using directives, assembly attributes, types, class members, and statements in the order a reader needs them; the harness moves statements and loose members into a method and class of their own.
- **Ambient names need no declaration.** A block may use the host objects and placeholder types its guide's prelude declares: `builder`, `services`, `app`, `configuration`, `provider`, `cancellationToken`/`ct`, plus per-guide names such as `setup` (inside `AddHeadlessMessaging`), `options` (inside `AddHeadlessJobs`), `scheduler`, and application types such as `OrderPlaced` or `AppDbContext`. The preludes live in the test project's `Preludes/` folder; add a name there rather than declaring setup the reader does not need. A block that declares a name itself uses its own.
- **Usings.** The prelude imports what an ASP.NET Core project imports implicitly and the namespaces of the guide's Headless packages. Write every other namespace in the block (`Microsoft.EntityFrameworkCore`, `Polly`, `OpenTelemetry.Trace`, a broker client), because a reader copying the block needs it.
- **`AddModule<X.MessagingModule>()` and `AddModule<X.JobsModule>()`.** The first module a block names sets the compilation's assembly name, so the generator emits that module from the block's own consumers or jobs. Any other module the block names comes from an assembly it does not show and compiles against a stub.
- **Opting out.** Mark a block that cannot be a valid standalone fragment with ```` ```csharp no-compile ````, the only recognized marker. Use it for pseudo-code, a deliberately wrong example, or a fragment that depends on something the harness cannot supply, such as another assembly or a third-party source generator. Never use it to silence a block that is wrong. The test reports each marked block as skipped.

To compile another guide, add it to the test project's `None` items and to the guide table in `DocExamplesCompileTests`, with a prelude for its ambient names.

### Domain guide template

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

### Configuration

<Only options and defaults that affect consumer decisions.>

### Design and runtime behavior

<Non-obvious guarantees, side effects, failure modes, and operational constraints.>
````

## Package READMEs

A package README is a landing page, not a second manual. Keep it under 35 lines unless a NuGet-specific warning must be visible before installation.

### Package README template

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

## Index

`docs/llms/index.md` is a router, not a handbook or package catalog.

- Keep framework-wide invariants only.
- Route by task or capability, not by repository layout.
- Give every domain guide exactly one row of its own in the primary router. A row for a task that spans several domains may link guides that already have their own row.
- Keep package enumeration in the root README and package ownership in domain frontmatter.
- Put domain-specific rules in the domain guide, even if they are important.

## Change routing

Update documentation when a change affects public API, consumer-visible behavior, configuration, runtime effects, or package availability.

| Change | Required documentation |
| --- | --- |
| Package added or removed | Package README, owning domain guide (frontmatter and `## Headless.*` section), catalog row and package count in `README.md` and `README.ar.md`, `eng/expected-packages.txt`, index if routing changes |
| Package purpose or name | Package README, owning domain guide, catalog in `README.md` and `README.ar.md`, index if routing changes |
| Setup entry point or provider member | Owning domain guide, setup table in `README.md` and `README.ar.md` when the family is listed there |
| Registration, API, option, default, failure, ordering, or runtime effect | Owning domain guide |
| Framework-wide invariant | Index and affected domain guides |
| Internal refactor or tests only | None |

Before committing:

1. Confirm every `src/Headless.*/README.md` maps to exactly one domain guide.
2. Confirm each domain frontmatter package has one matching `## Headless.*` section.
3. Confirm `README.md` and `README.ar.md` list the same setup rows, catalog sections, and package links in the same order.
4. Check changed links and anchors.
5. Compile or otherwise verify changed code samples against the current public API.
6. Search removed or renamed public names across `docs/llms/`, package READMEs, and both root READMEs.

## Scoped safety rules

When documenting provider SDK types in options, preserve deliberate full-fidelity pass-throughs. Name the SDK type and coupling; do not invent a lossy wrapper only to hide a dependency.

For metrics and dashboards, use bounded dimensions and document the authorization boundary. Do not put payloads, raw headers, credentials, or free-form tenant values in examples. Distinguish atomic persistence from handler execution and external side effects; neither direct transport nor external effects are exactly once.
