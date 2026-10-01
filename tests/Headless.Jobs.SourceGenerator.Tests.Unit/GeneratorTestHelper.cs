// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Base;
using Headless.Jobs.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

internal static class GeneratorTestHelper
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> _References = new(() =>
        GeneratorCompilation.LoadedAssemblyReferences(
            typeof(JobAttribute).Assembly,
            typeof(JobsCatalogBuilder).Assembly,
            typeof(IServiceCollection).Assembly
        )
    );

    public static GeneratorDriver Run(string source)
    {
        return Run(source, out _);
    }

    public static GeneratorDriver Run(
        string source,
        out ImmutableArray<Diagnostic> compilationDiagnostics,
        params MetadataReference[] additionalReferences
    ) => Run([("Jobs.SourceGenerator.Tests.cs", source)], out compilationDiagnostics, additionalReferences);

    public static GeneratorDriver Run(
        IReadOnlyCollection<(string Path, string Source)> sources,
        out ImmutableArray<Diagnostic> compilationDiagnostics,
        params MetadataReference[] additionalReferences
    )
    {
        var compilation = CreateCompilation("Jobs.SourceGenerator.Tests", sources, additionalReferences);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new JobsIncrementalSourceGenerator());
        var result = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics
        );
        compilationDiagnostics = outputCompilation.GetDiagnostics().AddRange(generatorDiagnostics);
        return result;
    }

    public static MetadataReference EmitReference(
        string assemblyName,
        string source,
        out ImmutableArray<Diagnostic> compilationDiagnostics
    )
    {
        var compilation = CreateCompilation(assemblyName, [($"{assemblyName}.cs", source)], []);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new JobsIncrementalSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        compilationDiagnostics = outputCompilation.GetDiagnostics().AddRange(generatorDiagnostics);

        using var stream = new MemoryStream();
        var emitResult = outputCompilation.Emit(stream);
        emitResult.Success.Should().BeTrue(string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    /// <summary>A library compilation that references the Jobs runtime plus <paramref name="additionalReferences"/>.</summary>
    public static CSharpCompilation CreateCompilation(
        string assemblyName,
        IReadOnlyCollection<(string Path, string Source)> sources,
        IReadOnlyCollection<MetadataReference> additionalReferences
    ) => GeneratorCompilation.Create(assemblyName, sources, [.. _References.Value, .. additionalReferences]);
}
