// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Generator.Primitives.Models;
using Headless.Generator.Primitives.Shared;
using Headless.SourceGenerators;
using Microsoft.CodeAnalysis;

namespace Headless.Generator.Primitives.Helpers;

internal static class EntityFrameworkValueConverterSourceFilesGeneratorEmitter
{
    private const string _HelperClassName = "AddPrimitivesValueConvertersExtensions";
    private const string _HelperMethodName = "AddPrimitivePropertyConversions";

    private static string _CreateHelperNamespaceName(string assemblyName)
    {
        return $"{NamespaceName.From(assemblyName)}.Converters.Extensions";
    }

    private static string _CreateConverterClassName(string className)
    {
        return $"{className}ValueConverter";
    }

    /// <summary>Processes the Entity Framework value converter for the specified generator data and source production context.</summary>
    internal static void AddEntityFrameworkValueConverter(this SourceProductionContext context, GeneratorData data)
    {
        var builder = new SourceCodeBuilder();

        builder.AppendSourceHeader("Primitives Generator");

        builder.AppendUsings([
            data.Namespace,
            data.PrimitiveTypeNamespace,
            AbstractionConstants.Namespace,
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Storage.ValueConversion",
        ]);

        builder.AppendNamespace(data.ConvertersNamespace);

        var converterName = _CreateConverterClassName(data.ClassName);

        builder.AppendSummary($"ValueConverter for <see cref = \"{data.ClassName}\"/>");
        builder.AppendClass(
            isRecord: false,
            "public sealed",
            converterName,
            $"{TypeNames.ValueConverter}<{data.ClassName}, {data.PrimitiveTypeFriendlyName}>"
        );

        builder.AppendLine($"public {converterName}() : base(v => v, v => v)" + " { }");
        builder.NewLine();

        builder.AppendLine(
            $"public {converterName}({TypeNames.ConverterMappingHints}? mappingHints = null) : base(v => v, v => v, mappingHints) {{ }}"
        );

        builder.CloseBracket();

        context.AddSource(GeneratorData.HintName($"{data.ConvertersNamespace}.{converterName}"), builder.ToString());
    }

    /// <summary>Generates the value converters add all helpers.</summary>
    internal static void AddEntityFrameworkValueConvertersHelper(
        this SourceProductionContext context,
        string assemblyName,
        List<GeneratorData> types,
        bool addAssemblyAttribute
    )
    {
        var builder = new SourceCodeBuilder();

        builder.AppendSourceHeader("Primitives Generator");

        // Types are referenced by full name: primitives that share a simple name in different namespaces would make
        // an imported short name ambiguous.
        builder.AppendUsings(["Microsoft.EntityFrameworkCore"]);

        if (addAssemblyAttribute)
        {
            builder.AppendLine($"[assembly: {AbstractionConstants.PrimitiveAssemblyAttributeFullName}]");
        }

        builder.AppendNamespace(_CreateHelperNamespaceName(assemblyName));

        builder.AppendSummary(
            $"Helper class providing methods to configure Entity Framework Value Converters for Primitive types of {assemblyName}"
        );

        builder.AppendClass(isRecord: false, "public static", _HelperClassName);

        builder.AppendSummary(
            "Adds Entity Framework Value Converters for specific custom types to ensure proper mapping to EF ORM."
        );

        builder.AppendParamDescription(
            "configurationBuilder",
            "The ModelConfigurationBuilder instance to which converters are added."
        );

        builder
            .AppendLine(
                $"public static {TypeNames.ModelConfigurationBuilder} {_HelperMethodName}(this {TypeNames.ModelConfigurationBuilder} configurationBuilder)"
            )
            .OpenBracket();

        foreach (var data in types)
        {
            builder.AppendLine(
                $"configurationBuilder.Properties<global::{data.FullyQualifiedName}>().HaveConversion<global::{data.ConvertersNamespace}.{_CreateConverterClassName(data.ClassName)}>();"
            );
        }

        builder.AppendLine("return configurationBuilder;");
        builder.CloseBracket();

        builder.CloseBracket();
        context.AddSource($"{_HelperClassName}.g.cs", builder.ToString());
    }
}
