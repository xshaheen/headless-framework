// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.SchemaProcessors;
using Headless.Testing.Tests;
using Namotion.Reflection;
using NJsonSchema;
using NJsonSchema.Generation;

namespace Tests.SchemaProcessors;

public sealed class GenericNullabilitySchemaProcessorTests : TestBase
{
    private readonly GenericNullabilitySchemaProcessor _sut = new();

    #region Test Types

    // ReSharper disable UnusedAutoPropertyAccessor.Local
    private sealed class GenericWrapper<T>
    {
        public T Data { get; set; } = default!;
        public string Name { get; set; } = "";
    }

    private sealed class MultiGenericWrapper<T1, T2>
    {
        public T1 First { get; set; } = default!;
        public T2 Second { get; set; } = default!;
        public string Label { get; set; } = "";
    }

    private sealed class NonGenericType
    {
        public string Value { get; set; } = "";
    }

    // Properties with explicit nullability annotations — the contextual type
    // from these properties carries the generic argument nullability info.
    private sealed class TestContainer
    {
        public GenericWrapper<string?> NullableGeneric { get; set; } = null!;
        public GenericWrapper<string> NonNullableGeneric { get; set; } = null!;
        public MultiGenericWrapper<string?, int> MixedGeneric { get; set; } = null!;
    }
    // ReSharper restore UnusedAutoPropertyAccessor.Local

    #endregion

    [Fact]
    public void should_mark_property_as_nullable_when_generic_argument_is_nullable()
    {
        // given
        var contextualType = _GetContextualPropertyType(nameof(TestContainer.NullableGeneric));
        var schema = _CreateObjectSchema(("Data", JsonObjectType.String), ("Name", JsonObjectType.String));
        var context = _CreateContext(contextualType, schema);

        // when
        _sut.Process(context);

        // then
        schema.Properties["Data"].IsNullableRaw.Should().BeTrue();
    }

    [Fact]
    public void should_handle_camel_case_schema_property_names()
    {
        // given — NSwag generates camelCase property names by default (System.Text.Json)
        var contextualType = _GetContextualPropertyType(nameof(TestContainer.NullableGeneric));
        var schema = _CreateObjectSchema(("data", JsonObjectType.String), ("name", JsonObjectType.String));
        var context = _CreateContext(contextualType, schema);

        // when
        _sut.Process(context);

        // then
        schema.Properties["data"].IsNullableRaw.Should().BeTrue();
        schema.Properties["name"].IsNullableRaw.Should().NotBe(true);
    }

    [Fact]
    public void should_not_change_non_generic_properties()
    {
        // given
        var contextualType = _GetContextualPropertyType(nameof(TestContainer.NullableGeneric));
        var schema = _CreateObjectSchema(("Data", JsonObjectType.String), ("Name", JsonObjectType.String));
        var context = _CreateContext(contextualType, schema);

        // when
        _sut.Process(context);

        // then
        schema.Properties["Name"].IsNullableRaw.Should().NotBe(true);
    }

    [Fact]
    public void should_not_mark_property_when_generic_argument_is_not_nullable()
    {
        // given
        var contextualType = _GetContextualPropertyType(nameof(TestContainer.NonNullableGeneric));
        var schema = _CreateObjectSchema(("Data", JsonObjectType.String));
        var context = _CreateContext(contextualType, schema);

        // when
        _sut.Process(context);

        // then
        schema.Properties["Data"].IsNullableRaw.Should().NotBe(true);
    }

    [Fact]
    public void should_handle_multiple_generic_parameters_independently()
    {
        // given — MixedGeneric is MultiGenericWrapper<string?, int>
        var contextualType = _GetContextualPropertyType(nameof(TestContainer.MixedGeneric));
        var schema = _CreateObjectSchema(
            ("First", JsonObjectType.String),
            ("Second", JsonObjectType.Integer),
            ("Label", JsonObjectType.String)
        );
        var context = _CreateContext(contextualType, schema);

        // when
        _sut.Process(context);

        // then
        schema.Properties["First"].IsNullableRaw.Should().BeTrue();
        schema.Properties["Second"].IsNullableRaw.Should().NotBe(true);
        schema.Properties["Label"].IsNullableRaw.Should().NotBe(true);
    }

