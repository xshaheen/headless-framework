<!--
Starting point for src/Headless.<Package>/README.md. This README ships with the
NuGet package and is shown on nuget.org. Its body mirrors the matching
`## Headless.<Package>` section in docs/llms/<domain>.md — same sub-section order,
same facts. The only differences: no frontmatter, no map, and sub-sections are
H2 here (the domain-doc H3s promoted one level).

Read docs/authoring/AUTHORING.md before editing any package README.
-->

# Headless.<Package>

<One sentence: what this package is and who uses it. Matches the lead sentence of
the `## Headless.<Package>` section in docs/llms/<domain>.md.>

## Problem Solved

<What this package gives the consumer that they would otherwise have to build or stitch together.>

## Key Features

- <Capability 1 — concrete, not promotional>
- <Capability 2>

<!--
OPTIONAL. Include only when a conventional reading of the API would lead the
consumer wrong. Skip for ordinary packages — do not write `None.` here. Mirror
the matching `### Design Notes` in docs/llms/<domain>.md.
-->

## Design Notes

- <Choice. Why. What the consumer must do because of it.>

## Installation

```bash
dotnet add package Headless.<Package>
```

## Quick Start

```csharp
// Minimal setup that compiles in a real project. Registration call + one use.
```

## Configuration

<Options block or table. Write `None.` if the package has no configuration.>

```csharp
options.<OptionName> = <default>;  // <what it controls>
```

## Dependencies

- `Headless.<Other>`
- `<Third-party package, if any>`

## Side Effects

- <DI registrations (interfaces registered, lifetime)>
- <Background/hosted services>
- <Filesystem, network, or process effects>
- <Write `None.` if the package is pure (abstractions-only).>
