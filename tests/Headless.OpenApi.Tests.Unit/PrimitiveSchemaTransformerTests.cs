// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Headless.Generator.Primitives;
using Headless.OpenApi;
using Headless.Testing.Tests;
using Microsoft.OpenApi;

namespace Tests;

public sealed class PrimitiveSchemaTransformerTests : TestBase
{
    private static readonly JsonSerializerOptions _JsonOptions = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    [Theory]
    [InlineData(typeof(bool), JsonSchemaType.Boolean, null)]
    [InlineData(typeof(sbyte), JsonSchemaType.Integer, "int8")]
    [InlineData(typeof(byte), JsonSchemaType.Integer, "uint8")]
    [InlineData(typeof(short), JsonSchemaType.Integer, "int16")]
    [InlineData(typeof(ushort), JsonSchemaType.Integer, "uint16")]
    [InlineData(typeof(int), JsonSchemaType.Integer, "int32")]
    [InlineData(typeof(uint), JsonSchemaType.Integer, "uint32")]
    [InlineData(typeof(long), JsonSchemaType.Integer, "int64")]
    [InlineData(typeof(ulong), JsonSchemaType.Integer, "uint64")]
    [InlineData(typeof(float), JsonSchemaType.Number, "float")]
    [InlineData(typeof(double), JsonSchemaType.Number, "double")]
    [InlineData(typeof(decimal), JsonSchemaType.Number, "decimal")]
    [InlineData(typeof(DateTime), JsonSchemaType.String, "date-time")]
    [InlineData(typeof(DateTimeOffset), JsonSchemaType.String, "date-time")]
    [InlineData(typeof(DateOnly), JsonSchemaType.String, "date")]
    [InlineData(typeof(TimeOnly), JsonSchemaType.String, "time")]
    [InlineData(typeof(TimeSpan), JsonSchemaType.String, "time-span")]
    [InlineData(typeof(Guid), JsonSchemaType.String, "uuid")]
    [InlineData(typeof(string), JsonSchemaType.String, null)]
    [InlineData(typeof(char), JsonSchemaType.String, null)]
    public async Task should_describe_a_primitive_as_the_json_type_and_format_of_its_value(
        Type valueType,
        JsonSchemaType expectedType,
        string? expectedFormat
    )
    {
        var primitiveType = typeof(TestPrimitive<>).MakeGenericType(valueType);
        var schema = _ObjectSchema();

        await _TransformAsync(schema, primitiveType);

        schema.Type.Should().Be(expectedType);
        schema.Format.Should().Be(expectedFormat);
        schema.Properties.Should().BeNull();
        schema.Required.Should().BeNull();
        schema.Pattern.Should().BeNull();
    }

    [Theory]
    [InlineData(typeof(ComparableFirstPrimitive))]
    [InlineData(typeof(ComparableLastPrimitive))]
    public async Task should_take_the_value_type_from_the_primitive_contract_not_another_generic_interface(
        Type primitiveType
    )
    {
        // Each type also implements IComparable<Guid>; picking that argument would describe a uuid string.
        var schema = _ObjectSchema();

        await _TransformAsync(schema, primitiveType);

        schema.Type.Should().Be(JsonSchemaType.Integer);
        schema.Format.Should().Be("int32");
    }

    [Fact]
    public async Task should_admit_null_for_a_nullable_value_type_primitive()
    {
        var schema = _ObjectSchema();

        await _TransformAsync(schema, typeof(TestPrimitive<long>?));

        schema.Type.Should().Be(JsonSchemaType.Integer | JsonSchemaType.Null);
        schema.Format.Should().Be("int64");
    }

    [Fact]
    public async Task should_leave_a_type_that_is_not_a_primitive_untouched()
    {
        var schema = _ObjectSchema();

        await _TransformAsync(schema, typeof(Address));

        schema.Type.Should().Be(JsonSchemaType.Object);
        schema.Format.Should().BeNull();
        schema.Properties.Should().ContainKey("value");
        schema.Pattern.Should().Be("^x$");
    }

    private static OpenApiSchema _ObjectSchema() =>
        new()
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["value"] = new OpenApiSchema(),
            },
            Required = new HashSet<string>(StringComparer.Ordinal) { "value" },
            Pattern = "^x$",
        };

    private static Task _TransformAsync(OpenApiSchema schema, Type type) =>
        new PrimitiveSchemaTransformer().TransformAsync(
            schema,
            TransformerContexts.Schema(_JsonOptions.GetTypeInfo(type)),
            AbortToken
        );
}

/// <summary>A hand-written primitive over any value type, standing in for the generated ones.</summary>
public readonly record struct TestPrimitive<T> : IPrimitive<T>
    where T : IEquatable<T>, IComparable, IComparable<T>
{
    public Type GetUnderlyingPrimitiveType() => typeof(T);

    public T GetUnderlyingPrimitiveValue() => default!;

#pragma warning disable CA1000 // False positive: IPrimitive<T> declares Validate as static abstract, so the implementation must be static.
    public static PrimitiveValidationResult Validate(T value) => PrimitiveValidationResult.Ok;
#pragma warning restore CA1000
}

/// <summary>A primitive over <see cref="int"/> that declares another generic interface before its primitive contract.</summary>
#pragma warning disable CA1036 // A fixture that only carries interfaces; ordering operators would add code no test exercises.
public readonly record struct ComparableFirstPrimitive : IComparable<Guid>, IPrimitive<int>
{
    public int CompareTo(Guid other) => 0;

    public Type GetUnderlyingPrimitiveType() => typeof(int);

    public int GetUnderlyingPrimitiveValue() => 0;

    public static PrimitiveValidationResult Validate(int value) => PrimitiveValidationResult.Ok;
}

/// <summary>A primitive over <see cref="int"/> that declares another generic interface after its primitive contract.</summary>
#pragma warning disable CA1036 // A fixture that only carries interfaces; ordering operators would add code no test exercises.
public readonly record struct ComparableLastPrimitive : IPrimitive<int>, IComparable<Guid>
{
    public int CompareTo(Guid other) => 0;

    public Type GetUnderlyingPrimitiveType() => typeof(int);

    public int GetUnderlyingPrimitiveValue() => 0;

    public static PrimitiveValidationResult Validate(int value) => PrimitiveValidationResult.Ok;
}
#pragma warning restore CA1036
