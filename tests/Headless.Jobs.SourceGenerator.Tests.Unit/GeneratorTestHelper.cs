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

    public static GeneratorDriver Run(string source, out ImmutableArray<Diagnostic> compilationDiagnostics)
    {
        var compilation = CSharpCompilation.Create(
            "Jobs.SourceGenerator.Tests",
            [CSharpSyntaxTree.ParseText(source)],
            _References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new JobsIncrementalSourceGenerator());
        var result = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics
        );
        compilationDiagnostics = outputCompilation.GetDiagnostics().AddRange(generatorDiagnostics);
        return result;
    }
}
