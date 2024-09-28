// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Diagnostics.CodeAnalysis;
using Framework.Generator.Primitives;
using Framework.Generator.Primitives.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tests.Helpers;

internal static class TestHelpers
{
    internal static GeneratedOutput GetGeneratedOutput<T>(string source, PrimitiveGlobalOptions? globalOptions = null)
        where T : IIncrementalGenerator, new()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = AppDomain
            .CurrentDomain.GetAssemblies()
            .Where(x => !x.IsDynamic && !string.IsNullOrWhiteSpace(x.Location))
            .Select(x => MetadataReference.CreateFromFile(x.Location))
            .Concat(
                [
                    MetadataReference.CreateFromFile(typeof(T).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(IPrimitive<>).Assembly.Location),
                    MetadataReference.CreateFromFile(
                        typeof(System.ComponentModel.DataAnnotations.DisplayAttribute).Assembly.Location
                    ),
                ]
            );

        var compilation = CSharpCompilation.Create(
            "generator_Test",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var originalTreeCount = compilation.SyntaxTrees.Length;
        var generator = new T();

        var driver = CSharpGeneratorDriver
            .Create(generator)
            .WithUpdatedAnalyzerConfigOptions(
                new PrimitiveConfigOptionsProvider(globalOptions ?? new PrimitiveGlobalOptions())
            );

        var newDriver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var diagnostics
        );
        var trees = outputCompilation.SyntaxTrees.ToList();

        return new(
            diagnostics,
            trees.Count != originalTreeCount ? trees[1..].ConvertAll(x => x.ToString()) : [],
            newDriver
        );
    }

    private sealed class PrimitiveConfigOptionsProvider(PrimitiveGlobalOptions options) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            throw new NotSupportedException("Source generators do not need this");
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            throw new NotSupportedException("Source generators do not need this");
        }

        public override AnalyzerConfigOptions GlobalOptions { get; } = new DomainPrimitivesOptions(options);

        private sealed class DomainPrimitivesOptions(PrimitiveGlobalOptions options) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            {
                switch (key)
                {
                    case "build_property.PrimitiveJsonConverters":
                        value = options.GenerateJsonConverters.ToString();
                        return true;
                    case "build_property.PrimitiveTypeConverters":
                        value = options.GenerateTypeConverters.ToString();
                        return true;
                    case "build_property.PrimitiveXmlConverters":
                        value = options.GenerateXmlConverters.ToString();
                        return true;
                    case "build_property.PrimitiveNswagSwaggerConverters":
                        value = options.GenerateNswagSwaggerConverters.ToString();
                        return true;
                    case "build_property.PrimitiveSwashbuckleSwaggerConverters":
                        value = options.GenerateSwashbuckleSwaggerConverters.ToString();
                        return true;
                    case "build_property.PrimitiveEntityFrameworkValueConverters":
                        value = options.GenerateEntityFrameworkValueConverters.ToString();
                        return true;
                    default:
                        value = null;
                        return false;
                }
            }
        }
    }
}
