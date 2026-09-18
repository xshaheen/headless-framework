---
title: "Provider setup classes and the options pattern"
date: 2026-09-18
last_updated: 2026-09-18
category: conventions
module: headless-framework
problem_type: design_pattern
component: dependency_injection
severity: high
tags:
  - dependency-injection
  - setup-builder
  - options
  - fluentvalidation
related_components:
  - provider_packages
  - Headless.Hosting
applies_when:
  - "Adding a provider package and its registration surface"
  - "Adding or validating an options class"
  - "Deciding which registration overloads a new Use* or Add* member needs"
---

# Provider setup and options

## Setup classes

Every provider package exposes a single static `Setup{Provider}` class in `Setup.cs` at the package root.

Multi-provider features follow the [unified provider setup builder
pattern](../architecture-patterns/unified-provider-setup-builder-pattern.md): the feature's Core package owns
the root `AddHeadless{Feature}(Action<Headless{Feature}SetupBuilder>)` entry plus the provider gates, and each
provider package contributes `Use{Provider}` extension members on that builder.

```csharp
public static class SetupRedisCache
{
    extension(HeadlessCachingSetupBuilder setup) // C# 14 extension members
    {
        public HeadlessCachingSetupBuilder UseRedis(IConfiguration configuration) { ... }
        public HeadlessCachingSetupBuilder UseRedis(Action<TOptions> setupAction) { ... }
        public HeadlessCachingSetupBuilder UseRedis(Action<TOptions, IServiceProvider> setupAction) { ... }
    }

    private static IServiceCollection _AddCacheCore(...) { /* shared wiring */ }
}
```

Single-backend packages with no provider choice keep plain `Add{Feature}` extensions on `IServiceCollection`,
with the same overload trio. Name the shared private helper `_Add{Feature}Core`.

Setup classes live in the family root namespace, not the provider's own namespace. See
[Namespace policy § tier 2](namespace-policy.md#three-tier-placement).

### When the overload trio does not apply

The trio applies to **provider** `Use{Provider}` and `Add{Feature}` members that bind that backend's options.

**Cross-cutting consumer extensions** adapt an already-composed feature. `UseOutputCache` and `UseBclCache`
consume a named `ICache` rather than supply a provider, so they expose a single `Action<TOptions>` overload plus
the consumed feature's builder. They bind no provider option section, so the `IConfiguration` and
`Action<TOptions, IServiceProvider>` overloads do not apply.

## Options

Validate options only when they need it. When they do, use FluentValidation through the `Headless.Hosting`
extensions — `AddOptions<TOptions, TValidator>()` and `Configure<TOptions, TValidator>(...)` — rather than a
custom `IValidateOptions<T>`.

- Put an `internal sealed class {OptionsName}Validator : AbstractValidator<{OptionsName}>` in the same file as
  the options class, directly below it, when any property needs validation.
- Register the validator through DI with `services.Configure<TOption, TValidator>(action)` or
  `services.AddOptions<TOption, TValidator>()` from `Headless.Hosting`. Both wire up FluentValidation and
  `ValidateOnStart()`.
- Never call `new Validator().ValidateAndThrow()` by hand. Use the DI pipeline.
- Higher-level bootstrap APIs may auto-bind required options from the sections they own (for example
  `Headless:*`) when that binding is part of the package contract.
- When options are required, offer no parameterless registration overload. Require the options and delegate to
  the optioned path.
