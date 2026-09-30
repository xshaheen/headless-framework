# Headless.Generator.ProviderSetup

Roslyn source generator that emits a provider package's registration surface from one attributed options class.

## Why use this package

Provider packages repeat the same registration code: the `IConfiguration` / `Action<T>` / `Action<T, IServiceProvider>` overloads for the default and named slots, options validation, the named HttpClient, and keyed sender registration. Mark the options class with `[GenerateProviderSetup]` (or `[GenerateClientSetup]` for a single-backend client) plus `[OutboundEffect]`, and the generator writes that surface, with a resilience pipeline derived from the declared effect. It uses no reflection, so the output is trimming- and AOT-friendly.

## Install

```bash
dotnet add package Headless.Generator.ProviderSetup
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Utilities guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/utilities.md#headlessgeneratorprovidersetup)
