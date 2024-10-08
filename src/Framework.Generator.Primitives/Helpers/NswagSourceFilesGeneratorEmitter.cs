// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Generator.Primitives.Extensions;
using Framework.Generator.Primitives.Models;
using Framework.Generator.Primitives.Shared;
using Microsoft.CodeAnalysis;

namespace Framework.Generator.Primitives.Helpers;

internal static class NswagSourceFilesGeneratorEmitter
{
    private const string _NswagSwaggerExtensionsClassName = "AddNswagSwaggerPrimitivesMappingsExtensions";
    private const string _NswagAddPrimitivesMethodName = "AddSwaggerPrimitiveMappings";

    /// <summary>Adds Swagger mappings for specific custom types to ensure proper OpenAPI documentation generation.</summary>
    /// <param name="context">The source production context.</param>
    /// <param name="assemblyName">The AssemblyName of the project.</param>
    /// <param name="types">A list of custom types to add Swagger mappings for.</param>
    /// <param name="addAssemblyAttribute">Add PrimitiveAssemblyAttribute assembly attribute</param>
    internal static void AddNswagSwaggerMappingsHelper(
        this SourceProductionContext context,
        string assemblyName,
        List<GeneratorData> types,
        bool addAssemblyAttribute
    )
    {
        var builder = new SourceCodeBuilder();

        builder.AppendSourceHeader("Primitives Generator");

        var usings = types.ConvertAll(x => x.Namespace);
        usings.Add("Microsoft.Extensions.DependencyInjection");
        usings.Add("NJsonSchema");
        usings.Add("NJsonSchema.Generation");
        usings.Add("NJsonSchema.Generation.TypeMappers");
        usings.Add("Microsoft.OpenApi.Models");
        usings.Add("Microsoft.OpenApi.Any");
        usings.Add(AbstractionConstants.Namespace);
        builder.AppendUsings(usings);

        if (addAssemblyAttribute)
        {
            builder.AppendLine($"[assembly: {AbstractionConstants.PrimitiveAssemblyAttributeFullName}]");
        }

        var ns = string.Join(".", assemblyName.Split('.').Select(s => char.IsDigit(s[0]) ? '_' + s : s));

        builder.AppendNamespace(ns + ".Converters.Extensions");

        builder.AppendSummary(
            $"Helper class providing methods to configure Swagger mappings for Primitive types of {assemblyName}"
        );

        builder.AppendClass(isRecord: false, "public static", _NswagSwaggerExtensionsClassName);

        builder.AppendSummary(
            "Adds Swagger mappings for specific custom types to ensure proper OpenAPI documentation generation."
        );

        builder.AppendParamDescription(
            "settings",
            "The JsonSchemaGeneratorSettings instance to which mappings are added."
        );

        builder.AppendLine("/// <remarks>");
        builder.AppendLine("/// The method adds Swagger mappings for the following types:");

        foreach (var data in types)
        {
            builder.Append("/// <see cref=\"").Append(data.ClassName).AppendLine("\" />");
        }

        builder.AppendLine("/// </remarks>");

        builder
            .AppendLine(
                $"public static void {_NswagAddPrimitivesMethodName}(this JsonSchemaGeneratorSettings settings)"
            )
            .OpenBracket();

        foreach (var data in types)
        {
            // Get the XML documentation comment for the namedTypeSymbol
            var xmlDocumentation = data.TypeSymbol.GetDocumentationCommentXml(
                cancellationToken: context.CancellationToken
            );

            addMapping(isNullable: false);

            if (data.TypeSymbol.IsValueType)
            {
                addMapping(isNullable: true);
            }

            continue;

            void addMapping(bool isNullable)
            {
                var primitiveSwaggerInfo = data.PrimitiveTypeSymbol.GetNswagSwaggerTypeAndFormatAndExample();

                builder
                    .AppendLine("settings.TypeMappers.Add(")
                    .IncreaseIndentation()
                    .AppendLine("new PrimitiveTypeMapper(")
                    .IncreaseIndentation()
                    .AppendLine($"typeof({data.ClassName}{(isNullable ? "?" : "")}),")
                    .AppendLine("schema =>")
                    .OpenBracket()
                    .AppendLine($"schema.Type = {primitiveSwaggerInfo.Type};");

                if (primitiveSwaggerInfo.Format is not null)
                {
                    builder.AppendLine($"schema.Format = {primitiveSwaggerInfo.Format};");
                }

                if (isNullable)
                {
                    builder.AppendLine("schema.IsNullableRaw = true;");
                }

                builder
                    .Append("schema.Title = ")
                    .AppendQuoted(isNullable ? $"Nullable<{data.ClassName}>" : data.ClassName)
                    .AppendLine(";");

                if (!string.IsNullOrEmpty(xmlDocumentation))
                {
                    var xmlDoc = xmlDocumentation!.LoadXmlDocument();

                    // Select the <summary> node
                    var summaryNode = xmlDoc.SelectSingleNode("member/summary");

                    if (summaryNode is not null)
                    {
                        builder
                            .Append("schema.Description = @")
                            .AppendQuoted(summaryNode.InnerText.Trim())
                            .AppendLine(";");
                    }

                    var example = xmlDoc.SelectSingleNode("member/example");

                    if (example is not null)
                    {
                        var exampleValue = example.InnerText.Trim().Replace("\"", "\\\"");

                        builder.Append("schema.Example = ").Append("\"" + exampleValue + "\"").AppendLine(";");
                    }
                }

                builder
                    .CloseBracket()
                    .RemoveIndentations()
                    .CloseParenthesis()
                    .NewLine()
                    .RemoveIndentations()
                    .AppendLine(");");
            }
        }

        builder.CloseBracket();
        builder.CloseBracket();

        context.AddSource($"{_NswagSwaggerExtensionsClassName}.g.cs", builder.ToString());
    }
}
