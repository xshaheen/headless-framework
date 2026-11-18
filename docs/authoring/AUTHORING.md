# Authoring Consumer Documentation

Headless has five documentation surfaces. Each has one job:

| Surface | Audience | Owns |
| --- | --- | --- |
| [`README.md`](../../README.md) | Evaluators and first-time users | Why Headless, first setup, package catalog |
| [`README.ar.md`](../../README.ar.md) | Arabic-reading evaluators | The Arabic mirror of `README.md` |
| [`docs/llms/index.md`](../llms/index.md) | Coding agents | Task routing and framework-wide invariants |
| `docs/llms/<domain>.md` | Coding agents using one domain in a consumer app | Package choice, setup, behavior, constraints, operations |
| `src/Headless.<Package>/README.md` | NuGet visitors | Why this package exists, installation, canonical links |

Do not mirror reference content between surfaces. The domain guide is the canonical consumer contract. Package READMEs point to it; the index routes to it; the root README explains the framework and catalogs packages.

Contributor notes (domain vocabulary, past fixes, and decisions) are kept outside this repository and are outside this contract.

## Root READMEs

`README.ar.md` is the one deliberate mirror. It carries every section of `README.md` in the same order: the introduction, the setup table, "What is in the box", and every package catalog section with the same packages, order, and links. Write its prose in Egyptian Arabic, keep technical terms and public names in English, and separate setup-table members with `،`. Code blocks, identifiers, links, and package names stay byte-identical to `README.md`.

The catalog summary states the package count. Keep it equal to the catalog rows and to the IDs in [`eng/expected-packages.txt`](../../eng/expected-packages.txt).

## Domain guides

### Write for an agent in a consumer app

The reader of `docs/llms/` is a coding agent working in an application that references Headless packages. It has the guide, the installed packages, and the application's code. It does not have this repository's source, tests, build scripts, or history, and it does not change the framework.

Every sentence answers one of these questions for that agent:

- **Choose:** which package, provider, or API fits this task, and what each choice costs.
- **Wire:** what to install, register, configure, and deploy, in order.
- **Call:** how application code uses the API, with the tempting wrong call named beside the right one.
- **Observe:** what the application sees at run time: defaults, ordering, retries, exceptions, status codes, health checks, metrics, and deploy-time verbs.
- **Verify:** how the agent proves the wiring works before it calls the task done.
- **Test:** how the application's own tests exercise the feature.

Content that answers none of them goes, or moves to contributor docs (the repository `AGENTS.md`). That covers rules for code under `src/`, analyzer and banned-symbol policy, test-harness internals, internal type names, and the history of a change.

**Keep a reason only when it changes a decision.** One clause of why helps the agent handle the case the rule did not foresee: "use `UseRedis`, because `UseInMemory` counts per process and N replicas admit N times the limit". Cut mechanism the consumer cannot act on: which internal tier a call routes through, how a type is laid out inside, or why the framework was built that way. When a mechanism shows up as observable behavior, state the behavior, not the mechanism.

**End every setup recipe with a Verify step.** Name the observable proof that the wiring works: the `check` or `describe` verb's output, a readiness health check that reports the feature, a `resources provision` result, a log line, or an assertion in the application's test. Write it as a bold `**Verify:**` lead-in after the recipe's code. A recipe without an observable proof says so: "**Verify:** no startup signal; the first call fails with `X` when the store is unreachable."

### Apply the writing standard

Write in the Diátaxis mode each section needs, and keep one mode per section: rules and recipes are how-to, configuration tables and behavior lists are reference, and a short concept section is explanation. Address the agent as "you", in the present tense. Write instructions as commands with the condition first. Put one instruction in each sentence. Keep "only" and "not" next to the word they change. Call each thing by one name across every guide, and use the public symbol name, not a paraphrase. Use plain words a developer says out loud. Prefer periods to semicolons and em dashes.

Prune for the agent's context. Delete a sentence that tells a capable model what it does by default. Keep each fact in one place and link to it from the others. Do not restate what the agent reads cheaply in the installed package: a full member list, a dependency list, or an XML doc comment.

