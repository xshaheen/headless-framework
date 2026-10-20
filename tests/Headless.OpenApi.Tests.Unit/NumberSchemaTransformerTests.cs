// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Headless.OpenApi;
using Headless.Testing.Tests;
using Microsoft.OpenApi;

namespace Tests;

public sealed class NumberSchemaTransformerTests : TestBase
{
    private const string _DigitsPattern = "^-?(?:0|[1-9]\\d*)$";

    private static readonly JsonSerializerOptions _LenientOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    [Fact]
    public async Task should_keep_the_string_alternative_when_the_declaring_type_writes_numbers_as_strings()
    {
        var property = _LenientOptions.GetTypeInfo(typeof(StringNumbersView)).Properties.Single();
        var schema = _UnionSchema();

        await new NumberSchemaTransformer().TransformAsync(
            schema,
            TransformerContexts.Schema(_LenientOptions.GetTypeInfo(typeof(int)), property),
            AbortToken
        );

        schema.Type.Should().Be(JsonSchemaType.Integer | JsonSchemaType.String);
        schema.Pattern.Should().Be(_DigitsPattern);
    }

    [Fact]
    public async Task should_follow_the_number_handling_of_the_type_contract_over_the_options()
    {
        // A contract modifier can override the options' number handling for one type.
        var options = new JsonSerializerOptions
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    typeInfo =>
                    {
                        if (typeInfo.Type == typeof(long))
                        {
                            typeInfo.NumberHandling = JsonNumberHandling.WriteAsString;
                        }
                    },
                },
            },
        };
        var schema = _UnionSchema();

        await new NumberSchemaTransformer().TransformAsync(
            schema,
            TransformerContexts.Schema(options.GetTypeInfo(typeof(long))),
            AbortToken
        );

        schema.Type.Should().Be(JsonSchemaType.Integer | JsonSchemaType.String);
        schema.Pattern.Should().Be(_DigitsPattern);
    }

    [Fact]
    public async Task should_drop_the_string_alternative_of_a_number_only_read_from_strings()
    {
        var schema = _UnionSchema();

        await new NumberSchemaTransformer().TransformAsync(
            schema,
            TransformerContexts.Schema(_LenientOptions.GetTypeInfo(typeof(long))),
            AbortToken
        );

        schema.Type.Should().Be(JsonSchemaType.Integer);
        schema.Pattern.Should().BeNull();
    }

    private static OpenApiSchema _UnionSchema() =>
        new() { Type = JsonSchemaType.Integer | JsonSchemaType.String, Pattern = _DigitsPattern };
}

[JsonNumberHandling(JsonNumberHandling.WriteAsString)]
public sealed class StringNumbersView
{
    public int Count { get; init; }
}
