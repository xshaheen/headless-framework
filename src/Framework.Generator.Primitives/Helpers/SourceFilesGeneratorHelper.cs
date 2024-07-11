using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.CodeAnalysis;
using Primitives.Generator.Extensions;
using Primitives.Generator.Models;

namespace Primitives.Generator.Helpers;

internal static class SourceFilesGeneratorHelper
{
    private const string _SwashbuckleSwaggerExtensionsClassName = "AddSwashbuckleSwaggerPrimitivesMappingsExtensions";
    private const string _SwashbuckleAddPrimitivesMethodName = "AddSwaggerPrimitiveMappings";
    private const string _NswagSwaggerExtensionsClassName = "AddNswagSwaggerPrimitivesMappingsExtensions";
    private const string _NswagAddPrimitivesMethodName = "AddSwaggerPrimitiveMappings";
    private const string _ValueConverterExtensionsClassName = "AddPrimitivesValueConvertersExtensions";

    /// <summary>Adds Swagger mappings for specific custom types to ensure proper OpenAPI documentation generation.</summary>
    /// <param name="context">The source production context.</param>
    /// <param name="assemblyName">The AssemblyName of the project.</param>
    /// <param name="types">A list of custom types to add Swagger mappings for.</param>
    /// <param name="addAssemblyAttribute">Add PrimitiveAssemblyAttribute assembly attribute</param>
    internal static void AddSwashbuckleSwaggerMappingsHelper(
        this SourceProductionContext context,
        string assemblyName,
        List<GeneratorData> types,
        bool addAssemblyAttribute = true
    )
    {
        if (types.Count == 0)
        {
            return;
        }

        var builder = new SourceCodeBuilder();

        builder.AppendSourceHeader("Primitives Generator");

        var usings = types.ConvertAll(x => x.Namespace);
        usings.Add("Microsoft.Extensions.DependencyInjection");
        usings.Add("Swashbuckle.AspNetCore.SwaggerGen");
        usings.Add("Microsoft.OpenApi.Models");
        usings.Add("Microsoft.OpenApi.Any");
        usings.Add("Primitives");
        builder.AppendUsings(usings);

        if (addAssemblyAttribute)
        {
            builder.AppendLine("[assembly: Primitives.PrimitiveAssemblyAttribute]");
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
            var (typeName, format) = data.PrimitiveTypeSymbol.GetSwashbuckleSwaggerTypeAndFormat();

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
                    var xmlDoc = _LoadXmlDocument(xmlDocumentation!);

                    // Select the <summary> node
                    var summaryNode = xmlDoc.SelectSingleNode("member/summary");

                    if (summaryNode is not null)
                    {
                        builder.Append("Description = @").AppendQuoted(summaryNode.InnerText.Trim()).AppendLine(",");
                    }

                    var example = xmlDoc.SelectSingleNode("member/example");

                    if (example is not null)
                    {
                        var exampleValue = example.InnerText.Trim().Replace("\"", "\\\"");

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

    /// <summary>Adds Swagger mappings for specific custom types to ensure proper OpenAPI documentation generation.</summary>
    /// <param name="context">The source production context.</param>
    /// <param name="assemblyName">The AssemblyName of the project.</param>
    /// <param name="types">A list of custom types to add Swagger mappings for.</param>
    /// <param name="addAssemblyAttribute">Add PrimitiveAssemblyAttribute assembly attribute</param>
    internal static void AddNswagSwaggerMappingsHelper(
        this SourceProductionContext context,
        string assemblyName,
        List<GeneratorData> types,
        bool addAssemblyAttribute = true
    )
    {
        if (types.Count == 0)
        {
            return;
        }

        var builder = new SourceCodeBuilder();

        builder.AppendSourceHeader("Primitives Generator");

        var usings = types.ConvertAll(x => x.Namespace);
        usings.Add("Microsoft.Extensions.DependencyInjection");
        usings.Add("NJsonSchema");
        usings.Add("NJsonSchema.Generation");
        usings.Add("NJsonSchema.Generation.TypeMappers");
        usings.Add("Microsoft.OpenApi.Models");
        usings.Add("Microsoft.OpenApi.Any");
        usings.Add("Primitives");
        builder.AppendUsings(usings);

        if (addAssemblyAttribute)
        {
            builder.AppendLine("[assembly: Primitives.PrimitiveAssemblyAttribute]");
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
                    var xmlDoc = _LoadXmlDocument(xmlDocumentation!);

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

    /// <summary>Generates the value converters extension for the specified assembly name, types, and source production context.</summary>
    /// <param name="context">The source production context.</param>
    /// <param name="addAssemblyAttribute">if assembly attribute should be added</param>
    /// <param name="assemblyName">The name of the assembly.</param>
    /// <param name="types">The list of named type symbols.</param>
    internal static void AddValueConvertersHelper(
        this SourceProductionContext context,
        bool addAssemblyAttribute,
        string assemblyName,
        List<INamedTypeSymbol> types
    )
    {
        if (types.Count == 0)
        {
            return;
        }

        var builder = new SourceCodeBuilder();
        builder.AppendSourceHeader("Primitives Generator");

        var usings = types.ConvertAll(x => x.ContainingNamespace.ToDisplayString());
        usings.Add("Microsoft.EntityFrameworkCore");

        usings.AddRange(
            types.ConvertAll(x => x.ContainingNamespace.ToDisplayString() + ".EntityFrameworkCore.Converters")
        );

        builder.AppendUsings(usings);

        if (addAssemblyAttribute)
        {
            builder.AppendLine("[assembly: Primitives.PrimitiveAssemblyAttribute]");
        }

        var ns = string.Join(".", assemblyName.Split('.').Select(s => char.IsDigit(s[0]) ? '_' + s : s));

        builder.AppendNamespace(ns + ".Converters.Extensions");

        builder.AppendSummary(
            $"Helper class providing methods to configure EntityFrameworkCore ValueConverters for Primitive types of {assemblyName}"
        );

        builder.AppendClass(false, "public static", _ValueConverterExtensionsClassName);

        builder.AppendSummary(
            "Adds EntityFrameworkCore ValueConverters for specific custom types to ensure proper mapping to EFCore ORM."
        );

        builder.AppendParamDescription(
            "configurationBuilder",
            "The ModelConfigurationBuilder instance to which converters are added."
        );

        builder
            .AppendLine(
                "public static ModelConfigurationBuilder AddPrimitivePropertyConversions(this ModelConfigurationBuilder configurationBuilder)"
            )
            .OpenBracket();

        foreach (var type in types)
        {
            builder
                .Append("configurationBuilder.Properties<")
                .Append(type.Name)
                .Append(">().HaveConversion<")
                .Append(type.Name)
                .AppendLine("ValueConverter>();");
        }

        builder.AppendLine("return configurationBuilder;");
        builder.CloseBracket();

        builder.CloseBracket();
        context.AddSource($"{_ValueConverterExtensionsClassName}.g.cs", builder.ToString());
    }

    /// <summary>Processes the generator data and generates code for a specified class.</summary>
    /// <param name="data">The GeneratorData for the class.</param>
    /// <param name="ctorCode">The constructor code for the class.</param>
    /// <param name="options">The PrimitiveGlobalOptions for the generator.</param>
    /// <param name="context">The SourceProductionContext for reporting diagnostics.</param>
    internal static void AddPrimateImplementation(
        this SourceProductionContext context,
        GeneratorData data,
        string ctorCode,
        PrimitiveGlobalOptions options
    )
    {
        var modifiers = data.TypeSymbol.GetModifiers() ?? "public partial";

        if (!modifiers.Contains("partial"))
        {
            context.ReportDiagnostic(DiagnosticHelper.ClassMustBePartial(data.TypeSymbol.Locations.FirstOrDefault()));
        }

        var builder = new SourceCodeBuilder();

        var usings = new List<string>
        {
            "System",
            "System.Numerics",
            "System.Diagnostics",
            "System.Runtime.CompilerServices",
            "Primitives"
        };

        if (data.ParentSymbols.Count > 0)
        {
            usings.Add(data.ParentSymbols[0].ContainingNamespace.ToDisplayString());
        }

        if (data.GenerateImplicitOperators)
        {
            usings.Add("System.Diagnostics.CodeAnalysis");
        }

        if (options.GenerateJsonConverters)
        {
            usings.Add("System.Text.Json.Serialization");
            usings.Add($"{data.Namespace}.Converters");
        }

        if (options.GenerateTypeConverters)
        {
            usings.Add("System.ComponentModel");
        }

        if (options.GenerateXmlConverters)
        {
            usings.Add("System.Xml");
            usings.Add("System.Xml.Schema");
            usings.Add("System.Xml.Serialization");
        }

        var needsMathOperators = data.HasMathOperators();

        var isByteOrShort = data.ParentSymbols.Count == 0 && data.UnderlyingType.IsByteOrShort();

        builder.AppendSourceHeader("Primitives Generator");
        builder.AppendUsings(usings);
        builder.AppendNamespace(data.Namespace);

        if (options.GenerateJsonConverters)
        {
            builder.AppendLine($"[JsonConverter(typeof({data.ClassName + "JsonConverter"}))]");
        }

        if (options.GenerateTypeConverters)
        {
            builder.AppendLine($"[TypeConverter(typeof({data.ClassName + "TypeConverter"}))]");
        }

        builder.Append("[UnderlyingPrimitiveType(typeof(").Append(data.PrimitiveTypeFriendlyName).AppendLine("))]");

        builder.AppendLine("[DebuggerDisplay(\"{\" + nameof(_value) + \"}\")]");

        if (!data.TypeSymbol.IsValueType)
        {
            builder.AppendClass(false, modifiers, data.ClassName, createInheritedInterfaces(data, data.ClassName));
        }
        else
        {
            builder.AppendStruct(modifiers, data.ClassName, createInheritedInterfaces(data, data.ClassName));
        }

        builder
            .AppendInheritDoc()
            .Append("public Type GetUnderlyingPrimitiveType() => typeof(")
            .Append(data.PrimitiveTypeFriendlyName)
            .AppendLine(");")
            .NewLine();

        builder
            .AppendInheritDoc()
            .AppendLine($"public {data.PrimitiveTypeFriendlyName} GetUnderlyingPrimitiveValue() => this;")
            .NewLine();

        builder.AppendLines(ctorCode);

        if (needsMathOperators && isByteOrShort)
        {
            // Add int constructor
            builder.AppendComment("Private constructor with 'int' value");

            builder
                .Append(
                    $"private {data.ClassName}(int value) : this(value is >= {data.PrimitiveTypeFriendlyName}.MinValue and <= {data.PrimitiveTypeFriendlyName}.MaxValue ? ({data.PrimitiveTypeFriendlyName})value : throw new InvalidPrimitiveValueException(\"The value has exceeded a {data.PrimitiveTypeFriendlyName} limit\"))"
                )
                .EmptyBracket()
                .NewLine()
                .NewLine();
        }

        builder.GenerateMandatoryMethods(data);

        builder.NewLine();
        builder.AppendRegion("IEquatable Implementation");
        builder.GenerateEquatableOperators(data.ClassName, data.TypeSymbol.IsValueType);
        builder.NewLine();
        builder.AppendEndRegion();

        builder.NewLine();
        builder.AppendRegion("IComparable Implementation");
        builder.GenerateComparableCode(data.ClassName, data.TypeSymbol.IsValueType);
        builder.NewLine();
        builder.AppendEndRegion();

        if (data.GenerateParsable)
        {
            builder.NewLine();
            builder.AppendRegion("IParsable Implementation");
            builder.GenerateParsable(data);
            builder.NewLine();
            builder.AppendEndRegion();
        }

        if (data.GenerateSpanFormattable)
        {
            builder.NewLine();
            builder.AppendRegion("IFormattable Implementation");
            builder.GenerateSpanFormattable(data.FieldName);
            builder.NewLine();
            builder.AppendEndRegion();
        }

        if (data.GenerateUtf8SpanFormattable)
        {
            builder.NewLine();
            builder.AppendRegion("IUtf8SpanFormattable Implementation");
            builder.GenerateUtf8Formattable(data.FieldName);
            builder.NewLine();
            builder.AppendEndRegion();
        }

        if (data.GenerateConvertibles)
        {
            builder.NewLine();
            builder.AppendRegion("IConvertible Implementation");
            builder.GenerateConvertibles(data);
            builder.NewLine();
            builder.AppendEndRegion();
        }

        if (data.GenerateXmlSerializableMethods)
        {
            builder.NewLine();
            builder.AppendRegion("IXmlSerializable Implementation");
            builder.GenerateIXmlSerializableMethods(data);
            builder.NewLine();
            builder.AppendEndRegion();
        }

        if (data.GenerateImplicitOperators)
        {
            builder.NewLine();
            builder.AppendRegion("Implicit Operators");
            builder.GenerateImplicitOperators(data);
            builder.AppendEndRegion();
        }

        if (needsMathOperators)
        {
            builder.NewLine();
            builder.AppendRegion("Math Operators");

            if (data.GenerateAdditionOperators)
            {
                builder.GenerateAdditionCode(data.ClassName, data.FieldName);
                builder.NewLine();
            }

            if (data.GenerateSubtractionOperators)
            {
                builder.GenerateSubtractionCode(data.ClassName, data.FieldName);
                builder.NewLine();
            }

            if (data.GenerateMultiplyOperators)
            {
                builder.GenerateMultiplyCode(data.ClassName, data.FieldName);
                builder.NewLine();
            }

            if (data.GenerateDivisionOperators)
            {
                builder.GenerateDivisionCode(data.ClassName, data.FieldName);
                builder.NewLine();
            }

            if (data.GenerateModulusOperator)
            {
                builder.GenerateModulusCode(data.ClassName, data.FieldName);
                builder.NewLine();
            }

            builder.AppendEndRegion();
        }

        if (data.GenerateComparison)
        {
            builder.NewLine();
            builder.AppendRegion("Comparison Operators");
            builder.GenerateComparisonCode(data.ClassName, data.FieldName);
            builder.NewLine();
            builder.AppendEndRegion();
        }

        var baseType = data.ParentSymbols.Count == 0 ? data.PrimitiveTypeSymbol : data.ParentSymbols[0];

        var hasExplicitToStringMethod = data
            .TypeSymbol.GetMembersOfType<IMethodSymbol>()
            .Any(x =>
                string.Equals(x.Name, "ToString", StringComparison.Ordinal)
                && x is { IsStatic: true, Parameters.Length: 1 }
                && x.Parameters[0].Type.Equals(baseType, SymbolEqualityComparer.Default)
            );

        builder.NewLine();
        builder.AppendInheritDoc();
        builder.AppendMethodImplAggressiveInliningAttribute();

        builder.AppendLine(
            hasExplicitToStringMethod
                ? $"public override string ToString() => ToString({data.FieldName});"
                : $"public override string ToString() => {data.FieldName}.ToString();"
        );

        if (data.GenerateHashCode)
        {
            builder.NewLine();
            builder.AppendInheritDoc();
            builder.AppendMethodImplAggressiveInliningAttribute();

            if (data.IsPrimitiveUnderlyingTypString())
            {
                builder.AppendLine(
                    $"public override int GetHashCode() => {data.FieldName}.GetHashCode(StringComparison.Ordinal);"
                );
            }
            else
            {
                builder.AppendLine($"public override int GetHashCode() => {data.FieldName}.GetHashCode();");
            }
        }

        builder.CloseBracket();

        context.AddSource(data.ClassName + ".g", builder.ToString());

        return;

        static string createInheritedInterfaces(GeneratorData data, string className)
        {
            var sb = new StringBuilder(8096);

            sb.Append("IEquatable<").Append(className).Append('>');

            appendInterface(sb, nameof(IComparable));
            appendInterface(sb, "IComparable<").Append(className).Append('>');

            if (data.GenerateAdditionOperators)
            {
                appendInterface(sb, "IAdditionOperators<")
                    .Append(className)
                    .Append(", ")
                    .Append(className)
                    .Append(", ")
                    .Append(className)
                    .Append('>');
            }

            if (data.GenerateSubtractionOperators)
            {
                appendInterface(sb, "ISubtractionOperators<")
                    .Append(className)
                    .Append(", ")
                    .Append(className)
                    .Append(", ")
                    .Append(className)
                    .Append('>');
            }

            if (data.GenerateMultiplyOperators)
            {
                appendInterface(sb, "IMultiplyOperators<")
                    .Append(className)
                    .Append(", ")
                    .Append(className)
                    .Append(", ")
                    .Append(className)
                    .Append('>');
            }

            if (data.GenerateDivisionOperators)
            {
                appendInterface(sb, "IDivisionOperators<")
                    .Append(className)
                    .Append(", ")
                    .Append(className)
                    .Append(", ")
                    .Append(className)
                    .Append('>');
            }

            if (data.GenerateModulusOperator)
            {
                appendInterface(sb, "IModulusOperators<")
                    .Append(className)
                    .Append(", ")
                    .Append(className)
                    .Append(", ")
                    .Append(className)
                    .Append('>');
            }

            if (data.GenerateComparison)
            {
                appendInterface(sb, "IComparisonOperators<")
                    .Append(className)
                    .Append(", ")
                    .Append(className)
                    .Append(", ")
                    .Append("bool>");
            }

            if (data.GenerateSpanFormattable)
            {
                appendInterface(sb, "ISpanFormattable");
            }

            if (data.GenerateParsable)
            {
                appendInterface(sb, "ISpanParsable<").Append(className).Append('>');
            }

            if (data.GenerateConvertibles)
            {
                appendInterface(sb, nameof(IConvertible));
            }

            if (data.GenerateXmlSerializableMethods)
            {
                appendInterface(sb, nameof(IXmlSerializable));
            }

            if (data.GenerateUtf8SpanFormattable)
            {
                sb.AppendLine().Append("#if NET8_0_OR_GREATER");
                appendInterface(sb, "IUtf8SpanFormattable");
                sb.AppendLine().Append("#endif");
            }

            return sb.ToString();

            static StringBuilder appendInterface(StringBuilder sb, string interfaceName) =>
                sb.AppendLine().Append(SourceCodeBuilder.GetIndentation(2)).Append(", ").Append(interfaceName);
        }
    }

    /// <summary>Generates code for a TypeConverter for the specified type.</summary>
    /// <param name="data">The generator data containing type information.</param>
    /// <param name="context">The source production context.</param>
    internal static void AddTypeConverter(this SourceProductionContext context, GeneratorData data)
    {
        var friendlyName = data.UnderlyingType.ToString();
        var builder = new SourceCodeBuilder();

        builder.AppendSourceHeader("Primitives Generator");

        builder.AppendUsings([data.Namespace, "System", "System.ComponentModel", "System.Globalization", "Primitives"]);
        builder.AppendNamespace(data.Namespace + ".Converters");
        builder.AppendSummary($"TypeConverter for <see cref = \"{data.ClassName}\"/>");

        builder.AppendClass(false, "public sealed", data.ClassName + "TypeConverter", $"{friendlyName}Converter");

        builder
            .AppendInheritDoc()
            .AppendLine(
                "public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)"
            )
            .OpenBracket();

        if (data.SerializationFormat is not null)
        {
            builder
                .AppendLine("if (value is string s)")
                .OpenBracket()
                .AppendLine("try")
                .OpenBracket()
                .Append("return ")
                .Append(data.ClassName)
                .AppendLine(".Parse(s, culture);")
                .CloseBracket()
                .AppendLine("catch (InvalidPrimitiveValueException ex)")
                .OpenBracket()
                .Append("throw new FormatException(\"Cannot parse ")
                .AppendLine("\", ex);")
                .CloseBracket()
                .CloseBracket()
                .NewLine()
                .AppendLine("return base.ConvertFrom(context, culture, value);");
        }
        else
        {
            builder
                .AppendLine("var result = base.ConvertFrom(context, culture, value);")
                .NewLine()
                .AppendLine("if (result is null)")
                .OpenBracket()
                .AppendLine("return null;")
                .CloseBracket()
                .NewLine()
                .AppendLine("try")
                .OpenBracket()
                .AppendLine($"return new {data.ClassName}(({data.PrimitiveTypeFriendlyName})result);")
                .CloseBracket()
                .AppendLine("catch (InvalidPrimitiveValueException ex)")
                .OpenBracket()
                .Append("throw new FormatException(\"Cannot parse ")
                .Append(data.ClassName)
                .AppendLine("\", ex);")
                .CloseBracket();
        }

        builder.CloseBracket().CloseBracket();

        context.AddSource($"{data.ClassName}TypeConverter.g.cs", builder.ToString());
    }

    /// <summary>Processes the Entity Framework value converter for the specified generator data and source production context.</summary>
    /// <param name="data">The generator data.</param>
    /// <param name="context">The source production context.</param>
    internal static void AddEntityFrameworkValueConverter(this SourceProductionContext context, GeneratorData data)
    {
        var builder = new SourceCodeBuilder();

        builder.AppendSourceHeader("Primitives Generator");

        var converterName = data.ClassName + "ValueConverter";

        builder.AppendUsings(
            [
                data.Namespace,
                data.PrimitiveTypeSymbol.ContainingNamespace.ToDisplayString(),
                "Microsoft.EntityFrameworkCore",
                "Microsoft.EntityFrameworkCore.Storage.ValueConversion",
                "Primitives",
            ]
        );

        builder.AppendNamespace(data.Namespace + ".EntityFrameworkCore.Converters");
        builder.AppendSummary($"ValueConverter for <see cref = \"{data.ClassName}\"/>");

        builder.AppendClass(
            false,
            "public sealed",
            converterName,
            $"ValueConverter<{data.ClassName}, {data.PrimitiveTypeFriendlyName}>"
        );

        builder.AppendLine($"public {converterName}() : base(v => v, v => v)" + " { }");
        builder.NewLine();

        builder.AppendLine(
            $"public {converterName}(ConverterMappingHints? mappingHints = null) : base(v => v, v => v, mappingHints)"
                + " { }"
        );

        builder.CloseBracket();

        context.AddSource($"{converterName}.g.cs", builder.ToString());
    }

    /// <summary>Generates code for a JsonConverter for the specified type.</summary>
    /// <param name="data">The generator data containing type information.</param>
    /// <param name="context">The source production context.</param>
    internal static void AddJsonConverter(this SourceProductionContext context, GeneratorData data)
    {
        var builder = new SourceCodeBuilder();

        builder.AppendSourceHeader("Primitives Generator");

        builder.AppendUsings(
            [
                data.Namespace,
                "System",
                "System.Text.Json",
                "System.Text.Json.Serialization",
                "System.Globalization",
                "System.Text.Json.Serialization.Metadata",
                "Primitives"
            ]
        );

        var converterName = data.UnderlyingType.ToString();
        var primitiveTypeIsValueType = data.PrimitiveTypeSymbol.IsValueType;

        builder.AppendNamespace(data.Namespace + ".Converters");
        builder.AppendSummary($"JsonConverter for <see cref = \"{data.ClassName}\"/>");

        builder.AppendClass(
            false,
            "public sealed",
            data.ClassName + "JsonConverter",
            $"JsonConverter<{data.ClassName}>"
        );

        builder
            .AppendInheritDoc()
            .Append("public override ")
            .Append(data.ClassName)
            .AppendLine(" Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)")
            .OpenBracket();

        if (data.SerializationFormat is null)
        {
            builder
                .AppendLine("try")
                .OpenBracket()
                .AppendLine(
                    $"return JsonInternalConverters.{converterName}Converter.Read(ref reader, typeToConvert, options){(primitiveTypeIsValueType ? "" : "!")};"
                )
                .CloseBracket();
        }
        else
        {
            builder
                .AppendLine("if (reader.TokenType != JsonTokenType.String)")
                .AppendIndentation()
                .Append("throw new JsonException(\"Expected a string value to deserialize ")
                .Append(data.ClassName)
                .AppendLine("\");")
                .NewLine()
                .Append(
                    "var str = reader.GetString() ?? throw new JsonException(\"Expected a non-null string value to deserialize "
                )
                .Append(data.ClassName)
                .AppendLine("\");")
                .AppendLine("try")
                .OpenBracket()
                .Append("return ")
                .Append(data.ClassName)
                .AppendLine(".Parse(str, CultureInfo.InvariantCulture);")
                .CloseBracket();
        }

        builder
            .AppendLine("catch (InvalidPrimitiveValueException ex)")
            .OpenBracket()
            .AppendLine("throw new JsonException(ex.Message);")
            .CloseBracket()
            .CloseBracket()
            .NewLine();

        builder
            .AppendInheritDoc()
            .AppendLine(
                $"public override void Write(Utf8JsonWriter writer, {data.ClassName} value, JsonSerializerOptions options)"
            )
            .OpenBracket()
            .AppendLineIf(
                data.SerializationFormat is null,
                $"JsonInternalConverters.{converterName}Converter.Write(writer, ({data.PrimitiveTypeFriendlyName})value, options);"
            )
            .AppendLineIf(
                data.SerializationFormat is not null,
                $"writer.WriteStringValue(value.ToString(\"{data.SerializationFormat}\", CultureInfo.InvariantCulture));"
            )
            .CloseBracket()
            .NewLine();

        builder
            .AppendInheritDoc()
            .Append("public override ")
            .Append(data.ClassName)
            .AppendLine(
                " ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)"
            )
            .OpenBracket();

        if (data.SerializationFormat is null)
        {
            builder
                .AppendLine("try")
                .OpenBracket()
                .AppendLine(
                    $"return JsonInternalConverters.{converterName}Converter.ReadAsPropertyName(ref reader, typeToConvert, options){(primitiveTypeIsValueType ? "" : "!")};"
                )
                .CloseBracket();
        }
        else
        {
            builder
                .AppendLine("if (reader.TokenType != JsonTokenType.String)")
                .AppendIndentation()
                .Append("throw new JsonException(\"Expected a string value to deserialize ")
                .Append(data.ClassName)
                .AppendLine("\");")
                .NewLine()
                .Append(
                    "var str = reader.GetString() ?? throw new JsonException(\"Expected a non-null string value to deserialize "
                )
                .Append(data.ClassName)
                .AppendLine("\");")
                .AppendLine("try")
                .OpenBracket()
                .Append("return ")
                .Append(data.ClassName)
                .AppendLine(".Parse(str, CultureInfo.InvariantCulture);")
                .CloseBracket();
        }

        builder
            .AppendLine("catch (InvalidPrimitiveValueException ex)")
            .OpenBracket()
            .AppendLine("throw new JsonException(ex.Message);")
            .CloseBracket()
            .CloseBracket()
            .NewLine();

        builder
            .AppendInheritDoc()
            .Append("public override void WriteAsPropertyName(Utf8JsonWriter writer, ")
            .Append(data.ClassName)
            .AppendLine(" value, JsonSerializerOptions options)")
            .OpenBracket()
            .AppendLineIf(
                data.SerializationFormat is null,
                $"JsonInternalConverters.{converterName}Converter.WriteAsPropertyName(writer, ({data.PrimitiveTypeFriendlyName})value, options);"
            )
            .AppendLineIf(
                data.SerializationFormat is not null,
                $"writer.WritePropertyName(value.ToString(\"{data.SerializationFormat}\", CultureInfo.InvariantCulture));"
            )
            .CloseBracket();

        builder.CloseBracket();

        context.AddSource($"{data.ClassName}JsonConverter.g.cs", builder.ToString());
    }

    private static XmlDocument _LoadXmlDocument(string xml)
    {
        var xmlDoc = new XmlDocument();
        xmlDoc.LoadXml(xml);

        return xmlDoc;
    }
}
