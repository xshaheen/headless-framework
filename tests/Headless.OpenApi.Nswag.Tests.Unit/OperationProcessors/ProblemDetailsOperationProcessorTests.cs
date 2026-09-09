// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.OpenApi.Nswag;
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
            { "428", typeof(PreconditionRequiredProblemDetails) },
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
        context.Document.Definitions.Should().ContainKeys([.. definitions.Keys]);
        context.Document.Definitions.Should().ContainKey(nameof(HeadlessProblemDetails));
    }

    public static TheoryData<Type> SingleErrorProblemTypes =>
        [
            typeof(BadRequestProblemDetails),
            typeof(UnauthorizedProblemDetails),
            typeof(ForbiddenProblemDetails),
            typeof(EntityNotFoundProblemDetails),
            typeof(TooManyRequestsProblemDetails),
        ];

    [Theory]
    [MemberData(nameof(SingleErrorProblemTypes))]
    public void should_declare_the_optional_error_descriptor_the_creator_writes(Type problemType)
    {
        // IProblemDetailsCreator writes an "error" member for every one of these responses, and the
        // generated definition forbids additional properties. An undeclared error therefore makes the
        // framework's own body fail validation against the schema the same framework published — the
        // tenant-required 403 (g:tenant_required) is the case that occurs out of the box.
        var context = _CreateContext(("400", new OpenApiResponse()));

        new ProblemDetailsOperationProcessor().Process(context);

        var definition = context.Document.Definitions[problemType.Name];
        definition.AllowAdditionalProperties.Should().BeFalse();
        definition.ActualProperties.Should().ContainKey(nameof(ForbiddenProblemDetails.Error));
        definition.ActualProperties[nameof(ForbiddenProblemDetails.Error)].IsRequired.Should().BeFalse();
    }

    [Fact]
    public void should_declare_the_retry_after_the_creator_writes_for_too_many_requests()
    {
        var context = _CreateContext(("429", new OpenApiResponse()));

        new ProblemDetailsOperationProcessor().Process(context);

        var definition = context.Document.Definitions[nameof(TooManyRequestsProblemDetails)];
        var retryAfter = nameof(TooManyRequestsProblemDetails.RetryAfter);
        definition.AllowAdditionalProperties.Should().BeFalse();
        definition.ActualProperties.Should().ContainKey(retryAfter);
        definition.ActualProperties[retryAfter].IsRequired.Should().BeTrue();
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
        // Mirrors SetupNswag: without it the derived problem types generate as an allOf against the base,
        // where the base subschema still forbids additional properties and knows nothing of the derived
        // members — a different (and more permissive) shape than any real document this package produces.
        settings.SchemaSettings.FlattenInheritanceHierarchy = true;
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
