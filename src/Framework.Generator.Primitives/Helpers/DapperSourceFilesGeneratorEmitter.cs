// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Generator.Primitives.Models;
using Framework.Generator.Primitives.Shared;
using Microsoft.CodeAnalysis;

namespace Framework.Generator.Primitives.Helpers;

internal static class DapperSourceFilesGeneratorEmitter
{
    private const string _HelperClassName = "AddPrimitivesDapperTypeHandlersHelper";
    private const string _HelperMethodName = "AddPrimitivesDapperTypeHandlers";

    private static string _CreateHelperNamespaceName(string assemblyName)
    {
        var ns = string.Join(".", assemblyName.Split('.').Select(s => char.IsDigit(s[0]) ? '_' + s : s));

        return $"{ns}.Converters.Extensions";
    }

    private static string _CreateHandlerClassName(string className)
    {
        return $"{className}DapperTypeHandler";
    }

    private static string _CreateConverterNamespaceName(string containingNamespace)
    {
        return $"{containingNamespace}.Converters";
    }

    private static string _CreateHandlerFileName(string className)
    {
        return $"{_CreateHandlerClassName(className)}.g.cs";
    }

    /// <summary>Processes the Dapper converter for the specified generator data and source production context.</summary>
    internal static void AddDapperTypeHandlerConverter(this SourceProductionContext context, GeneratorData data)
    {
        var builder = new SourceCodeBuilder();

        builder.AppendSourceHeader("Primitives Generator");

        builder.AppendUsings(
            [
                "System",
                "System.Globalization",
                data.Namespace,
                data.PrimitiveTypeSymbol.ContainingNamespace.ToDisplayString(),
                AbstractionConstants.Namespace,
            ]
        );

        builder.AppendNamespace(_CreateConverterNamespaceName(data.Namespace));
        builder.AppendSummary($"Dapper TypeHandler for <see cref = \"{data.ClassName}\"/>");

        builder.AppendClass(
            isRecord: false,
            "public sealed",
            _CreateHandlerClassName(data.ClassName),
            $"{TypeNames.DapperTypeHandler}<{data.ClassName}>"
        );

        var parameterTypeName = data.TypeSymbol.IsValueType ? data.ClassName : data.ClassName + '?';

        // override SetValue method
        builder
            .AppendLine(
                $"public override void SetValue({TypeNames.IDbDataParameter} parameter, {parameterTypeName} value)"
            )
            .OpenBracket()
            .AppendLine(
                data.TypeSymbol.IsValueType
                    ? "parameter.Value = value.GetUnderlyingPrimitiveType();"
                    : "parameter.Value = value?.GetUnderlyingPrimitiveType();"
            )
            .CloseBracket()
            .NewLine();

        // override Parse method
        builder
            .AppendLine($"public override {parameterTypeName} Parse(object value)")
            .OpenBracket()
            .AppendLine("return value switch")
            .OpenBracket(); // start switch expression

        // TODO: Enhance support for SByte, byte, UInt16, UInt32, UInt64

        Span<string> switchCases = data.UnderlyingType switch
        {
            PrimitiveUnderlyingType.String => [$"string stringValue => new {data.ClassName}(stringValue),"],
            PrimitiveUnderlyingType.Guid =>
            [
                $"{TypeNames.Guid} guidValue => new {data.ClassName}(guidValue),",
                $"string stringValue when !string.IsNullOrEmpty(stringValue) && {TypeNames.Guid}.TryParse(stringValue, {StaticValues.InvariantCulture}, out var result) => new {data.ClassName}(result),",
            ],
            PrimitiveUnderlyingType.Boolean =>
            [
                $"bool boolValue => new {data.ClassName}(boolValue)",
                $"string stringValue when !string.IsNullOrEmpty(stringValue) && bool.TryParse(stringValue, out var result) => new {data.ClassName}(result)",
            ],
            PrimitiveUnderlyingType.Int16 =>
            [
                $"byte byteValue => new {data.ClassName}(byteValue)",
                $"short shortValue => new {data.ClassName}(shortValue)",
                $"int intValue and < short.MaxValue and > short.MinValue => new {data.ClassName}((short)intValue)",
                $"long longValue and < short.MaxValue and > short.MinValue => new {data.ClassName}((short)longValue)",
                $"decimal decimalValue and < short.MaxValue and > short.MinValue => new {data.ClassName}((short)decimalValue)",
                $"string stringValue when !string.IsNullOrEmpty(stringValue) && short.TryParse(stringValue, {StaticValues.InvariantCulture}, out var result) => new {data.ClassName}(result)",
            ],
            PrimitiveUnderlyingType.Int32 =>
            [
                $"byte byteValue => new {data.ClassName}(byteValue)",
                $"int intValue => new {data.ClassName}(intValue)",
                $"short shortValue => new {data.ClassName}(shortValue)",
                $"long longValue and < int.MaxValue and > int.MinValue => new {data.ClassName}((int)longValue)",
                $"decimal decimalValue and < int.MaxValue and > int.MinValue => new {data.ClassName}((int)decimalValue)",
                $"string stringValue when !string.IsNullOrEmpty(stringValue) && int.TryParse(stringValue, {StaticValues.InvariantCulture}, out var result) => new {data.ClassName}(result)",
            ],
            PrimitiveUnderlyingType.Int64 =>
            [
                $"byte byteValue => new {data.ClassName}(byteValue)",
                $"long longValue => new {data.ClassName}(longValue)",
                $"int intValue => new {data.ClassName}(intValue)",
                $"short shortValue => new {data.ClassName}(shortValue)",
                $"decimal decimalValue and < long.MaxValue and > long.MinValue => new {data.ClassName}((long)decimalValue)",
                $"string stringValue when !string.IsNullOrEmpty(stringValue) && long.TryParse(stringValue, c out var result) => new {data.ClassName}(result)",
            ],
            PrimitiveUnderlyingType.Single =>
            [
                $"float floatValue => new {data.ClassName}(floatValue)",
                $"double doubleValue and > float.MinValue and < float.MaxValue => new {data.ClassName}((float)doubleValue)",
                $"byte byteValue => new {data.ClassName}(byteValue)",
                $"short shortValue => new {data.ClassName}(shortValue)",
                $"long longValue => new {data.ClassName}(longValue)",
                $"int intValue => new {data.ClassName}(intValue)",
                $"string stringValue when !string.IsNullOrEmpty(stringValue) && float.TryParse(stringValue, {StaticValues.InvariantCulture}, out var result) => new {data.ClassName}(result)",
            ],
            PrimitiveUnderlyingType.Double =>
            [
                $"double doubleValue => new {data.ClassName}(doubleValue)",
                $"float floatValue => new {data.ClassName}(floatValue)",
                $"byte byteValue => new {data.ClassName}(byteValue)",
                $"short shortValue => new {data.ClassName}(shortValue)",
                $"long longValue => new {data.ClassName}(longValue)",
                $"int intValue => new {data.ClassName}(intValue)",
                $"string stringValue when !string.IsNullOrEmpty(stringValue) && double.TryParse(stringValue, {StaticValues.InvariantCulture}, out var result) => new {data.ClassName}(result)",
            ],
            PrimitiveUnderlyingType.Char =>
            [
                $"char charValue => new {data.ClassName}(charValue)",
                $"string stringValue when !string.IsNullOrEmpty(stringValue) && char.TryParse(stringValue, out var charValue) => new {data.ClassName}(charValue)",
            ],
            PrimitiveUnderlyingType.DateTime =>
            [
                $"{TypeNames.DateTime} dateOnly => new {data.ClassName}(dateOnly)",
                $"{TypeNames.DateTimeOffset} dateTimeOffset => new {data.ClassName}(dateTimeOffset.DateTime)",
                $"""string stringValue when !string.IsNullOrEmpty(stringValue) && {TypeNames.DateTime}.TryParseExact(stringValue, "o", {StaticValues.InvariantCulture}, {StaticValues.AssumeLocal}, out var result) => new {data.ClassName}(result)""",
            ],
            PrimitiveUnderlyingType.DateTimeOffset =>
            [
                $"{TypeNames.DateTimeOffset} dateTimeOffset => new {data.ClassName}(dateTimeOffset)",
                $"""string stringValue when !string.IsNullOrEmpty(stringValue) && {TypeNames.DateTimeOffset}.TryParseExact(stringValue, "o", {StaticValues.InvariantCulture}, {StaticValues.AssumeLocal}, out var result) => new {data.ClassName}(result)""",
            ],
            PrimitiveUnderlyingType.DateOnly =>
            [
                $"{TypeNames.DateOnly} dateOnly => new {data.ClassName}(dateOnly)",
                $"{TypeNames.DateTime} dateTime => new {data.ClassName}({TypeNames.DateOnly}.FromDateTime(dateTime))",
                $"{TypeNames.DateTimeOffset} dateTimeOffset => new {data.ClassName}({TypeNames.DateOnly}.FromDateTime(dateTimeOffset.DateTime))",
                $"string stringValue when !string.IsNullOrEmpty(stringValue) && {TypeNames.DateOnly}.TryParse(stringValue, {StaticValues.InvariantCulture}, out var result) => new {data.ClassName}(result)",
            ],
            PrimitiveUnderlyingType.TimeOnly =>
            [
                $"{TypeNames.TimeOnly} timeOnly => new {data.ClassName}(timeOnly)",
                $"string stringValue when !string.IsNullOrEmpty(stringValue) && {TypeNames.TimeOnly}.TryParse(stringValue, {StaticValues.InvariantCulture}, out var result) => new {data.ClassName}(result)",
            ],
            PrimitiveUnderlyingType.TimeSpan =>
            [
                $"{TypeNames.TimeSpan} timeSpan => new {data.ClassName}(timeSpan)",
                $"string stringValue when !string.IsNullOrEmpty(stringValue) && {TypeNames.TimeSpan}.TryParse(stringValue, {StaticValues.InvariantCulture}, out var result) => new {data.ClassName}(result)",
            ],
            _ => [$"{data.PrimitiveTypeFriendlyName} primitiveValue => new {data.ClassName}(primitiveValue),"],
        };

        foreach (var line in switchCases)
        {
            builder.AppendLine(line);
        }

        builder.AppendLine(
            $$"""_ => throw new global::System.InvalidCastException($"Unable to cast object of type {value.GetType()} to {{data.ClassName}}"),"""
        );

        builder.CloseExpressionBracket(); // end switch expression
        builder.CloseBracket(); // end Parse method
        builder.CloseBracket(); // end class

        context.AddSource(_CreateHandlerFileName(data.ClassName), builder.ToString());
    }

