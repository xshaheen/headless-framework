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
    private readonly JsonSchema _errorDescriptorSchema = JsonSchema.FromType<ErrorDescriptor>();
    private readonly JsonSchema _problemDetailsSchema = JsonSchema.FromType<ProblemDetails>();
    private readonly JsonSchema _entityNotFoundSchema = JsonSchema.FromType<EntityNotFoundProblemDetails>();
    private readonly JsonSchema _entityNotFoundParamsSchema = JsonSchema.FromType<EntityNotFoundProblemDetailsParams>();
    private readonly JsonSchema _conflictSchema = JsonSchema.FromType<ConflictProblemDetails>();
    private readonly JsonSchema _unprocessableEntitySchema = JsonSchema.FromType<UnprocessableEntityProblemDetails>();
    private readonly JsonSchema _badRequestSchema = JsonSchema.FromType<BadRequestProblemDetails>();
    private readonly JsonSchema _unauthorizedSchema = JsonSchema.FromType<UnauthorizedProblemDetails>();
    private readonly JsonSchema _forbiddenSchema = JsonSchema.FromType<ForbiddenProblemDetails>();

    private readonly BadRequestProblemDetails _status400ProblemDetails = new()
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

    private readonly UnauthorizedProblemDetails _status401ProblemDetails = new()
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

    private readonly ForbiddenProblemDetails _status403ProblemDetails = new()
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

    private readonly EntityNotFoundProblemDetails _status404ProblemDetails = new()
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
        @params = new EntityNotFoundProblemDetailsParams { entity = "User", key = "user-123" },
    };

    private readonly ConflictProblemDetails _status409ProblemDetails = new()
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

    private readonly UnprocessableEntityProblemDetails _status422ProblemDetails = new()
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

        // Register schemas in document definitions to enable $ref usage
        _RegisterSchemaIfNeeded(context, _errorDescriptorSchema, nameof(ErrorDescriptor));
        _RegisterSchemaIfNeeded(context, _problemDetailsSchema, nameof(ProblemDetails));
        _RegisterSchemaIfNeeded(context, _entityNotFoundSchema, nameof(EntityNotFoundProblemDetails));
        _RegisterSchemaIfNeeded(context, _entityNotFoundParamsSchema, nameof(EntityNotFoundProblemDetailsParams));
        _RegisterSchemaIfNeeded(context, _conflictSchema, nameof(ConflictProblemDetails));
        _RegisterSchemaIfNeeded(context, _unprocessableEntitySchema, nameof(UnprocessableEntityProblemDetails));
        _RegisterSchemaIfNeeded(context, _badRequestSchema, nameof(BadRequestProblemDetails));
        _RegisterSchemaIfNeeded(context, _unauthorizedSchema, nameof(UnauthorizedProblemDetails));
        _RegisterSchemaIfNeeded(context, _forbiddenSchema, nameof(ForbiddenProblemDetails));

        var operation = context.OperationDescription.Operation;

        foreach (var response in operation.Responses)
        {
            _SetExampleResponseForKnownStatus(response.Key, response.Value);
        }
        return true;
    }

    private static void _RegisterSchemaIfNeeded(OperationProcessorContext context, JsonSchema schema, string schemaName)
    {
        if (!context.Document.Definitions.ContainsKey(schemaName))
        {
            context.Document.Definitions[schemaName] = schema;
        }
    }

    private void _SetExampleResponseForKnownStatus(string statusCode, OpenApiResponse response)
    {
        switch (statusCode)
        {
            case "400":
                _SetDefaultAndExample(response, _status400ProblemDetails, _badRequestSchema);
                break;
            case "401":
                _SetDefaultAndExample(response, _status401ProblemDetails, _unauthorizedSchema);
                break;
            case "403":
                _SetDefaultAndExample(response, _status403ProblemDetails, _forbiddenSchema);
                break;
            case "404":
                _SetDefaultAndExample(response, _status404ProblemDetails, _entityNotFoundSchema);
                break;
            case "409":
                _SetDefaultAndExample(response, _status409ProblemDetails, _conflictSchema);
                break;
            case "422":
                _SetDefaultAndExample(response, _status422ProblemDetails, _unprocessableEntitySchema);
                break;
        }
    }

    private static void _SetDefaultAndExample(OpenApiResponse response, object problemDetails, JsonSchema schema)
    {
        if (response.Content == null)
        {
            return; // Cannot set Content if null, so nothing to do
        }

        // Create a schema reference instead of embedding the schema
        var schemaReference = new JsonSchema { Reference = schema };

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

    #region Types

#pragma warning disable IDE1006
    public class ProblemDetails
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

    public sealed class BadRequestProblemDetails : ProblemDetails;

    public sealed class UnauthorizedProblemDetails : ProblemDetails;

    public sealed class ForbiddenProblemDetails : ProblemDetails;

    public sealed class EntityNotFoundProblemDetailsParams
    {
        public required string entity { get; init; }
        public required string key { get; init; }
    }

    public sealed class EntityNotFoundProblemDetails : ProblemDetails
    {
        public required EntityNotFoundProblemDetailsParams @params { get; init; }
    }

    public sealed class ConflictProblemDetails : ProblemDetails
    {
        public required List<ErrorDescriptor> errors { get; init; }
    }

    public sealed class UnprocessableEntityProblemDetails : ProblemDetails
    {
        public required Dictionary<string, List<ErrorDescriptor>> errors { get; init; }
    }
#pragma warning restore IDE1006

    #endregion
}
