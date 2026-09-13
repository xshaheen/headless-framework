<!--
Starting point for docs/llms/<domain>.md. Copy this file, then replace every
<placeholder>. Structure follows the information hierarchy — lead with the map,
then the rules, then the packages. Drop any section the domain does not need;
there is no fixed section list to reproduce verbatim.

Read docs/authoring/AUTHORING.md before editing any file under docs/llms/.
-->

---
domain: <Domain Name>
packages: <Package.Suffix.One, Package.Suffix.Two>
---

# <Domain Name>

> <One-line factual summary of the domain. No marketing adjectives.>

## Orientation

<2-4 sentences: the problem this domain solves, the entry-point interfaces and
key types, and when to pick which provider. This is the map an agent reads
first — keep it skimmable.>

## Agent Rules

- <One rule per bullet. State the do AND the don't where relevant.>
- <Reference abstraction interfaces by exact name (e.g. `ICache` from `Headless.Caching.Abstractions`).>
- <Call out footguns: things that look right but break — cancellation semantics, threading, ordering, defaults.>
- <Name banned alternatives explicitly (e.g. "Do not use `Microsoft.Extensions.Caching.Distributed.IDistributedCache`").>

<!--
OPTIONAL. Include when the domain has vocabulary or a mental model an agent must
understand before choosing a package or option. Skip for thin utilities. One H3
per concept: precise definition, why it matters, link to the package section
where the implementation lives.
-->

## Core Concepts

### <Concept Name>

<What it is. Why the framework models it this way. What an agent must know to
reason about it.>

<!--
REQUIRED when the domain ships 2+ providers an agent must choose between. Skip
for a single provider or a trivial choice.
-->

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.<Provider1>` | <Concrete condition> | <Anti-condition> | <The cost you accept> |
| `Headless.<Provider2>` | <Concrete condition> | <Anti-condition> | <The cost you accept> |

---

## Headless.<Package>

<One sentence: what this package is and who uses it.>

### Problem Solved

<What this package gives the consumer that they would otherwise have to build or stitch together.>

### Key Features

- <Capability 1 — concrete, not promotional>
- <Capability 2>

<!--
OPTIONAL. Include only when a conventional reading of the API would lead the
agent wrong: why the default is X not Y, why a token is checked at start vs.
mid-operation, why ordering is best-effort, why a dependency exists. Skip for
ordinary packages — do not write `None.` here.
-->

### Design Notes

- <Choice. Why. What the agent must do because of it.>

### Installation

```bash
dotnet add package Headless.<Package>
```

### Quick Start

```csharp
// Minimal setup that compiles in a real project. Registration call + one use.
```

### Configuration

<Options block or table. Write `None.` if the package has no configuration.>

```csharp
options.<OptionName> = <default>;  // <what it controls>
```

### Dependencies

- `Headless.<Other>`
- `<Third-party package, if any>`

### Side Effects

- <DI registrations (interfaces registered, lifetime)>
- <Background/hosted services>
- <Filesystem, network, or process effects>
- <Write `None.` if the package is pure (abstractions-only).>

---

<!--
Repeat the `## Headless.<Package>` block for each package. Order: Abstractions,
then Core, then providers alphabetically. Separate package sections with `---`.
Add a compact Table of Contents at the top only if the doc grows long enough
that an agent needs it to jump between packages.
-->