    /// <summary>Generates the dapper type handlers add all helpers.</summary>
    internal static void AddDapperTypeHandlersHelper(
        this SourceProductionContext context,
        string assemblyName,
        List<INamedTypeSymbol> types,
        bool addAssemblyAttribute
    )
    {
        var builder = new SourceCodeBuilder();

        builder.AppendSourceHeader("Primitives Generator");

        builder.AppendUsings(
            [
                .. types.ConvertAll(x => x.ContainingNamespace.ToDisplayString()),
                .. types.ConvertAll(x => _CreateConverterNamespaceName(x.ContainingNamespace.ToDisplayString())),
            ]
        );

        if (addAssemblyAttribute)
        {
            builder.AppendLine($"[assembly: {AbstractionConstants.PrimitiveAssemblyAttributeFullName}]");
        }

        builder.AppendNamespace(_CreateHelperNamespaceName(assemblyName));

        builder.AppendSummary(
            $"Helper class providing methods to configure Dapper Type Handlers for Primitive types of {assemblyName}"
        );

        builder.AppendClass(isRecord: false, "public static", _HelperClassName);

        builder.AppendSummary(
            "Adds Dapper Type Handlers for specific custom types to ensure proper mapping to Dapper ORM."
        );

        builder.AppendLine($"public static void {_HelperMethodName}()");
        builder.OpenBracket(); // start method

        foreach (var type in types)
        {
            builder.Append($"global::Dapper.SqlMapper.AddTypeMap(new {_CreateHandlerClassName(type.Name)}());");
        }

        builder.CloseBracket(); // end method
        builder.CloseBracket(); // end class
        context.AddSource($"{_HelperClassName}.g.cs", builder.ToString());
    }
}
