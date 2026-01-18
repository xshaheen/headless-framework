// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Generator.Primitives.Models;
using Framework.Generator.Primitives.Shared;
using Microsoft.CodeAnalysis;

namespace Framework.Generator.Primitives.Helpers;

internal static class TypeConverterSourceFilesGeneratorEmitter
{
    /// <summary>Generates code for a TypeConverter for the specified type.</summary>
    /// <param name="context">The source production context.</param>
    /// <param name="data">The generator data containing type information.</param>
    internal static void AddTypeConverter(this SourceProductionContext context, GeneratorData data)
    {
        var friendlyName = data.UnderlyingType.ToString();
        var builder = new SourceCodeBuilder();

        builder.AppendSourceHeader("Primitives Generator");

        builder.AppendUsings([
            data.Namespace,
            "System",
            "System.ComponentModel",
            "System.Globalization",
            AbstractionConstants.Namespace,
        ]);
        builder.AppendNamespace(data.Namespace + ".Converters");
        builder.AppendSummary($"TypeConverter for <see cref = \"{data.ClassName}\"/>");

        builder.AppendClass(
            isRecord: false,
            "public sealed",
            CreateClassName(data.ClassName),
            $"{friendlyName}Converter"
        );

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

        context.AddSource(_CreateFileName(data.ClassName), builder.ToString());
    }

    public static string CreateClassName(string className) => $"{className}TypeConverter";

    private static string _CreateFileName(string className) => $"{CreateClassName(className)}.g.cs";
}