    [Fact]
    public void should_skip_non_generic_types()
    {
        // given
        var contextualType = typeof(NonGenericType).ToContextualType();
        var schema = _CreateObjectSchema(("Value", JsonObjectType.String));
        var context = _CreateContext(contextualType, schema);

        // when
        _sut.Process(context);

        // then
        schema.Properties["Value"].IsNullableRaw.Should().NotBe(true);
    }

    [Fact]
    public void should_skip_schemas_with_no_properties()
    {
        // given
        var contextualType = _GetContextualPropertyType(nameof(TestContainer.NullableGeneric));
        var schema = new JsonSchema { Type = JsonObjectType.Object };
        var context = _CreateContext(contextualType, schema);

        // when / then — should not throw
        _sut.Process(context);
    }

    [Fact]
    public void should_skip_non_object_schemas()
    {
        // given
        var contextualType = _GetContextualPropertyType(nameof(TestContainer.NullableGeneric));
        var schema = new JsonSchema { Type = JsonObjectType.String };
        var context = _CreateContext(contextualType, schema);

        // when / then — should not throw
        _sut.Process(context);
    }

    [Fact]
    public void should_prefer_nullable_when_processed_after_non_nullable()
    {
        // given — simulate the conflict: non-nullable processed first, then nullable
        var schema = _CreateObjectSchema(("Data", JsonObjectType.String));

        var nonNullableType = _GetContextualPropertyType(nameof(TestContainer.NonNullableGeneric));
        _sut.Process(_CreateContext(nonNullableType, schema));
        schema.Properties["Data"].IsNullableRaw.Should().NotBe(true);

        var nullableType = _GetContextualPropertyType(nameof(TestContainer.NullableGeneric));

        // when
        _sut.Process(_CreateContext(nullableType, schema));

        // then — nullable wins
        schema.Properties["Data"].IsNullableRaw.Should().BeTrue();
    }

    [Fact]
    public void should_preserve_nullable_when_non_nullable_processed_after()
    {
        // given — nullable processed first
        var schema = _CreateObjectSchema(("Data", JsonObjectType.String));

        var nullableType = _GetContextualPropertyType(nameof(TestContainer.NullableGeneric));
        _sut.Process(_CreateContext(nullableType, schema));
        schema.Properties["Data"].IsNullableRaw.Should().BeTrue();

        var nonNullableType = _GetContextualPropertyType(nameof(TestContainer.NonNullableGeneric));

        // when — non-nullable processed second on the same schema
        _sut.Process(_CreateContext(nonNullableType, schema));

        // then — nullable is preserved (never set back to false)
        schema.Properties["Data"].IsNullableRaw.Should().BeTrue();
    }

    #region Helpers

    private static ContextualType _GetContextualPropertyType(string propertyName)
    {
        return typeof(TestContainer).GetProperty(propertyName)!.ToContextualProperty().PropertyType;
    }

    private static JsonSchema _CreateObjectSchema(params (string Name, JsonObjectType Type)[] properties)
    {
        var schema = new JsonSchema { Type = JsonObjectType.Object };

        foreach (var (name, type) in properties)
        {
            schema.Properties[name] = new JsonSchemaProperty { Type = type };
        }

        return schema;
    }

    private static SchemaProcessorContext _CreateContext(ContextualType contextualType, JsonSchema schema)
    {
        var settings = new SystemTextJsonSchemaGeneratorSettings();
        var resolver = new JsonSchemaResolver(schema, settings);
        var generator = new JsonSchemaGenerator(settings);

        return new SchemaProcessorContext(contextualType, schema, resolver, generator, settings);
    }

    #endregion
}
