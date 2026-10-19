// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.OpenApi;
using Headless.Testing.Tests;
using Microsoft.OpenApi;

namespace Tests;

public sealed class OpenApiSchemaComponentsTests : TestBase
{
    private static OpenApiSchema _Named(string id) =>
        new()
        {
            Type = JsonSchemaType.Object,
            Metadata = new Dictionary<string, object> { [OpenApiSchemaNullability.SchemaIdKey] = id },
        };

    private static string? _ReferenceId(IOpenApiSchema? schema) =>
        schema.Should().BeOfType<OpenApiSchemaReference>().Which.Reference.Id;

    [Fact]
    public void should_register_the_named_types_inside_an_inline_schema()
    {
        var document = new OpenApiDocument();
        var inline = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["address"] = _Named("Address"),
            },
        };

        var result = OpenApiSchemaComponents.Resolve(document, inline);

        result.Should().BeSameAs(inline);
        _ReferenceId(inline.Properties["address"]).Should().Be("Address");
        document.Components!.Schemas!.Keys.Should().Equal("Address");
    }

    [Fact]
    public void should_replace_named_alternatives_in_every_composition_keyword_with_references()
    {
        var document = new OpenApiDocument();
        var inline = new OpenApiSchema
        {
            AllOf = [_Named("Base"), new OpenApiSchema { Type = JsonSchemaType.Object }],
            AnyOf = [_Named("Left"), _Named("Right")],
            OneOf = [_Named("Only")],
        };

        OpenApiSchemaComponents.Resolve(document, inline);

        _ReferenceId(inline.AllOf[0]).Should().Be("Base");
        inline.AllOf[1].Should().BeOfType<OpenApiSchema>();
        inline.AnyOf.Select(_ReferenceId).Should().Equal("Left", "Right");
        _ReferenceId(inline.OneOf.Should().ContainSingle().Subject).Should().Be("Only");
        document.Components!.Schemas!.Keys.Should().BeEquivalentTo(["Base", "Left", "Right", "Only"]);
    }

    [Fact]
    public void should_wrap_a_marked_nullable_property_in_a_null_alternative()
    {
        var document = new OpenApiDocument();
        var nullable = _Named("Address");
        nullable.Metadata![OpenApiSchemaNullability.NullablePropertyKey] = true;
        var inline = new OpenApiSchema
        {
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal) { ["address"] = nullable },
        };

        OpenApiSchemaComponents.Resolve(document, inline);

        var alternatives = inline.Properties["address"].OneOf;
        alternatives.Should().HaveCount(2);
        alternatives![0].Type.Should().Be(JsonSchemaType.Null);
        _ReferenceId(alternatives[1]).Should().Be("Address");
    }
}
