// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.OpenApi;
using Headless.Testing.Tests;
using Microsoft.OpenApi;

namespace Tests;

public sealed class OpenApiSchemaNullabilityTests : TestBase
{
    private static OpenApiSchema _Null() => new() { Type = JsonSchemaType.Null };

    private static OpenApiSchema _String() => new() { Type = JsonSchemaType.String };

    private static OpenApiSchema _Integer() => new() { Type = JsonSchemaType.Integer };

    [Fact]
    public void should_not_treat_a_reference_as_nullable_or_change_it()
    {
        var reference = new OpenApiSchemaReference("Address", new OpenApiDocument());

        OpenApiSchemaNullability.IsNullable(reference).Should().BeFalse();
        OpenApiSchemaNullability.RemoveNullability(reference).Should().BeSameAs(reference);
    }

    [Fact]
    public void should_read_nullability_from_the_type()
    {
        OpenApiSchemaNullability
            .IsNullable(new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null })
            .Should()
            .BeTrue();
        OpenApiSchemaNullability.IsNullable(_String()).Should().BeFalse();
    }

    [Fact]
    public void should_read_nullability_from_a_null_one_of_alternative()
    {
        OpenApiSchemaNullability.IsNullable(new OpenApiSchema { OneOf = [_Null(), _String()] }).Should().BeTrue();
        OpenApiSchemaNullability.IsNullable(new OpenApiSchema { OneOf = [_String(), _Integer()] }).Should().BeFalse();
    }

    [Fact]
    public void should_read_nullability_from_a_null_any_of_alternative()
    {
        OpenApiSchemaNullability.IsNullable(new OpenApiSchema { AnyOf = [_Null(), _String()] }).Should().BeTrue();
        OpenApiSchemaNullability.IsNullable(new OpenApiSchema { AnyOf = [_String(), _Integer()] }).Should().BeFalse();
    }

    [Fact]
    public void should_read_nullability_from_the_nullable_property_marker()
    {
        var marked = new OpenApiSchema
        {
            Metadata = new Dictionary<string, object> { [OpenApiSchemaNullability.NullablePropertyKey] = true },
        };

        OpenApiSchemaNullability.IsNullable(marked).Should().BeTrue();
    }

    [Fact]
    public void should_remove_null_from_the_type_and_drop_the_nullable_property_marker()
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.String | JsonSchemaType.Null,
            Metadata = new Dictionary<string, object> { [OpenApiSchemaNullability.NullablePropertyKey] = true },
        };

        var result = OpenApiSchemaNullability.RemoveNullability(schema);

        result.Should().BeSameAs(schema);
        schema.Type.Should().Be(JsonSchemaType.String);
        OpenApiSchemaNullability.IsMarkedNullableProperty(schema).Should().BeFalse();
    }

    [Fact]
    public void should_collapse_a_nullable_reference_to_the_reference()
    {
        var reference = new OpenApiSchemaReference("Address", new OpenApiDocument());

        var result = OpenApiSchemaNullability.RemoveNullability(new OpenApiSchema { OneOf = [_Null(), reference] });

        result.Should().BeSameAs(reference);
    }

    [Fact]
    public void should_not_collapse_a_schema_that_still_describes_the_value_itself()
    {
        var typed = new OpenApiSchema { Type = JsonSchemaType.String, OneOf = [_Null(), _String()] };
        var withProperties = new OpenApiSchema
        {
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal) { ["name"] = _String() },
            OneOf = [_Null(), _String()],
        };
        var withAnyOf = new OpenApiSchema { OneOf = [_Null(), _String()], AnyOf = [_Integer()] };

        OpenApiSchemaNullability.RemoveNullability(typed).Should().BeSameAs(typed);
        typed.OneOf.Should().ContainSingle().Which.Type.Should().Be(JsonSchemaType.String);
        OpenApiSchemaNullability.RemoveNullability(withProperties).Should().BeSameAs(withProperties);
        OpenApiSchemaNullability.RemoveNullability(withAnyOf).Should().BeSameAs(withAnyOf);
    }

    [Fact]
    public void should_keep_every_non_null_alternative_in_order()
    {
        var first = _String();
        var second = _Integer();
        var schema = new OpenApiSchema { OneOf = [_Null(), first, second], AnyOf = [first, _Null()] };

        OpenApiSchemaNullability.RemoveNullability(schema);

        schema.OneOf.Should().Equal(first, second);
        schema.AnyOf.Should().Equal(first);
    }

    [Fact]
    public void should_drop_alternatives_that_only_admitted_null()
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            OneOf = [_Null()],
            AnyOf = [_Null()],
        };

        OpenApiSchemaNullability.RemoveNullability(schema);

        schema.OneOf.Should().BeNull();
        schema.AnyOf.Should().BeNull();
    }
}