### Shape a guide

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
2. `## Orientation`: the default path, entry-point abstractions, package roles, and provider choices.
3. `## Agent Rules`: one exact rule per bullet, each naming the right API and the tempting wrong one, or a constraint that changes the code the agent writes.
4. Concepts, decision tables, recipes, and operational reference needed across packages.
5. One `## Headless.<Package>` section per owned package: setup with its Verify step, configuration, and observable behavior.

Use exact public names. Keep examples compilable and version-free. Keep every `## Headless.<Package>` heading exactly as it is, because package READMEs link to its anchor. When you rename or move another heading, update every link to it in `docs/llms/`, `src/*/README.md`, `README.md`, and `README.ar.md`.

### Split a guide over 140 KB

A guide larger than about 140 KB costs an agent most of its context before it starts work. Split it into an entry file and topic files:

- `docs/llms/<domain>.md` stays the entry. It holds the frontmatter, the summary, Orientation, Agent Rules, the decision tables, the default setup with its Verify step, and a `## Topics` table.
- Each topic file is `docs/llms/<domain>/<topic>.md`, named by the task it serves (`retries.md`, `outbox.md`, `providers.md`). It starts with its own frontmatter, listing only the packages whose `## Headless.*` sections it holds, and a one-line summary that links back to the entry.
- The `## Topics` table has a row per topic file: a **Read when** column worded as the task that triggers it, and the link. An agent reads the entry on every task in the domain and a topic file only when its row matches.
- Put each fact in one file. A rule every task in the domain needs stays in the entry's Agent Rules, even when a topic file explains it further.
- Link to a topic file from outside the domain with its path and anchor (`messaging/outbox.md#…`). The index links to the entry only.

### Compiled examples

`tests/Headless.Docs.Examples.Tests.Unit` compiles every ```` ```csharp ```` block of `docs/llms/messaging.md`, `docs/llms/jobs.md`, and `docs/llms/dashboards.md`, and of every topic file under `docs/llms/messaging/`, `docs/llms/jobs/`, and `docs/llms/dashboards/`, as part of the unit suite, one test case per block, with the Messaging and Jobs source generators running. A failure names the guide and line, such as `docs/llms/jobs.md:556: CS1026: ) expected`. Run it with `make test-project TEST_PROJECT=tests/Headless.Docs.Examples.Tests.Unit/Headless.Docs.Examples.Tests.Unit.csproj`.

- **Each block compiles alone.** Blocks never share declarations, so two blocks may declare the same type. Write using directives, assembly attributes, types, class members, and statements in the order a reader needs them; the harness moves statements and loose members into a method and class of their own.
- **Ambient names need no declaration.** A block may use the host objects and placeholder types its guide's prelude declares: `builder`, `services`, `app`, `configuration`, `provider`, `cancellationToken`/`ct`, plus per-guide names such as `setup` (inside `AddHeadlessMessaging`), `options` (inside `AddHeadlessJobs`), `scheduler`, and application types such as `OrderPlaced` or `AppDbContext`. The preludes live in the test project's `Preludes/` folder; add a name there rather than declaring setup the reader does not need. A block that declares a name itself uses its own.
- **Usings.** The prelude imports what an ASP.NET Core project imports implicitly and the namespaces of the guide's Headless packages. Write every other namespace in the block (`Microsoft.EntityFrameworkCore`, `Polly`, `OpenTelemetry.Trace`, a broker client), because a reader copying the block needs it.
- **`AddModule<X.MessagingModule>()` and `AddModule<X.JobsModule>()`.** The first module a block names sets the compilation's assembly name. When the block declares consumers or jobs, the generator must emit that module from them, and the block fails when it does not. Every other module the block names, and the first one when the block declares none, comes from an assembly it does not show and compiles against a stub.
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

**Verify:** <The observable proof the wiring works: `check` or `describe` output, a readiness health check, a `resources provision` result, or a test assertion.>

### Configuration

<Only options and defaults that affect consumer decisions.>

### Design and runtime behavior

<What the application observes: guarantees, side effects, exceptions, status codes, ordering, and operational constraints. A reason only when it changes a decision.>
````

A topic file of a split guide uses the same shape without Orientation and Agent Rules, and starts with `> Part of [<Domain>](../<domain>.md). <One-line summary.>`.

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
