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
        [
            .. AppDomain
                .CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                .Select(assembly => (MetadataReference)MetadataReference.CreateFromFile(assembly.Location))
                .Concat([
                    MetadataReference.CreateFromFile(typeof(JobFunctionAttribute).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(JobFunctionProvider).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location),
                ])
                .DistinctBy(reference => reference.Display, StringComparer.Ordinal),
        ]
    );

    public static GeneratorDriver Run(string source) => Run(source, out _);

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

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        IReadOnlyCollection<(string Path, string Source)> sources,
        IReadOnlyCollection<MetadataReference> additionalReferences
    )
    {
        return CSharpCompilation.Create(
            assemblyName,
            sources.Select(source =>
                CSharpSyntaxTree.ParseText(
                    source.Source,
                    CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
                    source.Path
                )
            ),
            [.. _References.Value, .. additionalReferences],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
    }
}
