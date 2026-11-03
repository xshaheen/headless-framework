// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Generator.Primitives.Models;
using Headless.Generator.Primitives.Shared;
using Headless.SourceGenerators;
using Microsoft.CodeAnalysis;

namespace Headless.Generator.Primitives.Helpers;

internal static class MessagePackFormatterSourceFilesGeneratorEmitter
{
    internal static string CreateClassName(string className)
    {
        return $"{className}MessagePackFormatter";
    }

    /// <summary>Generates a MessagePack formatter that writes the primitive as its underlying value.</summary>
    /// <param name="context">The source production context.</param>
    /// <param name="data">The generator data containing type information.</param>
    internal static void AddMessagePackFormatter(this SourceProductionContext context, GeneratorData data)
    {
        var builder = new SourceCodeBuilder();

        builder.AppendSourceHeader("Primitives Generator");
        builder.AppendUsings(["System", data.PrimitiveTypeNamespace]);
        builder.AppendNamespace(data.ConvertersNamespace);

        // The primitive is named by its full name: a converter namespace may hold a type with the same simple name.
        var primitiveName = $"global::{data.FullyQualifiedName}";
        var formattedName = data.IsValueType ? primitiveName : primitiveName + '?';
        var underlyingName = data.PrimitiveTypeFriendlyName;
        var underlyingFormatter =
            $"{TypeNames.MessagePackFormatterResolverExtensions}.GetFormatterWithVerify<{underlyingName}>(options.Resolver)";

        builder.AppendSummary(
            $"MessagePack formatter for <see cref = \"{primitiveName}\"/>: writes the underlying value and reads it back through the validating constructor."
        );

        builder.AppendClass(
            isRecord: false,
            "public sealed",
            CreateClassName(data.ClassName),
            $"{TypeNames.IMessagePackFormatter}<{formattedName}>"
        );

        // Serialize
        builder
            .AppendInheritDoc()
            .AppendLine(
                $"public void Serialize(ref {TypeNames.MessagePackWriter} writer, {formattedName} value, {TypeNames.MessagePackSerializerOptions} options)"
            )
            .OpenBracket();

        if (!data.IsValueType)
        {
            builder
                .AppendLine("if (value is null)")
                .OpenBracket()
                .AppendLine("writer.WriteNil();")
                .AppendLine("return;")
                .CloseBracket()
                .NewLine();
        }

        builder
            .AppendLine($"{underlyingFormatter}.Serialize(ref writer, ({underlyingName})value, options);")
            .CloseBracket()
            .NewLine();

        // Deserialize
        builder
            .AppendInheritDoc()
            .AppendLine(
                $"public {formattedName} Deserialize(ref {TypeNames.MessagePackReader} reader, {TypeNames.MessagePackSerializerOptions} options)"
            )
            .OpenBracket();

        if (!data.IsValueType)
        {
            builder
                .AppendLine("if (reader.TryReadNil())")
                .OpenBracket()
                .AppendLine("return null;")
                .CloseBracket()
                .NewLine();
        }

        // Nil was consumed above, so a reference-type underlying value read here is never null.
        var suppressNull = data.PrimitiveTypeIsValueType ? "" : "!";

        builder
            .AppendLine(
                $"return new {primitiveName}({underlyingFormatter}.Deserialize(ref reader, options){suppressNull});"
            )
            .CloseBracket();

        builder.CloseBracket(); // end class

        context.AddSource(
            GeneratorData.HintName($"{data.ConvertersNamespace}.{CreateClassName(data.ClassName)}"),
            builder.ToString()
        );
    }
}
