// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Models;
using Headless.Checks;
using Headless.Constants;
using Headless.Constants;
using Headless.Primitives;
using Microsoft.AspNetCore.Http;
using NJsonSchema;
using NJsonSchema.Generation;
using NSwag;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Headless.Api.OperationProcessors;

/// <summary>
/// Shows an example of a ProblemDetails containing errors for known status codes in NSwag.
/// </summary>
public sealed class ProblemDetailsOperationProcessor : IOperationProcessor
{
    public bool Process(OperationProcessorContext context)
    {
        Argument.IsNotNull(context);

        // Create and register all schemas in document definitions to enable $ref usage
        var generator = new JsonSchemaGenerator(context.SchemaGenerator.Settings);

        _RegisterSchema(context, generator, typeof(ValidationSeverity));
        _RegisterSchema(context, generator, typeof(HeadlessProblemDetails));
        _RegisterSchema(context, generator, typeof(ErrorDescriptor));
        _RegisterSchema(context, generator, typeof(EntityNotFoundProblemDetails));
        _RegisterSchema(context, generator, typeof(ConflictProblemDetails));
        _RegisterSchema(context, generator, typeof(UnprocessableEntityProblemDetails));
        _RegisterSchema(context, generator, typeof(BadRequestProblemDetails));
        _RegisterSchema(context, generator, typeof(UnauthorizedProblemDetails));
        _RegisterSchema(context, generator, typeof(ForbiddenProblemDetails));
        _RegisterSchema(context, generator, typeof(TooManyRequestsProblemDetails));

        var operation = context.OperationDescription.Operation;

        foreach (var response in operation.Responses)
        {
            _SetExampleResponses(context, response.Key, response.Value);
        }

        return true;
    }

    private static void _RegisterSchema(OperationProcessorContext context, JsonSchemaGenerator generator, Type type)
    {
        if (!context.Document.Definitions.ContainsKey(type.Name))
        {
            var schema = generator.Generate(type, context.SchemaResolver);
            context.Document.Definitions[type.Name] = schema;
        }
    }

    private void _SetExampleResponses(OperationProcessorContext context, string statusCode, OpenApiResponse response)
    {
        switch (statusCode)
        {
            case OpenApiStatusCodes.BadRequest:
                _SetDefaultAndExample(context, response, _status400ProblemDetails, nameof(BadRequestProblemDetails));
                break;
            case OpenApiStatusCodes.Unauthorized:
                _SetDefaultAndExample(context, response, _status401ProblemDetails, nameof(UnauthorizedProblemDetails));
                break;
            case OpenApiStatusCodes.Forbidden:
                _SetDefaultAndExample(context, response, _status403ProblemDetails, nameof(ForbiddenProblemDetails));
                break;
            case OpenApiStatusCodes.NotFound:
                _SetDefaultAndExample(
                    context,
                    response,
                    _status404ProblemDetails,
                    nameof(EntityNotFoundProblemDetails)
                );
                break;
            case OpenApiStatusCodes.Conflict:
                _SetDefaultAndExample(context, response, _status409ProblemDetails, nameof(ConflictProblemDetails));
                break;
            case OpenApiStatusCodes.UnprocessableEntity:
                _SetDefaultAndExample(
                    context,
                    response,
                    _status422ProblemDetails,
                    nameof(UnprocessableEntityProblemDetails)
                );
                break;
            case OpenApiStatusCodes.TooManyRequests:
                _SetDefaultAndExample(
                    context,
                    response,
                    _status429ProblemDetails,
                    nameof(TooManyRequestsProblemDetails)
                );
                break;
        }
    }

    private static void _SetDefaultAndExample(
        OperationProcessorContext context,
        OpenApiResponse response,
        object problemDetails,
        string schemaName
    )
    {
        if (response.Content == null)
        {
            return; // Cannot set Content if null, so nothing to do
        }

        // Create a proper reference schema pointing to the registered definition
        var schemaReference = new JsonSchema { Reference = context.Document.Definitions[schemaName] };

        // Ensure ProblemJson content type exists with schema reference
        if (!response.Content.TryGetValue(ContentTypes.Applications.ProblemJson, out var problemJsonMediaType))
        {
            problemJsonMediaType = new OpenApiMediaType { Schema = schemaReference };
            response.Content[ContentTypes.Applications.ProblemJson] = problemJsonMediaType;
        }
        else
        {
            problemJsonMediaType.Schema = schemaReference;
        }

        problemJsonMediaType.Example = problemDetails;
    }

    #region Examples

