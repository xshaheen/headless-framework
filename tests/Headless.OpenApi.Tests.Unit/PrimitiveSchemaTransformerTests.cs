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
    public async Task should_publish_the_string_length_bounds_of_a_string_primitive()
    {
        var schema = _ObjectSchema();

        await _TransformAsync(schema, typeof(BoundedCode));

        schema.Type.Should().Be(JsonSchemaType.String);
        schema.MinLength.Should().Be(2);
        schema.MaxLength.Should().Be(10);
    }

    [Theory]
    [InlineData(typeof(UnvalidatedCode))]
    [InlineData(typeof(TestPrimitive<string>))]
    public async Task should_publish_no_length_bounds_when_the_primitive_enforces_none(Type primitiveType)
    {
        var schema = _ObjectSchema();

        await _TransformAsync(schema, primitiveType);

        schema.MinLength.Should().BeNull();
        schema.MaxLength.Should().BeNull();
    }

    [Fact]
    public async Task should_describe_a_primitive_wrapping_another_primitive_as_the_innermost_value()
    {
        // The wrapped primitive serializes as its own value, so describing it as an object or a bare string is wrong.
        var schema = _ObjectSchema();

        await _TransformAsync(schema, typeof(TestPrimitive<ComparablePrimitive<decimal>>));

        schema.Type.Should().Be(JsonSchemaType.Number);
        schema.Format.Should().Be("decimal");
        schema.Properties.Should().BeNull();
    }

    [Fact]
    public async Task should_describe_items_of_a_collection_of_nested_primitives()
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.Array };

        await _TransformAsync(schema, typeof(IReadOnlyList<TestPrimitive<ComparablePrimitive<int>>>));

        schema.Items.Should().NotBeNull();
        schema.Items!.Type.Should().Be(JsonSchemaType.Integer);
        schema.Items.Format.Should().Be("int32");
    }

    [Fact]
    public async Task should_publish_the_tightest_string_length_bounds_across_a_nested_primitive_chain()
    {
        // NestedCode allows 4 to 20 characters and wraps ComparableCode, which allows 2 to 10: a value must pass both.
        var schema = _ObjectSchema();

        await _TransformAsync(schema, typeof(NestedCode));

        schema.Type.Should().Be(JsonSchemaType.String);
        schema.MinLength.Should().Be(4);
        schema.MaxLength.Should().Be(10);
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

/// <summary>A string primitive whose generated constructor enforces 2 to 10 characters.</summary>
[StringLength(2, 10)]
public sealed class BoundedCode : IPrimitive<string>
{
    public Type GetUnderlyingPrimitiveType() => typeof(string);

    public string GetUnderlyingPrimitiveValue() => "";

    public static PrimitiveValidationResult Validate(string value) => PrimitiveValidationResult.Ok;
}

/// <summary>A string primitive that declares bounds but opts out of enforcing them.</summary>
[StringLength(2, 10, validate: false)]
public sealed class UnvalidatedCode : IPrimitive<string>
{
    public Type GetUnderlyingPrimitiveType() => typeof(string);

    public string GetUnderlyingPrimitiveValue() => "";

    public static PrimitiveValidationResult Validate(string value) => PrimitiveValidationResult.Ok;
}

#pragma warning disable CA1036 // Fixtures that only carry interfaces; operators would add code no test exercises.
/// <summary>A primitive that can itself be wrapped, because <c>IPrimitive&lt;T&gt;</c> requires a comparable value.</summary>
public readonly record struct ComparablePrimitive<T> : IPrimitive<T>, IComparable, IComparable<ComparablePrimitive<T>>
    where T : IEquatable<T>, IComparable, IComparable<T>
{
    public Type GetUnderlyingPrimitiveType() => typeof(T);

    public T GetUnderlyingPrimitiveValue() => default!;

    public int CompareTo(object? obj) => 0;

    public int CompareTo(ComparablePrimitive<T> other) => 0;

#pragma warning disable CA1000 // False positive: IPrimitive<T> declares Validate as static abstract, so the implementation must be static.
    public static PrimitiveValidationResult Validate(T value) => PrimitiveValidationResult.Ok;
#pragma warning restore CA1000
}

/// <summary>A string primitive that allows 2 to 10 characters and can be wrapped by another primitive.</summary>
[StringLength(2, 10)]
public sealed class ComparableCode
    : IPrimitive<string>,
        IEquatable<ComparableCode>,
        IComparable,
        IComparable<ComparableCode>
{
    public Type GetUnderlyingPrimitiveType() => typeof(string);

    public string GetUnderlyingPrimitiveValue() => "";

    public bool Equals(ComparableCode? other) => other is not null;

    public override bool Equals(object? obj) => obj is ComparableCode;

    public override int GetHashCode() => 0;

    public int CompareTo(object? obj) => 0;

    public int CompareTo(ComparableCode? other) => 0;

    public static PrimitiveValidationResult Validate(string value) => PrimitiveValidationResult.Ok;
}

#pragma warning restore CA1036

/// <summary>A primitive over <see cref="ComparableCode"/> that allows 4 to 20 characters of its own.</summary>
[StringLength(4, 20)]
public sealed class NestedCode : IPrimitive<ComparableCode>
{
    public Type GetUnderlyingPrimitiveType() => typeof(string);

    public ComparableCode GetUnderlyingPrimitiveValue() => new();

    public static PrimitiveValidationResult Validate(ComparableCode value) => PrimitiveValidationResult.Ok;
}
