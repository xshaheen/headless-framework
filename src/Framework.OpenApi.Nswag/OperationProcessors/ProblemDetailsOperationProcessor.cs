// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api.Abstractions;
using Framework.Checks;
using Framework.Constants;
using Framework.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NJsonSchema;
using NSwag;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Framework.OpenApi.Nswag.OperationProcessors;

/// <summary>
/// Shows an example of a ProblemDetails containing errors for known status codes in NSwag.
/// </summary>
public sealed class ProblemDetailsOperationProcessor : IOperationProcessor
{
    private readonly HeadlessProblemDetails _status400ProblemDetails = new()
    {
        Type = ProblemDetailsConstants.Types.BadRequest,
        Title = ProblemDetailsConstants.Titles.BadRequest,
        Status = StatusCodes.Status400BadRequest,
        Detail = ProblemDetailsConstants.Details.BadRequest,
        Instance = "/public/some-endpoint",
        TraceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
        BuildNumber = "1.0.0",
        CommitNumber = "abc123def",
        Timestamp = DateTimeOffset.UtcNow,
    };

    private readonly HeadlessProblemDetails _status401ProblemDetails = new()
    {
        Type = ProblemDetailsConstants.Types.Unauthorized,
        Title = ProblemDetailsConstants.Titles.Unauthorized,
        Status = StatusCodes.Status401Unauthorized,
        Detail = ProblemDetailsConstants.Details.Unauthorized,
        Instance = "/public/some-endpoint",
        TraceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
        BuildNumber = "1.0.0",
        CommitNumber = "abc123def",
        Timestamp = DateTimeOffset.UtcNow,
    };

    private readonly HeadlessProblemDetails _status403ProblemDetails = new()
    {
        Type = ProblemDetailsConstants.Types.Forbidden,
        Title = ProblemDetailsConstants.Titles.Forbidden,
        Status = StatusCodes.Status403Forbidden,
        Detail = ProblemDetailsConstants.Details.Forbidden,
        Instance = "/public/some-endpoint",
        TraceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
        BuildNumber = "1.0.0",
        CommitNumber = "abc123def",
        Timestamp = DateTimeOffset.UtcNow,
    };

    private readonly EntityNotFoundHeadlessProblemDetails _status404ProblemDetails = new()
    {
        Type = ProblemDetailsConstants.Types.EntityNotFound,
        Title = ProblemDetailsConstants.Titles.EntityNotFound,
        Status = StatusCodes.Status404NotFound,
        Detail = ProblemDetailsConstants.Details.EntityNotFound("User", "user-123"),
        Instance = "/public/some-endpoint",
        TraceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
        BuildNumber = "1.0.0",
        CommitNumber = "abc123def",
        Timestamp = DateTimeOffset.UtcNow,
        Params = new EntityNotFoundHeadlessProblemDetailsParams { Entity = "User", Key = "user-123" },
    };

    private readonly ConflictHeadlessProblemDetails _status409ProblemDetails = new()
    {
        Type = ProblemDetailsConstants.Types.Conflict,
        Title = ProblemDetailsConstants.Titles.Conflict,
        Status = StatusCodes.Status409Conflict,
        Detail = ProblemDetailsConstants.Details.Conflict,
        Instance = "/public/some-endpoint",
        TraceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
        BuildNumber = "1.0.0",
        CommitNumber = "abc123def",
        Timestamp = DateTimeOffset.UtcNow,
        Errors = [new("business_error", @"Some business rule failed.")],
    };

    private readonly UnprocessableEntityHeadlessProblemDetails _status422ProblemDetails = new()
    {
        Type = ProblemDetailsConstants.Types.UnprocessableEntity,
        Title = ProblemDetailsConstants.Titles.UnprocessableEntity,
        Status = StatusCodes.Status422UnprocessableEntity,
        Detail = ProblemDetailsConstants.Details.UnprocessableEntity,
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
                _SetDefaultAndExample(response, _status400ProblemDetails);
                break;
            case "401":
                _SetDefaultAndExample(response, _status401ProblemDetails);
                break;
            case "403":
                _SetDefaultAndExample(response, _status403ProblemDetails);
                break;
            case "404":
                _SetDefaultAndExample(response, _status404ProblemDetails);
                break;
            case "409":
                _SetDefaultAndExample(response, _status409ProblemDetails);
                break;
            case "422":
                _SetDefaultAndExample(response, _status422ProblemDetails);
                break;
        }
    }

    private static void _SetDefaultAndExample(OpenApiResponse response, object problemDetails)
    {
        if (response.Content == null)
        {
            // Cannot set Content if null, so nothing to do
            return;
        }

        // Ensure ProblemJson content type exists
        if (!response.Content.TryGetValue(ContentTypes.Applications.ProblemJson, out var problemJsonMediaType))
        {
            problemJsonMediaType = new OpenApiMediaType { Schema = new JsonSchema { Type = JsonObjectType.Object } };
            response.Content[ContentTypes.Applications.ProblemJson] = problemJsonMediaType;
        }

        problemJsonMediaType.Example = problemDetails;
    }
}

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

public sealed class EntityNotFoundHeadlessProblemDetailsParams
{
    public required string Entity { get; init; }
    public required string Key { get; init; }
}

public sealed class EntityNotFoundHeadlessProblemDetails : HeadlessProblemDetails
{
    public required EntityNotFoundHeadlessProblemDetailsParams Params { get; init; }
}

public sealed class ConflictHeadlessProblemDetails : HeadlessProblemDetails
{
    public required List<ErrorDescriptor> Errors { get; init; }
}

public sealed class UnprocessableEntityHeadlessProblemDetails : HeadlessProblemDetails
{
    public required Dictionary<string, List<ErrorDescriptor>> Errors { get; init; }
}
