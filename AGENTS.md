# Repository Guidelines

## Project Structure & Module Organization
- `src/` houses the reusable framework packages (typically `Framework.*` projects).
- `tests/` contains unit and integration test projects (naming uses `*.Tests.Unit` and `*.Tests.Integration`).
- `demo/` includes sample applications (`*.Demo`) for wiring and usage examples.
- `build/` contains the NUKE build orchestration used by the root `build.sh`, `build.ps1`, and `build.cmd`.
- `docs/` stores published documentation; `plans/` captures internal refactor notes.
- `artifacts/` is the default output location for build/test results.

## Build, Test, and Development Commands
- `./build.sh Compile` (or `build.cmd`, `build.ps1` on Windows): build the solution via NUKE.
- `./build.sh Test`: run all test projects and write results to `artifacts/test-results`.
- `./build.sh Pack`: create NuGet packages in `artifacts/packages-results`.
- `dotnet build headless-framework.slnx`: quick local build of the solution.
- `dotnet test tests/Framework.Messages.Core.Tests.Unit`: run a single test project.

## Coding Style & Naming Conventions
- Follow `.editorconfig`: C# uses 4 spaces, web assets use 2 spaces, max line length is 120.
- Use `Framework.*` for library projects; tests follow `Framework.<Area>.Tests.<Unit|Integration>`.
- Keep public APIs in `src/` self-contained and package-ready (each folder is a NuGet package).

## Testing Guidelines
- Test framework: xUnit v3 with `Microsoft.NET.Test.Sdk`; coverage collected via `coverlet.collector`.
- Place snapshots under `tests/**/Snapshots` and keep test data in `tests/**/Files`.
- Prefer running `./build.sh Test` before submitting changes that touch shared packages.

## Commit & Pull Request Guidelines
- Commit messages follow Conventional Commits (examples: `feat: add ...`, `fix: resolve ...`, `docs: update ...`, `chore: ...`).
- PRs should include a concise summary, affected packages/projects, and test evidence (command + result).
- Attach screenshots or gifs when modifying dashboard UI (`src/Framework.*.Dashboard/wwwroot`).

## Configuration Notes
- .NET SDK version is pinned by `global.json`; use the build scripts to align tooling.
- Package outputs and test results are stored in `artifacts/`—clean with `./build.sh Clean`.
