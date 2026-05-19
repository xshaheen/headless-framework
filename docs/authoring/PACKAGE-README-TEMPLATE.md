<!--
Canonical template for src/Headless.<Package>/README.md.

This README ships with the NuGet package and is shown on nuget.org. Its body
MUST mirror the matching `## Headless.<Package>` per-package section in
docs/llms/<domain>.md — same sub-section order, same content. Agents and human
readers should see the same facts regardless of which file they open.

Read docs/authoring/AUTHORING.md for the full invariants and lifecycle
workflows before editing any package README.
-->

# Headless.<Package>

<One sentence: what this package is and who uses it. Matches the lead sentence
of the `## Headless.<Package>` section in docs/llms/<domain>.md.>

## Problem Solved

<What this package gives the consumer that they would otherwise have to build or stitch together.>

## Key Features

- <Capability 1 — concrete, not promotional>
- <Capability 2>
- <Capability 3>

<!--
OPTIONAL SECTION — fixed position. Include when the package makes a non-obvious
choice the agent must understand to use it correctly. Skip entirely for
conventional packages — do not write `None.` here. Mirror this content in the
matching `### Design Notes` of docs/llms/<domain>.md.
-->

## Design Notes

- <Decision, then the reason. Each bullet: "<Choice>. <Why>. <What the consumer must do because of this>.">

## Installation

```bash
dotnet add package Headless.<Package>
```

## Quick Start

```csharp
// Minimal working setup. Compilable in a real project (no pseudo-code).
// Show the registration call and one representative use.
```

## Configuration

<Options code block or Markdown table. Write `None.` if the package has no configuration.>

```csharp
options.<OptionName> = <default>;  // <what it controls>
```

## Dependencies

- `Headless.<Other>`
- `<Third-party package, if any>`

## Side Effects

- <DI registrations (which interfaces are registered, lifetime)>
- <Background services, hosted services>
- <File system, network, or process-level effects>
- <Write `None.` if the package is pure (abstractions-only).>
