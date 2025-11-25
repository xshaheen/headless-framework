// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Constants;
using Framework.Primitives;
using Microsoft.AspNetCore.Http;
using NJsonSchema;
using NSwag;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

// ReSharper disable InconsistentNaming
namespace Framework.OpenApi.Nswag.OperationProcessors;

/// <summary>
/// Shows an example of a ProblemDetails containing errors for known status codes in NSwag.
/// </summary>
public sealed class ProblemDetailsOperationProcessor : IOperationProcessor
{
    private readonly JsonSchema _baseProblemDetailsSchema;
    private readonly JsonSchema _entityNotFoundProblemDetailsSchema;
    private readonly JsonSchema _conflictProblemDetailsSchema;
    private readonly JsonSchema _unprocessableEntityProblemDetailsSchema;

    public ProblemDetailsOperationProcessor()
    {
        _baseProblemDetailsSchema = JsonSchema.FromType<HeadlessProblemDetails>();
        _entityNotFoundProblemDetailsSchema = JsonSchema.FromType<EntityNotFoundHeadlessProblemDetails>();
        _conflictProblemDetailsSchema = JsonSchema.FromType<ConflictHeadlessProblemDetails>();
        _unprocessableEntityProblemDetailsSchema = JsonSchema.FromType<UnprocessableEntityHeadlessProblemDetails>();
    }

    private readonly HeadlessProblemDetails _status400ProblemDetails = new()
    {
        type = ProblemDetailsConstants.Types.BadRequest,
        title = ProblemDetailsConstants.Titles.BadRequest,
        status = StatusCodes.Status400BadRequest,
        detail = ProblemDetailsConstants.Details.BadRequest,
        instance = "/public/some-endpoint",
        traceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
        buildNumber = "1.0.0",
        commitNumber = "abc123def",
        timestamp = DateTimeOffset.UtcNow,
    };

    private readonly HeadlessProblemDetails _status401ProblemDetails = new()
    {
        type = ProblemDetailsConstants.Types.Unauthorized,
        title = ProblemDetailsConstants.Titles.Unauthorized,
        status = StatusCodes.Status401Unauthorized,
        detail = ProblemDetailsConstants.Details.Unauthorized,
        instance = "/public/some-endpoint",
        traceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
        buildNumber = "1.0.0",
        commitNumber = "abc123def",
        timestamp = DateTimeOffset.UtcNow,
    };

    private readonly HeadlessProblemDetails _status403ProblemDetails = new()
    {
        type = ProblemDetailsConstants.Types.Forbidden,
        title = ProblemDetailsConstants.Titles.Forbidden,
        status = StatusCodes.Status403Forbidden,
        detail = ProblemDetailsConstants.Details.Forbidden,
        instance = "/public/some-endpoint",
        traceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
        buildNumber = "1.0.0",
        commitNumber = "abc123def",
        timestamp = DateTimeOffset.UtcNow,
    };

    private readonly EntityNotFoundHeadlessProblemDetails _status404ProblemDetails = new()
    {
        type = ProblemDetailsConstants.Types.EntityNotFound,
        title = ProblemDetailsConstants.Titles.EntityNotFound,
        status = StatusCodes.Status404NotFound,
        detail = ProblemDetailsConstants.Details.EntityNotFound("User", "user-123"),
        instance = "/public/some-endpoint",
        traceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
        buildNumber = "1.0.0",
        commitNumber = "abc123def",
        timestamp = DateTimeOffset.UtcNow,
        @params = new EntityNotFoundHeadlessProblemDetailsParams { entity = "User", key = "user-123" },
    };

    private readonly ConflictHeadlessProblemDetails _status409ProblemDetails = new()
    {
        type = ProblemDetailsConstants.Types.Conflict,
        title = ProblemDetailsConstants.Titles.Conflict,
        status = StatusCodes.Status409Conflict,
        detail = ProblemDetailsConstants.Details.Conflict,
        instance = "/public/some-endpoint",
        traceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
        buildNumber = "1.0.0",
        commitNumber = "abc123def",
        timestamp = DateTimeOffset.UtcNow,
        errors = [new("business_error", @"Some business rule failed.")],
    };