    private static readonly DateTimeOffset _ExampleTimestamp = new(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly BadRequestProblemDetails _status400ProblemDetails = new()
    {
        Type = HeadlessProblemDetailsConstants.Types.BadRequest,
        Title = HeadlessProblemDetailsConstants.Titles.BadRequest,
        Status = StatusCodes.Status400BadRequest,
        Detail = HeadlessProblemDetailsConstants.Details.BadRequest,
        Instance = "/public/some-endpoint",
        TraceId = "<trace-id>",
        BuildNumber = "<version>",
        CommitNumber = "<commit>",
        Timestamp = _ExampleTimestamp,
    };

    private static readonly UnauthorizedProblemDetails _status401ProblemDetails = new()
    {
        Type = HeadlessProblemDetailsConstants.Types.Unauthorized,
        Title = HeadlessProblemDetailsConstants.Titles.Unauthorized,
        Status = StatusCodes.Status401Unauthorized,
        Detail = HeadlessProblemDetailsConstants.Details.Unauthorized,
        Instance = "/public/some-endpoint",
        TraceId = "<trace-id>",
        BuildNumber = "<version>",
        CommitNumber = "<commit>",
        Timestamp = _ExampleTimestamp,
    };

    private static readonly ForbiddenProblemDetails _status403ProblemDetails = new()
    {
        Type = HeadlessProblemDetailsConstants.Types.Forbidden,
        Title = HeadlessProblemDetailsConstants.Titles.Forbidden,
        Status = StatusCodes.Status403Forbidden,
        Detail = HeadlessProblemDetailsConstants.Details.Forbidden,
        Instance = "/public/some-endpoint",
        TraceId = "<trace-id>",
        BuildNumber = "<version>",
        CommitNumber = "<commit>",
        Timestamp = _ExampleTimestamp,
    };

    private static readonly EntityNotFoundProblemDetails _status404ProblemDetails = new()
    {
        Type = HeadlessProblemDetailsConstants.Types.EntityNotFound,
        Title = HeadlessProblemDetailsConstants.Titles.EntityNotFound,
        Status = StatusCodes.Status404NotFound,
        Detail = HeadlessProblemDetailsConstants.Details.EntityNotFound,
        Instance = "/public/some-endpoint",
        TraceId = "<trace-id>",
        BuildNumber = "<version>",
        CommitNumber = "<commit>",
        Timestamp = _ExampleTimestamp,
    };

    private static readonly ConflictProblemDetails _status409ProblemDetails = new()
    {
        Type = HeadlessProblemDetailsConstants.Types.Conflict,
        Title = HeadlessProblemDetailsConstants.Titles.Conflict,
        Status = StatusCodes.Status409Conflict,
        Detail = HeadlessProblemDetailsConstants.Details.Conflict,
        Instance = "/public/some-endpoint",
        TraceId = "<trace-id>",
        BuildNumber = "<version>",
        CommitNumber = "<commit>",
        Timestamp = _ExampleTimestamp,
        Errors = [new("business_error", @"Some business rule failed.")],
    };

    private static readonly UnprocessableEntityProblemDetails _status422ProblemDetails = new()
    {
        Type = HeadlessProblemDetailsConstants.Types.UnprocessableEntity,
        Title = HeadlessProblemDetailsConstants.Titles.UnprocessableEntity,
        Status = StatusCodes.Status422UnprocessableEntity,
        Detail = HeadlessProblemDetailsConstants.Details.UnprocessableEntity,
        Instance = "/public/some-endpoint",
        TraceId = "<trace-id>",
        BuildNumber = "<version>",
        CommitNumber = "<commit>",
        Timestamp = _ExampleTimestamp,
        Errors = new(StringComparer.Ordinal)
        {
            ["email"] =
            [
                new ErrorDescriptor("auth:invalid_email_format", @"The email address format is invalid."),
                new ErrorDescriptor("auth:email_already_exists", @"The specified email address is already in use."),
            ],
            ["username"] =
            [
                new ErrorDescriptor("auth:username_too_short", @"The username must be at least 6 characters long."),
            ],
        },
    };

    private static readonly TooManyRequestsProblemDetails _status429ProblemDetails = new()
    {
        Type = HeadlessProblemDetailsConstants.Types.TooManyRequests,
        Title = HeadlessProblemDetailsConstants.Titles.TooManyRequests,
        Status = StatusCodes.Status429TooManyRequests,
        Detail = HeadlessProblemDetailsConstants.Details.TooManyRequests,
        Instance = "/public/some-endpoint",
        TraceId = "<trace-id>",
        BuildNumber = "<version>",
        CommitNumber = "<commit>",
        Timestamp = _ExampleTimestamp,
    };

    #endregion
}
