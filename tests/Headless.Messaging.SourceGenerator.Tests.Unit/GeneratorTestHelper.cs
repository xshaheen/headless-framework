// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

internal static class GeneratorTestHelper
{
    public const string AssemblyName = "Messaging.SourceGenerator.Tests";

    private static readonly Lazy<ImmutableArray<MetadataReference>> _References = new(() =>
        GeneratorCompilation.LoadedAssemblyReferences(
            typeof(BusConsumerAttribute).Assembly,
            typeof(Headless.Messaging.Registration.MessagingContributionBuilder).Assembly, // Messaging.Core, which samples call ConfigureMessaging from
            // The catalog's consumer methods take a failure policy factory, so generated modules compile against it.
            typeof(Headless.Reliability.FailurePolicy).Assembly,
            typeof(ActivatorUtilities).Assembly,
            typeof(GeneratorTestHelper).Assembly
        )
    );

    public static GeneratorDriver Run(string source) => Run(source, out _);

    public static GeneratorDriver Run(string source, out ImmutableArray<Diagnostic> compilationDiagnostics) =>
        Run(source, out compilationDiagnostics, out _);

    public static GeneratorDriver Run(
        string source,
        out ImmutableArray<Diagnostic> compilationDiagnostics,
        out Compilation outputCompilation
    )
    {
        var compilation = CreateCompilation(AssemblyName, [("Messaging.SourceGenerator.Tests.cs", source)]);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new MessagingIncrementalSourceGenerator().AsSourceGenerator()],
            parseOptions: GeneratorCompilation.ParseOptions
        );
        var result = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out outputCompilation,
            out var generatorDiagnostics
        );
        compilationDiagnostics = outputCompilation.GetDiagnostics().AddRange(generatorDiagnostics);
        return result;
    }

    /// <summary>Diagnostics the generator itself reported, which carry the HM prefix.</summary>
    public static IEnumerable<Diagnostic> GeneratorDiagnostics(GeneratorDriver driver) =>
        driver.GetRunResult().Diagnostics;

    /// <summary>A library compilation that references the Messaging runtime and this test assembly.</summary>
    public static CSharpCompilation CreateCompilation(
        string assemblyName,
        IReadOnlyCollection<(string Path, string Source)> sources
    ) => GeneratorCompilation.Create(assemblyName, sources, _References.Value);
}
