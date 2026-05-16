# Contributing

Thanks for contributing to `headless-framework`.

This repository is a modular .NET 10 framework, not a single application. Changes often affect multiple packages, package READMEs, XML docs, and test projects. Keep PRs focused and explicit.

## Before You Start

- Small fixes can go straight to a pull request.
- For larger changes, open an issue first so package boundaries and API shape can be agreed before implementation.
- Search existing issues and package READMEs before proposing new abstractions or providers.

## Local Setup

```bash
dotnet tool restore
./build.sh Compile
./build.sh Test
```

On Windows, use `./build.ps1` instead of `./build.sh`.

Integration tests use Testcontainers and require Docker. Unit tests should stay isolated and not depend on external services.

## Repository Layout

- `src/` - NuGet packages
- `tests/` - unit, integration, and harness projects
- `build/` - NUKE build automation
- `docs/` - generated and hand-written documentation

Most domains follow an abstraction + provider split:

- `Headless.*.Abstractions` exposes contracts
- `Headless.*.<Provider>` contains concrete implementations

## Coding Rules

- Use file-scoped namespaces.
- Prefer primary constructors for DI.
- Use `required` and `init` where appropriate.
- Default to `sealed` unless inheritance is intentional.
- Prefer collection expressions and pattern matching over older syntax.
- Keep package versions in `Directory.Packages.props`. Do not add `Version=` to `.csproj` files.
- Match existing naming, option patterns, and dependency registration style.

## Tests And Docs

- Add or update tests with every behavior change.
- Update XML docs for public API changes.
- Update the package `README.md` under `src/Headless.*/` when package behavior, options, or setup changes.
- Keep `README.md`, `docs/llms/index.md`, and domain docs under `docs/llms/` in sync when you change public guidance.

## Pull Requests

- Link the related issue when one exists.
- Describe affected packages and whether the change is breaking.
- Include the validation you ran (`Compile`, `Test`, package-specific checks).
- Keep PRs reviewable. Separate refactors from behavior changes when possible.
