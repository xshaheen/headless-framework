# Headless.Tus

Base dependency shared by all Headless TUS store packages. Contains no code and no store; it exists only to pin and share the `tusdotnet` and `Headless.Hosting` package references so every TUS provider aligns on one version.

## Problem Solved

Provides a consistent `tusdotnet` integration point for all TUS store packages so each provider does not independently manage endpoint wiring and version alignment.

## Key Features

- Shared `tusdotnet` dependency (all TUS packages reference this one)
- Shared `Headless.Hosting` reference so TUS providers align on one hosting baseline

## Installation

```bash
dotnet add package Headless.Tus
```

## Quick Start

`Headless.Tus` is a base package. Add `Headless.Tus.Azure` for a complete upload setup. This package does not need to be installed directly; it is pulled in transitively.

## Configuration

None.

## Dependencies

- `tusdotnet`
- `Headless.Hosting`

## Side Effects

None.
