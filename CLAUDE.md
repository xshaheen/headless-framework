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

**Stack**: xUnit v3 (Microsoft Testing Platform), AwesomeAssertions (fork of FluentAssertions), NSubstitute, Bogus

## Build & Test

- Solution file: [headless-framework.slnx](headless-framework.slnx) (modern XML format). Passes directly to `dotnet`; older tooling may need `.sln`.
- Local CLI tools pinned in [dotnet-tools.json](dotnet-tools.json) — `dotnet tool restore`, then `dotnet <tool>`.
- Headless SDKs treat warnings as errors in CI.

## Conventions

**Argument Validation:**

- Use `Headless.Checks` (`Argument.*`, `Ensure.*`) for argument validation; avoid `ArgumentNullException.ThrowIfNull`, `ArgumentOutOfRangeException.ThrowIfGreaterThan`, etc.

**Options Pattern**:

- Validate options only when needed; when you do, use FluentValidation via the `Headless.Hosting` extensions: `AddOptions<TOptions, TValidator>()` / `Configure<TOptions, TValidator>(...)`. Avoid custom `IValidateOptions<T>` when hosting covers it.
- Create an `internal sealed class {OptionsName}Validator : AbstractValidator<{OptionsName}>` in the same file as the options class, directly below it if the option has any property that need validation.
- Register validators via DI using `services.Configure<TOption, TValidator>(action)` or `services.AddOptions<TOption, TValidator>()` from `Headless.Hosting`; these wire up FluentValidation + `ValidateOnStart()` automatically.
- Higher-level bootstrap APIs may auto-bind required options from owned default sections (for example `Headless:*`) when part of the package contract.
- If options are required, do not offer a parameterless registration overload; require options and delegate to the optioned path.
- Never call `new Validator().ValidateAndThrow()` manually; use the DI pipeline.

**DI Registration (Setup classes):**

Every provider package exposes a single static `Setup{Provider}` class in `Setup.cs` at the package root, following this shape:

```csharp
[PublicAPI]
public static class SetupRedisCache
{
    extension(IServiceCollection services) // C# 14 extension members
    {
        public IServiceCollection AddRedisCache(IConfiguration configuration, ...) { ... }
        public IServiceCollection AddRedisCache(Action<TOptions> setupAction, ...) { ... }
        public IServiceCollection AddRedisCache(Action<TOptions, IServiceProvider> setupAction, ...) { ... }

        private IServiceCollection _AddCacheCore(...) { /* shared wiring */ }
    }
}
```

- Name the shared private helper `_Add{Feature}Core`.

**Public API Discipline:**

- Each package's `public` surface IS its NuGet contract — keep types `internal sealed` and promote to `public` only when consumers must reference them.
- Use `JetBrains.Annotations` is globally imported via [Directory.Build.props](Directory.Build.props)
    - Annotate the intentional public surface with `[PublicAPI]`.
    - Use `[Pure]`, `[MustDisposeResource]` and `[MustUseReturnValue]` where applicable.

**Source File Header:**

Every source file starts with: `// Copyright (c) Mahmoud Shaheen. All rights reserved.`

**Input Validation Responsibility:**

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

After creating the project, attach it to [headless-framework.slnx](headless-framework.slnx). The Headless SDKs apply the project's strict baseline, including nullable references, current analyzers, banned `Newtonsoft.Json`, deterministic builds, and CI-aware warning handling. Do not disable defaults without a documented reason. Configuration switches and `Disable*` properties are documented at https://raw.githubusercontent.com/xshaheen/headless-sdk/refs/heads/main/README.md.

## Documentation

- ALWAYS keep docs/llms synchronized with the code. If behavior changes in a way an AI coding agent should know, update the relevant LLM docs
- Keep each package `README.md` in sync with the code; package READMEs live under `src/Headless.*`.
- `docs/solutions/` is a searchable knowledge store of past fixes and patterns, organized by category (`api`, `concurrency`, `guides`, `messaging`, etc.) with YAML frontmatter (`module`, `tags`, `problem_type`). Search it before implementing features, debugging issues, or making decisions in a documented area.