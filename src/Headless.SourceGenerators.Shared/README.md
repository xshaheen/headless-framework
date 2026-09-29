# Headless.SourceGenerators.Shared

Internal infrastructure shared by the Headless incremental source generators (`Headless.Jobs.SourceGenerator` and `Headless.Generator.Primitives`). It is not a package and has no project file: each generator imports `Headless.SourceGenerators.Shared.props`, which

- sets the Roslyn-component and packaging properties every generator package needs, references the Roslyn packages, and packs the generator under `analyzers/dotnet/cs` with the `lib/netstandard2.0/_._` marker;
- compiles these files into the generator as `internal` types in the `Headless.SourceGenerators` namespace, plus the netstandard2.0 language polyfills in `Polyfills.cs`.

A generator's `.csproj` keeps only its target framework, package metadata, and anything specific to it, such as extra DLLs to pack or `CompilerVisibleProperty` items.

## Why source inclusion

The compiler does not resolve an analyzer's NuGet dependencies. A shared assembly would have to ship inside every generator package, and two generators carrying different builds of it could conflict when loaded into the same compilation. Compiling the source into each generator avoids both problems.

## What it provides

| Type | Purpose |
| --- | --- |
| `EquatableArray<T>` | Value-equal array for pipeline models; `ImmutableArray<T>` compares by reference and defeats caching. |
| `LocationInfo` | Value-equal location that does not hold a `SyntaxTree`. |
| `DiagnosticInfo` | Value-equal diagnostic computed in a cached step and materialized only when reported. |
| `DiagnosticReporting.RegisterDiagnosticsOutput` | Reports `DiagnosticInfo` values against the live compilation's trees so `#pragma` suppression works. |
| `SourceWriter` | Indented writer that emits final-layout source without a parse-and-normalize pass. |

## Pipeline rules for a generator built on this

- Discover declarations with `ForAttributeWithMetadataName`, never `CreateSyntaxProvider` over all attributed syntax.
- Transforms return records of strings, primitives, `EquatableArray<T>`, `LocationInfo`, and `DiagnosticInfo` only. No `ISymbol`, `SyntaxNode`, `SemanticModel`, `Compilation`, or `Location` may cross a step boundary.
- Keep locations and diagnostics out of the model that feeds source output, so an edit that only moves code does not re-emit source.
- Name every step with `WithTrackingName` and cover it with a test that asserts the step is cached or unchanged after an unrelated edit.
- Give each generator its own diagnostic ID range, resx-backed messages, and a per-rule help link. Declare descriptors with a literal ID so the release-tracking analyzer can check `AnalyzerReleases.*.md`.
