// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Generator.Primitives.Extensions;
using Headless.Generator.Primitives.Models;
using Headless.Generator.Primitives.Shared;
using Microsoft.CodeAnalysis;

namespace Headless.Generator.Primitives.Helpers;

internal static class SwashbuckleSourceFilesGeneratorEmitter
{
    private const string _SwashbuckleSwaggerExtensionsClassName = "AddSwashbuckleSwaggerPrimitivesMappingsExtensions";
    private const string _SwashbuckleAddPrimitivesMethodName = "AddSwaggerPrimitiveMappings";

    /// <summary>Adds Swagger mappings for specific custom types to ensure proper OpenAPI documentation generation.</summary>
    /// <param name="context">The source production context.</param>
    /// <param name="assemblyName">The AssemblyName of the project.</param>
    /// <param name="types">A list of custom types to add Swagger mappings for.</param>
    /// <param name="addAssemblyAttribute">Add PrimitiveAssemblyAttribute assembly attribute</param>
    internal static void AddSwashbuckleSwaggerMappingsHelper(
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
        usings.Add("Swashbuckle.AspNetCore.SwaggerGen");
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

        builder.AppendClass(isRecord: false, "public static", _SwashbuckleSwaggerExtensionsClassName);

        builder.AppendSummary(
            "Adds Swagger mappings for specific custom types to ensure proper OpenAPI documentation generation."
        );

        builder.AppendParamDescription("options", "The SwaggerGenOptions instance to which mappings are added.");

        builder.AppendLine("/// <remarks>");
        builder.AppendLine("/// The method adds Swagger mappings for the following types:");

        foreach (var data in types)
        {
            builder.Append("/// <see cref=\"").Append(data.ClassName).AppendLine("\" />");
        }

        builder.AppendLine("/// </remarks>");

        builder
            .AppendLine($"public static void {_SwashbuckleAddPrimitivesMethodName}(this SwaggerGenOptions options)")
            .OpenBracket();

        foreach (var data in types)
        {
            var (typeName, format) = data.UnderlyingType.GetSwashbuckleSwaggerTypeAndFormat();

            // Get the XML documentation comment from extracted data
            var xmlDocumentation = data.XmlDocumentation;

            addMapping(isNullable: false);

            if (data.IsValueType)
            {
                addMapping(isNullable: true);
            }

            continue;

            void addMapping(bool isNullable)
            {
                builder.Append("options.MapType<").Append(data.ClassName);

                if (isNullable)
                {
                    builder.Append("?");
                }

                builder
                    .Append(">(() => new OpenApiSchema")
                    .OpenBracket()
                    .Append("Type = ")
                    .AppendQuoted(typeName)
                    .AppendLine(",");

                if (!string.IsNullOrEmpty(format))
                {
                    builder.Append("Format = ").AppendQuoted(data.SerializationFormat ?? format).AppendLine(",");
                }

                if (isNullable)
                {
                    builder.AppendLine("Nullable = true,");
                }

                var title = isNullable ? $"Nullable<{data.ClassName}>" : data.ClassName;
                builder.Append("Title = ").AppendQuoted(title).AppendLine(",");

                if (!string.IsNullOrEmpty(xmlDocumentation))
                {
                    var xmlDoc = xmlDocumentation!.LoadXmlDocument();

                    // Select the <summary> node
                    var summaryNode = xmlDoc.SelectSingleNode("member/summary");

                    if (summaryNode is not null)
                    {
                        builder.Append("Description = @").AppendQuoted(summaryNode.InnerText.Trim()).AppendLine(",");
                    }

                    var example = xmlDoc.SelectSingleNode("member/example");

                    if (example is not null)
                    {
                        var exampleValue = _EscapeForStringLiteral(example.InnerText.Trim());

                        builder
                            .Append("Example = new OpenApiString(")
                            .Append("\"" + exampleValue + "\"")
                            .AppendLine("),");
                    }
                }

                builder.Length -= SourceCodeBuilder.NewLineLength + 1;
                builder.NewLine();
                builder.AppendLine("});");
            }
        }

        builder.CloseBracket();
        builder.CloseBracket();

        context.AddSource($"{_SwashbuckleSwaggerExtensionsClassName}.g.cs", builder.ToString());
    }

    private static string _EscapeForStringLiteral(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t")
            .Replace("\0", "\\0");
    }
}
