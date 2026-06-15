# Contributing

Thanks for contributing to `headless-framework`.

This repository is a modular .NET 10 framework, not a single application. Changes can affect multiple NuGet packages, package READMEs, XML docs, and provider-specific tests. Keep PRs focused and explicit.

## Before You Start

- Small fixes can go straight to a pull request.
- For larger changes, open an issue first so package boundaries and API shape can be agreed before implementation.
- Search existing issues, package READMEs, and `docs/llms/` before proposing new abstractions or providers.
- Treat public APIs as NuGet contracts. Prefer clean breaking changes only when the issue or PR explains the trade-off.

## Local Setup

Use the Makefile as the local entry point:

```bash
make bootstrap
make build
make test-unit
```

Useful scoped commands:

```bash
make format-check
make test-project TEST_PROJECT=tests/Headless.Api.Tests.Unit/Headless.Api.Tests.Unit.csproj
make test-class CLASS='*ClockTests'
make test-integration
```

Integration tests use Testcontainers and require Docker. Unit tests should stay isolated and must not depend on external services.

## Repository Layout

- `src/` - NuGet packages
- `tests/` - unit, integration, and harness projects
- `docs/` - generated and hand-written documentation
- `docs/llms/` - domain guidance for agents and consumers
- `Directory.Packages.props` - central package versions
- `headless-framework.slnx` - solution entry point

Most domains follow an abstraction + provider split:

- `Headless.*.Abstractions` exposes contracts
- `Headless.*.<Provider>` contains concrete implementations

When multiple providers share a behavior contract, prefer a shared `*.Tests.Harness` project for conformance tests instead of duplicating provider fixtures.

## Coding Rules

- Use file-scoped namespaces.
- Prefer primary constructors for DI when they fit the type.
- Use `required` and `init` where appropriate.
- Default to `sealed` unless inheritance is intentional.
- Prefer collection expressions and pattern matching over older syntax.
- Keep package versions in `Directory.Packages.props`. Do not add `Version=` to `.csproj` files.
- Match existing naming, option validation, setup extension, and dependency registration patterns.

## Tests And Docs

- Add or update tests with every behavior change.
- Run the narrowest relevant test first, then widen to `make test-unit` or `make test` when the change warrants it.
- Run `make format-check` before submitting C# changes.
- Update XML docs for public API changes.
- Update package `README.md` files under `src/Headless.*/` when package behavior, options, or setup changes.
- Keep `README.md`, `docs/llms/index.md`, and domain docs under `docs/llms/` in sync when you change public guidance.

## Pull Requests

- Link the related issue when one exists.
- Describe affected packages and whether the change is breaking.
- Include the validation you ran, using the actual `make` commands.
- Keep PRs reviewable. Separate refactors from behavior changes when possible.
