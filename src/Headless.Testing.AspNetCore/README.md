# Headless.Testing.AspNetCore

ASP.NET Core integration-test server with controllable time, DI scope helpers, and database reset.

## Why use this package

Wraps `WebApplicationFactory<TProgram>` with the infrastructure most integration tests need: an
auto-registered `FakeTimeProvider`, readiness/initializer waiting, idempotent async disposal, DI
scope execution helpers, and Respawner-based database reset — so test fixtures don't re-implement
this plumbing per project.

## Install

```bash
dotnet add package Headless.Testing.AspNetCore
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Testing guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/testing.md#headlesstestingaspnetcore)
