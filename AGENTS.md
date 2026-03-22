# AGENTS.md

## Project Overview

**headless-framework** is a modular .NET 10 framework for building APIs and backend services. Composed of ~94 NuGet packages organized by functional domains (API, Blobs, Caching, Messaging, ORM, etc.). Unopinionated, zero lock-in design.

**This is a framework, not a finished application.**
It is designed to support multiple projects and packages, both internal and external. As such, it may contain abstractions, extension points, and utility classes or methods that are not directly used within this repository. These elements exist deliberately to enable extensibility, customization, and reuse by downstream consumers and future integrations.

**This is a greenfield project.**
Prefer simpler, cleaner APIs even when that requires breaking changes. Do not preserve awkward compatibility layers unless explicitly requested.

**Coverage targets:**
- **Line coverage**: ≥85% (minimum: 80%)
- **Branch coverage**: ≥80% (minimum: 70%)
- **Mutation score**: ≥70% (goal: 85%+)

## Architecture Pattern

Each feature follows **abstraction + provider pattern**:
- `Headless.*.Abstractions` — interfaces and contracts
- `Headless.*.<Provider>` — concrete implementation

Example: `Headless.Caching.Abstractions` + `Headless.Caching.Redis`

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
- `sealed` by default if not designed for inheritance
- Collection expressions: `[]`
- Pattern matching over old-style checks
- Use `Headless.Checks` guards (`Argument.*`, `Ensure.*`) for validation. Do not use `ArgumentNullException.ThrowIfNull`, `ArgumentOutOfRangeException.ThrowIfGreaterThan` and similars.
- Validate options only when needed, and when you do, use FluentValidation through the existing hosting/options extensions.
- For options with FluentValidation, use the existing hosting extensions: `AddOptions<TOptions, TValidator>()` and `Configure<TOptions, TValidator>(...)`. Do not add custom `IValidateOptions<T>` implementations when the hosting pattern already covers the case.
- When adding a DI registration method that configures options, provide all three overloads by default: `IConfiguration`, `Action<TOptions>`, and `Action<TOptions, IServiceProvider>`. Keep the shared registration in a private/core helper instead of duplicating service registration across overloads.
- Higher-level bootstrap APIs may also auto-bind required options from their owned default configuration sections, like `Headless:*`, when that convention is part of the package contract.
- If a feature requires options, do not expose a misleading parameterless registration overload. Higher-level APIs should accept the options they need and delegate to the optioned registration path.

## Package Management

- All versions in `Directory.Packages.props`. **Never** add `Version` attribute in `.csproj` files.

## Documentation

- Make sure to sync XML docs of the public APIs.
- Make sure to sync project README.md files for each package (exist in `src/Headless.*` folders).

## Tools

```bash
dotnet tool restore  # Install: csharpier, dotnet-ef, minver-cli, husky
```

## Build Validation

- To validate build warnings, use `dotnet build --no-incremental`.
- Use plain `dotnet build` only for quick feedback, not final warning verification.

## Design Decisions

- All NuGet versions are in `Directory.Packages.props` — never add `Version` in `.csproj`
- Public APIs must have XML docs, and README.md files for each package must be kept up to date.

### Options Validation

Always validate options classes with **FluentValidation** (`AbstractValidator<T>`). Never use `IValidateOptions<T>`.

- Create an `internal sealed class {OptionsName}Validator : AbstractValidator<{OptionsName}>` in the same file as the options class, directly below it.
- Register via DI using `services.Configure<TOption, TValidator>(action)` or `services.AddOptions<TOption, TValidator>()` from `Headless.Hosting` — these wire up FluentValidation + `ValidateOnStart()` automatically.
- Never call `new Validator().ValidateAndThrow()` manually — use the DI pipeline.

### Input Validation Responsibility

This framework delegates certain input validation to consuming applications:

- **Cache key length limits**: Not enforced by `ICache` implementations. Consumers should validate key lengths at their application boundaries if DoS protection is needed.
- **Message payload sizes**: `CacheInvalidationMessage` and similar DTOs don't enforce size limits. Consumers should configure their messaging infrastructure (RabbitMQ, Redis, etc.) with appropriate limits.
