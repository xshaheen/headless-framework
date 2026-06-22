# Headless.Tus

Base dependency that wires `tusdotnet` into the ASP.NET Core pipeline. Contains no store; exists to share the `tusdotnet` dependency and Headless hosting infrastructure across TUS packages.

## Problem Solved

Provides a consistent `tusdotnet` integration point for all TUS store packages so each provider does not independently manage endpoint wiring and version alignment.

## Key Features

- Shared `tusdotnet` dependency (all TUS packages reference this one)
- `Headless.Hosting` wiring for ASP.NET Core middleware

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
