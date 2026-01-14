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

## Code Coverage Analysis

Run comprehensive code coverage analysis with Coverlet and generate HTML reports:

```bash
# Run coverage for a specific test project
./build.sh CoverageAnalysis --test-project Framework.Checks.Tests.Unit

# With custom thresholds (default: 80% for both)
./build.sh CoverageAnalysis \
  --test-project Framework.Checks.Tests.Unit \
  --coverage-line-threshold 85 \
  --coverage-branch-threshold 80

# Agent-friendly JSON output
./build.sh CoverageAnalysis \
  --test-project Framework.Checks.Tests.Unit \
  --coverage-json-output
```

**JSON Output Format** (with `--coverage-json-output`):

```json
{
  "success": true,
  "timestamp": "2026-01-14T21:31:14.655442Z",
  "testProject": "Framework.Checks.Tests.Unit",
  "coverage": {
    "line": {
      "percentage": 88.1,
      "covered": 779,
      "coverable": 884
    },
    "branch": {
      "percentage": 81.1,
      "covered": 383,
      "total": 472
    }
  },
  "thresholds": {
    "line": 80,
    "branch": 80
  },
  "meetsThresholds": true,
  "reports": {
    "html": "/absolute/path/coverage/html/index.html",
    "summary": "/absolute/path/coverage/html/Summary.txt",
    "cobertura": "/absolute/path/TestResults/coverage.cobertura.xml"
  }
}
```

**Output:**
- **Coverage results**: `tests/{ProjectName}/TestResults/**/coverage.cobertura.xml`
- **HTML report**: `coverage/html/index.html`
- **Text summary**: `coverage/html/Summary.txt`

**Configure thresholds in test project** (recommended):

```xml
<PropertyGroup>
  <CollectCoverage>true</CollectCoverage>
  <CoverletOutputFormat>cobertura</CoverletOutputFormat>
  <Threshold>80</Threshold>
  <ThresholdType>line,branch</ThresholdType>
  <ThresholdStat>total</ThresholdStat>
  <ExcludeByAttribute>CompilerGenerated,GeneratedCode,ExcludeFromCodeCoverage</ExcludeByAttribute>
</PropertyGroup>
```

**Mutation Testing** (verify test quality):

```bash
# Install Stryker.NET (one-time)
dotnet tool install --global dotnet-stryker

# Run from test project directory
cd tests/Framework.Checks.Tests.Unit
dotnet stryker --reporter html --reporter progress --threshold-high 85 --threshold-low 70

# View report
open StrykerOutput/*/reports/mutation-report.html
```

**Coverage targets:**
- **Line coverage**: ≥85% (minimum: 80%)
- **Branch coverage**: ≥80% (minimum: 70%)
- **Mutation score**: ≥70% (goal: 85%+)

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
