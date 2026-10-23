// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Headless.Generator.Primitives;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Describes every Headless primitive (a type implementing <c>IPrimitive&lt;T&gt;</c>, such as <c>MoneyAmount</c>,
/// <c>Month</c>, <c>AccountId</c>, and <c>UserId</c>) as the JSON type of its underlying value, with the bounds of a
/// string primitive's <see cref="StringLengthAttribute"/> as <c>minLength</c> and <c>maxLength</c>.
/// </summary>
/// <remarks>
/// A primitive serializes through its own converter, which the native generator cannot see through, so without this
/// it publishes an empty schema that accepts any value. Generated primitives need no per-assembly registration: the
/// interface identifies them. A collection of primitives (<c>IReadOnlyList&lt;UserId&gt;</c>) gets the same description
/// as its <c>items</c>: the generator leaves a converter-backed element out of the array altogether, so the array would
/// otherwise publish no <c>items</c> and a generated client could not type its elements.
/// </remarks>
internal sealed class PrimitiveSchemaTransformer : IOpenApiSchemaTransformer
{
    // Every schema node asks, so the interface scan runs once per type.
    private static readonly ConcurrentDictionary<Type, Type?> _ValueTypes = new();

    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        var type = context.JsonTypeInfo.Type;
        var nullableUnderlying = Nullable.GetUnderlyingType(type);

        if (!_TryGetValueType(nullableUnderlying ?? type, out var valueType))
        {
            _DescribeItems(schema, context.JsonTypeInfo.ElementType);

            return Task.CompletedTask;
        }

        var (jsonType, format) = _Describe(valueType);
        var nullable =
            nullableUnderlying is not null
            || (!type.IsValueType && context.JsonPropertyInfo is { IsGetNullable: true });

        schema.Type = nullable ? jsonType | JsonSchemaType.Null : jsonType;
        schema.Format = format;
        schema.Properties = null;
        schema.Required = null;
        schema.Items = null;
        schema.OneOf = null;
        schema.AnyOf = null;
        schema.AllOf = null;
        schema.Pattern = null;
        _ApplyStringLength(schema, nullableUnderlying ?? type, valueType);

        return Task.CompletedTask;
    }

    private static void _DescribeItems(OpenApiSchema schema, Type? elementType)
    {
        if (schema.Items is not null || elementType is null)
        {
            return;
        }

        var elementUnderlying = Nullable.GetUnderlyingType(elementType);

        if (!_TryGetValueType(elementUnderlying ?? elementType, out var valueType))
        {
            return;
        }

        var (jsonType, format) = _Describe(valueType);
        var items = new OpenApiSchema
        {
            Type = elementUnderlying is null ? jsonType : jsonType | JsonSchemaType.Null,
            Format = format,
        };
        _ApplyStringLength(items, elementUnderlying ?? elementType, valueType);
        schema.Items = items;
    }

    // The generated constructor enforces the attribute's bounds unless it opts out with validate: false, in which case
    // the bounds describe nothing a request is held to.
    private static void _ApplyStringLength(OpenApiSchema schema, Type primitiveType, Type valueType)
    {
        if (
            valueType != typeof(string)
            || Attribute.GetCustomAttribute(primitiveType, typeof(StringLengthAttribute))
                is not StringLengthAttribute { Validate: true } length
        )
        {
            return;
        }

        if (length.MinimumLength > 0)
        {
            schema.MinLength = length.MinimumLength;
        }

        if (length.MaximumLength > 0)
        {
            schema.MaxLength = length.MaximumLength;
        }
    }

    internal static bool IsPrimitive(Type type)
    {
        return _TryGetValueType(Nullable.GetUnderlyingType(type) ?? type, out _);
    }

    private static bool _TryGetValueType(Type type, [NotNullWhen(true)] out Type? valueType)
    {
        valueType = _ValueTypes.GetOrAdd(type, _FindValueType);

        return valueType is not null;
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070:Unrecognized reflection pattern",
        Justification = "A trimmed interface list only hides a primitive, which then keeps the native schema."
    )]
    private static Type? _FindValueType(Type type)
    {
        if (!typeof(IPrimitive).IsAssignableFrom(type))
        {
            return null;
        }

        foreach (var contract in type.GetInterfaces())
        {
            if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IPrimitive<>))
            {
                return contract.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static (JsonSchemaType Type, string? Format) _Describe(Type valueType)
    {
        return Type.GetTypeCode(valueType) switch
        {
            TypeCode.Boolean => (JsonSchemaType.Boolean, null),
            TypeCode.SByte => (JsonSchemaType.Integer, "int8"),
            TypeCode.Byte => (JsonSchemaType.Integer, "uint8"),
            TypeCode.Int16 => (JsonSchemaType.Integer, "int16"),
            TypeCode.UInt16 => (JsonSchemaType.Integer, "uint16"),
            TypeCode.Int32 => (JsonSchemaType.Integer, "int32"),
            TypeCode.UInt32 => (JsonSchemaType.Integer, "uint32"),
            TypeCode.Int64 => (JsonSchemaType.Integer, "int64"),
            TypeCode.UInt64 => (JsonSchemaType.Integer, "uint64"),
            TypeCode.Single => (JsonSchemaType.Number, "float"),
            TypeCode.Double => (JsonSchemaType.Number, "double"),
            TypeCode.Decimal => (JsonSchemaType.Number, "decimal"),
            TypeCode.DateTime => (JsonSchemaType.String, "date-time"),
            _ when valueType == typeof(DateTimeOffset) => (JsonSchemaType.String, "date-time"),
            _ when valueType == typeof(DateOnly) => (JsonSchemaType.String, "date"),
            _ when valueType == typeof(TimeOnly) => (JsonSchemaType.String, "time"),
            _ when valueType == typeof(TimeSpan) => (JsonSchemaType.String, "time-span"),
            _ when valueType == typeof(Guid) => (JsonSchemaType.String, "uuid"),
            _ => (JsonSchemaType.String, null),
        };
    }
}
