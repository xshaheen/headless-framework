// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.OpenApi.Nswag.SchemaProcessors;
using Headless.Testing.Tests;
using Namotion.Reflection;
using NJsonSchema;
using NJsonSchema.Generation;

namespace Tests.SchemaProcessors;

public sealed class SchemaProcessorContextExtensionsTests : TestBase
{
    [Fact]
    public void should_add_only_non_nullable_object_properties_to_required()
    {
        var schema = _CreateObjectSchema();
        schema.Properties["required"] = new JsonSchemaProperty { Type = JsonObjectType.String };
        schema.Properties["nullable"] = new JsonSchemaProperty { Type = JsonObjectType.String | JsonObjectType.Null };

        var result = schema.NormalizeNullableAsRequired();

        result.Should().BeSameAs(schema);
        schema.RequiredProperties.Should().Equal("required");
    }

    [Fact]
    public void should_not_duplicate_an_existing_required_property()
    {
        var schema = _CreateObjectSchema();
        schema.Properties["name"] = new JsonSchemaProperty { Type = JsonObjectType.String };
        schema.RequiredProperties.Add("name");

        schema.NormalizeNullableAsRequired().NormalizeNullableAsRequired();

        schema.RequiredProperties.Should().Equal("name");
    }

    [Theory]
    [InlineData(JsonObjectType.String)]
    [InlineData(JsonObjectType.Array)]
    public void should_leave_non_object_schemas_unchanged(JsonObjectType schemaType)
    {
        var schema = new JsonSchema { Type = schemaType };

        schema.NormalizeNullableAsRequired();

        schema.RequiredProperties.Should().BeEmpty();
    }

    [Fact]
    public void processor_should_delegate_to_required_normalization()
    {
        var schema = _CreateObjectSchema();
        schema.Properties["name"] = new JsonSchemaProperty { Type = JsonObjectType.String };

        new NullabilityAsRequiredSchemaProcessor().Process(_CreateContext(schema));

        schema.RequiredProperties.Should().Equal("name");
    }

    private static JsonSchema _CreateObjectSchema()
    {
        return new JsonSchema { Type = JsonObjectType.Object };
    }

    private static SchemaProcessorContext _CreateContext(JsonSchema schema)
    {
        var settings = new SystemTextJsonSchemaGeneratorSettings();
        return new SchemaProcessorContext(
            typeof(object).ToContextualType(),
            schema,
            new JsonSchemaResolver(schema, settings),
            new JsonSchemaGenerator(settings),
            settings
        );
    }
}
