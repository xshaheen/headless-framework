# CLAUDE.md

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

## Conventions

**Argument Validation:**

- Use `Headless.Checks` (`Argument.*`, `Ensure.*`) for argument validation; avoid `ArgumentNullException.ThrowIfNull`, `ArgumentOutOfRangeException.ThrowIfGreaterThan`, etc.

**Options Pattern**:

- Validate options with FluentValidation + hosting extensions: `AddOptions<TOptions, TValidator>()` / `Configure<TOptions, TValidator>(...)`. Avoid custom `IValidateOptions<T>` when hosting covers it.
- Create an `internal sealed class {OptionsName}Validator : AbstractValidator<{OptionsName}>` in the same file as the options class, directly below it.
- Register validators via DI using `services.Configure<TOption, TValidator>(action)` or `services.AddOptions<TOption, TValidator>()` from `Headless.Hosting`; these wire up FluentValidation + `ValidateOnStart()` automatically.
- DI option registration must expose 3 overloads: `IConfiguration`, `Action<TOptions>`, `Action<TOptions, IServiceProvider>`. Share wiring in one private/core helper.
- Higher-level bootstrap APIs may auto-bind required options from owned default sections (for example `Headless:*`) when part of the package contract.
- If options are required, do not offer a parameterless registration overload; require options and delegate to the optioned path.
- Never call `new Validator().ValidateAndThrow()` manually; use the DI pipeline.

** Input Validation Responsibility:**

This framework delegates certain input validation to consuming applications:

- **Cache key length limits**: Not enforced by `ICache` implementations. Consumers should validate key lengths at their application boundaries if DoS protection is needed.
- **Message payload sizes**: `CacheInvalidationMessage` and similar DTOs don't enforce size limits. Consumers should configure their messaging infrastructure (RabbitMQ, Redis, etc.) with appropriate limits.

## Package Management

- All versions in `Directory.Packages.props`. **Never** add `Version` attribute in `.csproj` files.

## New .NET Projects

When adding a new `.csproj` to the solution, set the project SDK to one of the Headless MSBuild SDKs declared in [global.json](global.json). Do not use the stock `Microsoft.NET.Sdk` family for new projects. Versions are pinned in `global.json`'s `msbuild-sdks` block, so omit the version from the project declaration, for example `<Project Sdk="Headless.NET.Sdk.Web">`.

| Project type | SDK |
| --- | --- |
| Library, console app | `Headless.NET.Sdk` |
| ASP.NET Core / Web API | `Headless.NET.Sdk.Web` |
| Test project (xUnit v3, MTP) | `Headless.NET.Sdk.Test` |
| Razor class library | `Headless.NET.Sdk.Razor` |
| Blazor WebAssembly | `Headless.NET.Sdk.BlazorWebAssembly` |
| WPF / Windows Forms | `Headless.NET.Sdk.WindowsDesktop` |

After creating the project, attach it to [headless-framework.slnx](headless-framework.slnx). The Headless SDKs apply the project's strict baseline, including nullable references, current analyzers, banned `Newtonsoft.Json`, deterministic builds, and CI-aware warning handling. Do not disable defaults without a documented reason. Configuration switches and `Disable*` properties are documented at https://github.com/xshaheen/headless-sdk.

## Documentation

- Keep public API XML docs in sync with the code.
- Keep each package `README.md` in sync with the code; package READMEs live under `src/Headless.*`.
- `docs/solutions/` is a searchable knowledge store of past fixes and patterns, organized by category (`api`, `concurrency`, `guides`, `messaging`, etc.) with YAML frontmatter (`module`, `tags`, `problem_type`). Search it before implementing features, debugging issues, or making decisions in a documented area.
