# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**headless-framework** is a modular .NET 10 framework for building APIs and backend services. Composed of ~94 NuGet packages organized by functional domains (API, Blobs, Caching, Messaging, ORM, etc.). Unopinionated, zero lock-in design.

## Build Commands

```bash
# NUKE build system (prefer these)
./build.sh Compile    # Build solution
./build.sh Test       # Run all tests
./build.sh Pack       # Create NuGet packages
./build.sh Clean      # Clean outputs

# Direct dotnet (faster for single projects)
dotnet build src/Framework.Orm.EntityFramework
dotnet test tests/Framework.Base.Tests.Unit
dotnet test --filter "FullyQualifiedName~method_name"  # Single test
dotnet csharpier .    # Format code
```

## Architecture Pattern

Each feature follows **abstraction + provider pattern**:
- `Framework.*.Abstraction` — interfaces and contracts
- `Framework.*.<Provider>` — concrete implementation

Example: `Framework.Caching.Abstraction` + `Framework.Caching.Foundatio.Redis`

## Test Structure

- `*.Tests.Unit` — isolated, mocked, no external deps
- `*.Tests.Integration` — real deps via Testcontainers (requires Docker)
- `*.Tests.Harness` — shared fixtures and builders

**Stack**: xUnit, AwesomeAssertions (fork of FluentAssertions), NSubstitute, Bogus

## Code Conventions (Strictly Enforced)

**Required C# features**:

- File-scoped namespaces: `namespace X;`
- Primary constructors for DI
- `required`/`init` for properties
- `sealed` by default
- Collection expressions: `[]`
- Pattern matching over old-style checks

## Package Management

- All versions in `Directory.Packages.props`. **Never** add `Version` attribute in `.csproj` files.

## Documentation

- Make sure to sync XML docs of the public APIs.
- Make sure to sync project README.md files for each package (exist in `src/Framework.*` folders).

## Tools

```bash
dotnet tool restore  # Install: csharpier, dotnet-ef, minver-cli, husky
```
