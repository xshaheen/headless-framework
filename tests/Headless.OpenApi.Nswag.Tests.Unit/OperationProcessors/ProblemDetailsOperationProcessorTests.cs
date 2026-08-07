// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.OpenApi.Nswag.Models;
using Headless.OpenApi.Nswag.OperationProcessors;
using Headless.Testing.Tests;
using NJsonSchema.Generation;
using NSwag;
using NSwag.Generation;
using NSwag.Generation.AspNetCore;
using NSwag.Generation.Processors.Contexts;

namespace Tests.OperationProcessors;

public sealed class ProblemDetailsOperationProcessorTests : TestBase
{
    public static TheoryData<string, Type> ProblemResponseCases =>
        new()
        {
            { "400", typeof(BadRequestProblemDetails) },
            { "401", typeof(UnauthorizedProblemDetails) },
            { "403", typeof(ForbiddenProblemDetails) },
            { "404", typeof(EntityNotFoundProblemDetails) },
            { "409", typeof(ConflictProblemDetails) },
            { "422", typeof(UnprocessableEntityProblemDetails) },
            { "429", typeof(TooManyRequestsProblemDetails) },
        };

    [Theory]
    [MemberData(nameof(ProblemResponseCases))]
    public void should_attach_the_typed_problem_schema_and_example_to_known_error_responses(
        string statusCode,
        Type problemType
    )
    {
        var context = _CreateContext((statusCode, new OpenApiResponse()));

        var processed = new ProblemDetailsOperationProcessor().Process(context);

        processed.Should().BeTrue();
        var mediaType = context.OperationDescription.Operation.Responses[statusCode].Content[
            "application/problem+json"
        ];
        mediaType.Schema.Reference.Should().BeSameAs(context.Document.Definitions[problemType.Name]);
        mediaType.Example.Should().BeOfType(problemType);
    }

    [Fact]
    public void should_replace_an_existing_problem_media_schema_without_removing_other_content_types()
    {
        var response = new OpenApiResponse();
        response.Content["application/problem+json"] = new OpenApiMediaType
        {
            Schema = new NJsonSchema.JsonSchema { Type = NJsonSchema.JsonObjectType.String },
        };
        response.Content["application/json"] = new OpenApiMediaType();
        var context = _CreateContext(("400", response));

        new ProblemDetailsOperationProcessor().Process(context);

        response.Content.Keys.Should().BeEquivalentTo("application/problem+json", "application/json");
        response
            .Content["application/problem+json"]
            .Schema.Reference.Should()
            .BeSameAs(context.Document.Definitions[nameof(BadRequestProblemDetails)]);
    }

    [Fact]
    public void should_leave_unrecognized_responses_unchanged_while_registering_shared_definitions_once()
    {
        var response = new OpenApiResponse { Description = "Server error" };
        var context = _CreateContext(("500", response));
        var processor = new ProblemDetailsOperationProcessor();

        processor.Process(context);
        var definitions = context.Document.Definitions.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal
        );
        processor.Process(context);

        response.Content.Should().BeEmpty();
        context.Document.Definitions.Should().HaveCount(definitions.Count);
        context.Document.Definitions.Should().ContainKeys(definitions.Keys.ToArray());
        context.Document.Definitions.Should().ContainKey(nameof(HeadlessProblemDetails));
    }

    private static OperationProcessorContext _CreateContext(
        params (string StatusCode, OpenApiResponse Response)[] responses
    )
    {
        var document = new OpenApiDocument();
        var operation = new OpenApiOperation();
        foreach (var (statusCode, response) in responses)
        {
            operation.Responses[statusCode] = response;
        }

        var description = new OpenApiOperationDescription
        {
            Operation = operation,
            Path = "/test",
            Method = "GET",
        };
        var settings = new AspNetCoreOpenApiDocumentGeneratorSettings();
        var resolver = new JsonSchemaResolver(new NJsonSchema.JsonSchema(), settings.SchemaSettings);
        var generator = new OpenApiDocumentGenerator(settings, resolver);

        return new OperationProcessorContext(
            document,
            description,
            typeof(ProblemDetailsOperationProcessorTests),
            typeof(ProblemDetailsOperationProcessorTests).GetMethod(
                nameof(_Endpoint),
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly,
                binder: null,
                Type.EmptyTypes,
                modifiers: null
            ),
            generator,
            resolver,
            settings,
            [description]
        );
    }

    private static void _Endpoint() { }
}
