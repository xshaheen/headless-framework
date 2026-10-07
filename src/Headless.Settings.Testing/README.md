# Headless.Settings.Testing

Test-only doubles for Headless settings.

## Why use this package

Code that reads a typed value through `ISettingsSnapshot<T>` needs a snapshot in its tests, and the real one loads from a settings store in the background. This package supplies `TestSettingsSnapshot<T>`, an in-memory snapshot whose value the test sets directly, so each test project does not keep its own copy of a fake.

## Install

```bash
dotnet add package Headless.Settings.Testing
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Settings guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/settings.md#headlesssettingstesting)
