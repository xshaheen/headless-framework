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
public sealed class ProblemDetailsOperationProcessor(IProblemDetailsCreator problemDetailsCreator) : IOperationProcessor
{
    private readonly ProblemDetails _status400ProblemDetails = problemDetailsCreator.MalformedSyntax();
    private readonly ProblemDetails _status404ProblemDetails = problemDetailsCreator.EntityNotFound("User", "123");
    private readonly ProblemDetails _status403ProblemDetails = problemDetailsCreator.Forbidden();
    private readonly ProblemDetails _status409ProblemDetails = problemDetailsCreator.Conflict(
        new ErrorDescriptor("business_error", @"Some business rule failed.")
    );

    private readonly ProblemDetails _status422ProblemDetails = problemDetailsCreator.UnprocessableEntity(
        new(StringComparer.Ordinal)
        {
            ["password"] =
            [
                new ErrorDescriptor(
                    "auth:password_requires_digit",
                    @"Passwords must have at least one digit ['0'_'9']."
                ),
            ],
        }
    );

    private static readonly ProblemDetails _Status401ProblemDetails = new()
    {
        Type = "https://tools.ietf.org/html/rfc7235#section-3.1",
        Title = ProblemDetailTitles.Unauthorized,
        Status = StatusCodes.Status401Unauthorized,
        Extensions = { ["traceId"] = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00" },
    };

    private static readonly ProblemDetails _Status406ProblemDetails = new()
    {
        Type = "https://tools.ietf.org/html/rfc7231#section-6.5.6",
        Title = "not-acceptable",
        Status = StatusCodes.Status406NotAcceptable,
        Extensions = { ["traceId"] = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00" },
    };

    private static readonly ProblemDetails _Status415ProblemDetails = new()
    {
        Type = "https://tools.ietf.org/html/rfc7231#section-6.5.13",
        Title = "unsupported-media-type",
        Status = StatusCodes.Status415UnsupportedMediaType,
        Extensions = { ["traceId"] = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00" },
    };

    private static readonly ProblemDetails _Status500ProblemDetails = new()
    {
        Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
        Title = "internal-server-error",
        Status = StatusCodes.Status500InternalServerError,
        Extensions = { ["traceId"] = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00" },
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
                _SetDefaultAndExample(response, _Status401ProblemDetails);
                break;
            case "403":
                _SetDefaultAndExample(response, _status403ProblemDetails);
                break;
            case "404":
                _SetDefaultAndExample(response, _status404ProblemDetails);
                break;
            case "406":
                _SetDefaultAndExample(response, _Status406ProblemDetails);
                break;
            case "409":
                _SetDefaultAndExample(response, _status409ProblemDetails);
                break;
            case "415":
                _SetDefaultAndExample(response, _Status415ProblemDetails);
                break;
            case "422":
                _SetDefaultAndExample(response, _status422ProblemDetails);
                break;
            case "500":
                _SetDefaultAndExample(response, _Status500ProblemDetails);
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
