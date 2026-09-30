// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tests;

/// <summary>Builds the compilations and drivers that source generator tests run against.</summary>
public static class GeneratorCompilation
{
    /// <summary>Parse options for test sources: the language version the generators target.</summary>
    public static CSharpParseOptions ParseOptions { get; } =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14);

    /// <summary>
    /// References to every assembly loaded in the test process plus <paramref name="additional"/>, so test sources
    /// can use the framework and the attributes a generator looks for.
    /// </summary>
    public static ImmutableArray<MetadataReference> LoadedAssemblyReferences(params Assembly[] additional)
    {
        return
        [
            .. AppDomain
                .CurrentDomain.GetAssemblies()
                .Concat(additional)
                .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                .Select(assembly => assembly.Location)
                .Distinct(StringComparer.Ordinal)
                .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location)),
        ];
    }

    /// <summary>Creates a library compilation from named source files.</summary>
    public static CSharpCompilation Create(
        string assemblyName,
        IEnumerable<(string Path, string Source)> sources,
        IEnumerable<MetadataReference> references
    )
    {
        return CSharpCompilation.Create(
            assemblyName,
            sources.Select(source => CSharpSyntaxTree.ParseText(source.Source, ParseOptions, source.Path)),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
    }

    /// <summary>
    /// Creates a driver that records every tracked step, for tests that assert which steps were reused between runs.
    /// </summary>
    public static GeneratorDriver CreateTrackingDriver(
        IIncrementalGenerator generator,
        AnalyzerConfigOptionsProvider? optionsProvider = null
    )
    {
        return CSharpGeneratorDriver.Create(
            [generator.AsSourceGenerator()],
            parseOptions: ParseOptions,
            optionsProvider: optionsProvider,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true
            )
        );
    }
}
