// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Tests;

internal static class CompilationFixture
{
    private static readonly ImmutableArray<MetadataReference> _PlatformReferences = (
        (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
        ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.")
    )
        .Split(Path.PathSeparator)
        .Select(static path => MetadataReference.CreateFromFile(path))
        .ToImmutableArray<MetadataReference>();

    private static readonly Lazy<MetadataReference> _ProducerReference = new(static () =>
        EmitReference(CreateCompilation("Producer", FixtureSources.Producer))
    );

    public static MetadataReference ProducerReference => _ProducerReference.Value;

    public static CSharpCompilation CreateCompilation(
        string assemblyName,
        string source,
        params MetadataReference[] references
    )
    {
        return CSharpCompilation.Create(
            assemblyName,
            [
                CSharpSyntaxTree.ParseText(
                    source,
                    CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14)
                ),
            ],
            [.. _PlatformReferences, .. references],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
    }

    public static MetadataReference EmitReference(CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);

        result.Success.Should().BeTrue(string.Join(Environment.NewLine, result.Diagnostics));

        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    public static GeneratorRun Run(CSharpCompilation compilation, GeneratorDriver? driver = null)
    {
        driver ??= CSharpGeneratorDriver.Create(
            [new MiddlewareDiscoverySpikeGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true)
        );

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        var result = driver.GetRunResult().Results.Single();

        return new GeneratorRun(
            driver,
            outputCompilation,
            result.Diagnostics,
            result.GeneratedSources.Single().SourceText.ToString(),
            result.TrackedSteps
        );
    }
}

internal sealed record GeneratorRun(
    GeneratorDriver Driver,
    Compilation OutputCompilation,
    ImmutableArray<Diagnostic> GeneratorDiagnostics,
    string GeneratedSource,
    ImmutableDictionary<string, ImmutableArray<IncrementalGeneratorRunStep>> TrackedSteps
);