    private readonly UnprocessableEntityHeadlessProblemDetails _status422ProblemDetails = new()
    {
        type = ProblemDetailsConstants.Types.UnprocessableEntity,
        title = ProblemDetailsConstants.Titles.UnprocessableEntity,
        status = StatusCodes.Status422UnprocessableEntity,
        detail = ProblemDetailsConstants.Details.UnprocessableEntity,
        instance = "/public/some-endpoint",
        traceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
        buildNumber = "1.0.0",
        commitNumber = "abc123def",
        timestamp = DateTimeOffset.UtcNow,
        errors = new(StringComparer.Ordinal)
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

    public bool Process(OperationProcessorContext context)
    {
        Argument.IsNotNull(context);

        var operation = context.OperationDescription.Operation;

        foreach (var response in operation.Responses)
        {
            _SetExampleResponseForKnownStatus(response.Key, response.Value);
        }
        return true;
    }

    private void _SetExampleResponseForKnownStatus(string statusCode, OpenApiResponse response)
    {
        switch (statusCode)
        {
            case "400":
                _SetDefaultAndExample(response, _status400ProblemDetails, _baseProblemDetailsSchema);
                break;
            case "401":
                _SetDefaultAndExample(response, _status401ProblemDetails, _baseProblemDetailsSchema);
                break;
            case "403":
                _SetDefaultAndExample(response, _status403ProblemDetails, _baseProblemDetailsSchema);
                break;
            case "404":
                _SetDefaultAndExample(response, _status404ProblemDetails, _entityNotFoundProblemDetailsSchema);
                break;
            case "409":
                _SetDefaultAndExample(response, _status409ProblemDetails, _conflictProblemDetailsSchema);
                break;
            case "422":
                _SetDefaultAndExample(response, _status422ProblemDetails, _unprocessableEntityProblemDetailsSchema);
                break;
        }
    }

    private static void _SetDefaultAndExample(OpenApiResponse response, object problemDetails, JsonSchema schema)
    {
        if (response.Content == null)
        {
            // Cannot set Content if null, so nothing to do
            return;
        }

        // Ensure ProblemJson content type exists with proper schema
        if (!response.Content.TryGetValue(ContentTypes.Applications.ProblemJson, out var problemJsonMediaType))
        {
            problemJsonMediaType = new OpenApiMediaType { Schema = schema };
            response.Content[ContentTypes.Applications.ProblemJson] = problemJsonMediaType;
        }
        else
        {
            problemJsonMediaType.Schema = schema;
        }

        problemJsonMediaType.Example = problemDetails;

        // Also add application/json content type with the same schema and example
        if (!response.Content.TryGetValue(ContentTypes.Applications.Json, out var jsonMediaType))
        {
            jsonMediaType = new OpenApiMediaType { Schema = schema };
            response.Content[ContentTypes.Applications.Json] = jsonMediaType;
        }
        else
        {
            jsonMediaType.Schema = schema;
        }

        jsonMediaType.Example = problemDetails;
    }

#pragma warning disable IDE1006
    public class HeadlessProblemDetails
    {
        public required string type { get; init; }
        public required string title { get; init; }
        public required int status { get; init; }
        public required string detail { get; init; }
        public required string instance { get; init; }
        public required string traceId { get; init; }
        public required string buildNumber { get; init; }
        public required string commitNumber { get; init; }
        public required DateTimeOffset timestamp { get; init; }
    }

    public sealed class EntityNotFoundHeadlessProblemDetailsParams
    {
        public required string entity { get; init; }
        public required string key { get; init; }
    }

    public sealed class EntityNotFoundHeadlessProblemDetails : HeadlessProblemDetails
    {
        public required EntityNotFoundHeadlessProblemDetailsParams @params { get; init; }
    }

    public sealed class ConflictHeadlessProblemDetails : HeadlessProblemDetails
    {
        public required List<ErrorDescriptor> errors { get; init; }
    }

    public sealed class UnprocessableEntityHeadlessProblemDetails : HeadlessProblemDetails
    {
        public required Dictionary<string, List<ErrorDescriptor>> errors { get; init; }
    }
#pragma warning restore IDE1006
}
