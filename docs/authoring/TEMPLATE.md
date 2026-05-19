<!--
Canonical template for docs/llms/<domain>.md.

When writing a new domain doc, copy this file to docs/llms/<domain>.md and
replace every <placeholder>. Do NOT reorder or rename sections. Do NOT delete
required sub-sections — write `None.` if empty.

Read docs/authoring/AUTHORING.md for the full invariants and lifecycle
workflows before editing any file under docs/llms/.
-->

---
domain: <Domain Name>
packages: <Package.Suffix.One, Package.Suffix.Two>
---

# <Domain Name>

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
- [Choosing a Provider](#choosing-a-provider)
- [Headless.<Package>](#headlesspackage)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Design Notes](#design-notes)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)

> <One-line factual summary of the domain. No marketing adjectives.>

## Quick Orientation

<2-4 sentences. What problem this domain solves. The main entry points (interface names, key types). When to pick which provider package. Keep it skimmable — agents read this first.>

## Agent Instructions

- <One rule per bullet. State the do AND the don't where relevant.>
- <Reference abstraction interfaces by exact name (e.g., `ICache` from `Headless.Caching.Abstractions`).>
- <Call out footguns: things that look right but break (cancellation semantics, threading, ordering, defaults).>
- <Name banned alternatives explicitly (e.g., "Do not use `Microsoft.Extensions.Caching.Distributed.IDistributedCache`").>

<!--
OPTIONAL SECTION — fixed position. Include when the domain has vocabulary
or a mental model an agent must understand before picking a package or
option. Skip entirely for thin utilities (e.g., Slugs).
-->

## Core Concepts

<2-4 sentences orienting the reader to the domain's mental model. Then one H3 per concept. Each concept entry is 2-4 sentences: precise definition, why it matters, link to the per-package section if implementation lives there.>

### <Concept Name>

<What it is. Why the framework models it this way. What an agent needs to know to reason about it. Link to relevant per-package section.>

<!--
OPTIONAL SECTION — fixed position. Include when the domain ships 2+ provider
packages and the agent must choose between them. Skip when there is a single
provider or the choice is trivial.
-->

## Choosing a Provider

<One short sentence framing the decision.>

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.<Provider1>` | <Concrete condition> | <Anti-condition> | <The cost you accept for picking this> |
| `Headless.<Provider2>` | <Concrete condition> | <Anti-condition> | <The cost you accept for picking this> |

<Optional: a short decision tree or rule-of-thumb paragraph below the table if the table isn't enough.>

---

## Headless.<Package>

<One sentence: what this package is and who uses it.>

### Problem Solved

<What this package gives the consumer that they would otherwise have to build or stitch together.>

### Key Features

- <Capability 1 — concrete, not promotional>
- <Capability 2>
- <Capability 3>

<!--
OPTIONAL SUB-SECTION — fixed position. Include when the package makes a
non-obvious choice the agent must understand to use it correctly. Examples:
why the default value is X not Y; why a cancellation token is checked at
start vs. mid-operation; why ordering is best-effort; why this package
depends on Y when a simpler alternative exists. Skip entirely for
conventional packages — do not write `None.` here.
-->

### Design Notes

- <Decision, then the reason. Each bullet: "<Choice>. <Why>. <What the agent must do because of this>.">

### Installation

```bash
dotnet add package Headless.<Package>
```

### Quick Start

```csharp
// Minimal working setup. Compilable in a real project (no pseudo-code).
// Show the registration call and one representative use.
```

### Configuration

<Use an options code block or a Markdown table. Write `None.` if the package has no configuration.>

```csharp
options.<OptionName> = <default>;  // <what it controls>
```

### Dependencies

- `Headless.<Other>`
- `<Third-party package, if any>`

### Side Effects

- <DI registrations (which interfaces are registered, lifetime)>
- <Background services, hosted services>
- <File system, network, or process-level effects>
- <Write `None.` if the package is pure (abstractions-only).>

---

<!--
Repeat the `## Headless.<Package>` block above for each package in the domain.
Order packages by dependency direction: Abstractions first, then Core, then
providers alphabetically. Use `---` as the separator between package sections.
-->
