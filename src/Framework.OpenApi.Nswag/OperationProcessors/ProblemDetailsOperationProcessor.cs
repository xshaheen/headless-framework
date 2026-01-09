// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Constants;
using Framework.Primitives;
using Microsoft.AspNetCore.Http;
using NJsonSchema;
using NJsonSchema.Generation;
using NSwag;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Framework.Api.OperationProcessors;

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
        _RegisterSchema(context, generator, typeof(EntityNotFoundProblemDetailsParams));
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
            case "400":
                _SetDefaultAndExample(context, response, _status400ProblemDetails, nameof(BadRequestProblemDetails));
                break;
            case "401":
                _SetDefaultAndExample(context, response, _status401ProblemDetails, nameof(UnauthorizedProblemDetails));
                break;
            case "403":
                _SetDefaultAndExample(context, response, _status403ProblemDetails, nameof(ForbiddenProblemDetails));
                break;
            case "404":
                _SetDefaultAndExample(
                    context,
                    response,
                    _status404ProblemDetails,
                    nameof(EntityNotFoundProblemDetails)
                );
                break;
            case "409":
                _SetDefaultAndExample(context, response, _status409ProblemDetails, nameof(ConflictProblemDetails));
                break;
            case "422":
                _SetDefaultAndExample(
                    context,
                    response,
                    _status422ProblemDetails,
                    nameof(UnprocessableEntityProblemDetails)
                );
                break;
            case "429":
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

    private readonly BadRequestProblemDetails _status400ProblemDetails = new()
    {
        Type = HeadlessProblemDetailsConstants.Types.BadRequest,
        Title = HeadlessProblemDetailsConstants.Titles.BadRequest,
        Status = StatusCodes.Status400BadRequest,
        Detail = HeadlessProblemDetailsConstants.Details.BadRequest,
        Instance = "/public/some-endpoint",
        TraceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
        BuildNumber = "1.0.0",
        CommitNumber = "abc123def",
        Timestamp = DateTimeOffset.UtcNow,
    };

    private readonly UnauthorizedProblemDetails _status401ProblemDetails = new()
    {
        Type = HeadlessProblemDetailsConstants.Types.Unauthorized,
        Title = HeadlessProblemDetailsConstants.Titles.Unauthorized,
        Status = StatusCodes.Status401Unauthorized,
        Detail = HeadlessProblemDetailsConstants.Details.Unauthorized,
        Instance = "/public/some-endpoint",
        TraceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
        BuildNumber = "1.0.0",
        CommitNumber = "abc123def",
        Timestamp = DateTimeOffset.UtcNow,
    };

    private readonly ForbiddenProblemDetails _status403ProblemDetails = new()
    {
        Type = HeadlessProblemDetailsConstants.Types.Forbidden,
        Title = HeadlessProblemDetailsConstants.Titles.Forbidden,
        Status = StatusCodes.Status403Forbidden,
        Detail = HeadlessProblemDetailsConstants.Details.Forbidden,
        Instance = "/public/some-endpoint",
        TraceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
        BuildNumber = "1.0.0",
        CommitNumber = "abc123def",
        Timestamp = DateTimeOffset.UtcNow,
    };

    private readonly EntityNotFoundProblemDetails _status404ProblemDetails = new()
    {
        Type = HeadlessProblemDetailsConstants.Types.EntityNotFound,
        Title = HeadlessProblemDetailsConstants.Titles.EntityNotFound,
        Status = StatusCodes.Status404NotFound,
        Detail = HeadlessProblemDetailsConstants.Details.EntityNotFound("User", "user-123"),
        Instance = "/public/some-endpoint",
        TraceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
        BuildNumber = "1.0.0",
        CommitNumber = "abc123def",
        Timestamp = DateTimeOffset.UtcNow,
        Params = new EntityNotFoundProblemDetailsParams { Entity = "User", Key = "user-123" },
    };

    private readonly ConflictProblemDetails _status409ProblemDetails = new()
    {
        Type = HeadlessProblemDetailsConstants.Types.Conflict,
        Title = HeadlessProblemDetailsConstants.Titles.Conflict,
        Status = StatusCodes.Status409Conflict,
        Detail = HeadlessProblemDetailsConstants.Details.Conflict,
        Instance = "/public/some-endpoint",
        TraceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
        BuildNumber = "1.0.0",
        CommitNumber = "abc123def",
        Timestamp = DateTimeOffset.UtcNow,
        Errors = [new("business_error", @"Some business rule failed.")],
    };

    private readonly UnprocessableEntityProblemDetails _status422ProblemDetails = new()
    {
        Type = HeadlessProblemDetailsConstants.Types.UnprocessableEntity,
        Title = HeadlessProblemDetailsConstants.Titles.UnprocessableEntity,
        Status = StatusCodes.Status422UnprocessableEntity,
        Detail = HeadlessProblemDetailsConstants.Details.UnprocessableEntity,
        Instance = "/public/some-endpoint",
        TraceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
        BuildNumber = "1.0.0",
        CommitNumber = "abc123def",
        Timestamp = DateTimeOffset.UtcNow,
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

    private readonly TooManyRequestsProblemDetails _status429ProblemDetails = new()
    {
        Type = HeadlessProblemDetailsConstants.Types.TooManyRequests,
        Title = HeadlessProblemDetailsConstants.Titles.TooManyRequests,
        Status = StatusCodes.Status429TooManyRequests,
        Detail = HeadlessProblemDetailsConstants.Details.TooManyRequests,
        Instance = "/public/some-endpoint",
        TraceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
        BuildNumber = "1.0.0",
        CommitNumber = "abc123def",
        Timestamp = DateTimeOffset.UtcNow,
    };

    #endregion

    #region ProblemDetails Types

    public class HeadlessProblemDetails
    {
        public required string Type { get; init; }
        public required string Title { get; init; }
        public required int Status { get; init; }
        public required string Detail { get; init; }
        public required string Instance { get; init; }
        public required string TraceId { get; init; }
        public required string BuildNumber { get; init; }
        public required string CommitNumber { get; init; }
        public required DateTimeOffset Timestamp { get; init; }
    }

    public sealed class BadRequestProblemDetails : HeadlessProblemDetails;

    public sealed class UnauthorizedProblemDetails : HeadlessProblemDetails;

    public sealed class ForbiddenProblemDetails : HeadlessProblemDetails;

    public sealed class TooManyRequestsProblemDetails : HeadlessProblemDetails;

    public sealed class EntityNotFoundProblemDetailsParams
    {
        public required string Entity { get; init; }
        public required string Key { get; init; }
    }

    public sealed class EntityNotFoundProblemDetails : HeadlessProblemDetails
    {
        public required EntityNotFoundProblemDetailsParams Params { get; init; }
    }

    public sealed class ConflictProblemDetails : HeadlessProblemDetails
    {
        public required List<ErrorDescriptor> Errors { get; init; }
    }

    public sealed class UnprocessableEntityProblemDetails : HeadlessProblemDetails
    {
        public required Dictionary<string, List<ErrorDescriptor>> Errors { get; init; }
    }

    #endregion
}
