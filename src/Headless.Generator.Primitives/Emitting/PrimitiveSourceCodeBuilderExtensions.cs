// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.SourceGenerators;

namespace Headless.Generator.Primitives.Shared;

/// <summary>Attribute lines the primitive emitters write onto generated types and members.</summary>
internal static class PrimitiveSourceCodeBuilderExtensions
{
    public static SourceCodeBuilder AppendDebuggerBrowsableNeverAttribute(this SourceCodeBuilder builder)
    {
        return builder.AppendLine(
            "[global::System.Diagnostics.DebuggerBrowsable(global::System.Diagnostics.DebuggerBrowsableState.Never)]"
        );
    }

    public static SourceCodeBuilder AppendMethodAggressiveInliningAttribute(this SourceCodeBuilder builder)
    {
        return builder.AppendLine(
            "[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]"
        );
    }

    public static SourceCodeBuilder AppendJsonConverterAttribute(this SourceCodeBuilder builder, string typeName)
    {
        return builder.AppendLine($"[global::System.Text.Json.Serialization.JsonConverter(typeof({typeName}))]");
    }

    public static SourceCodeBuilder AppendTypeConverterAttribute(this SourceCodeBuilder builder, string typeName)
    {
        return builder.AppendLine($"[global::System.ComponentModel.TypeConverter(typeof({typeName}))]");
    }

    public static SourceCodeBuilder AppendDebuggerDisplay(this SourceCodeBuilder builder, string value)
    {
        return builder.AppendLine($"[global::System.Diagnostics.DebuggerDisplay(\"{value}\")]");
    }
}
