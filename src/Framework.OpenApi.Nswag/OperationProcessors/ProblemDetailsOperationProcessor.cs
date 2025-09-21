// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Constants;
using Microsoft.AspNetCore.Http;
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
    private const string _StatusCode400 = "400";
    private const string _StatusCode401 = "401";
    private const string _StatusCode403 = "403";
    private const string _StatusCode404 = "404";
    private const string _StatusCode406 = "406";
    private const string _StatusCode409 = "409";
    private const string _StatusCode415 = "415";
    private const string _StatusCode422 = "422";
    private const string _StatusCode500 = "500";

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

    private static void _SetExampleResponseForKnownStatus(string statusCode, OpenApiResponse response)
    {
        switch (statusCode)
        {
            case _StatusCode400:
                _SetDefaultAndExample(response, _Status400ProblemDetails);
                break;
            case _StatusCode401:
                _SetDefaultAndExample(response, _Status401ProblemDetails);
                break;
            case _StatusCode403:
                _SetDefaultAndExample(response, _Status403ProblemDetails);
                break;
            case _StatusCode404:
                _SetDefaultAndExample(response, _Status404ProblemDetails);
                break;
            case _StatusCode406:
                _SetDefaultAndExample(response, _Status406ProblemDetails);
                break;
            case _StatusCode409:
                _SetDefaultAndExample(response, _Status409ProblemDetails);
                break;
            case _StatusCode415:
                _SetDefaultAndExample(response, _Status415ProblemDetails);
                break;
            case _StatusCode422:
                _SetDefaultAndExample(response, _Status422ProblemDetails);
                break;
            case _StatusCode500:
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

        // Ensure ProblemXml content type exists
        if (!response.Content.TryGetValue(ContentTypes.Applications.ProblemXml, out var problemXmlMediaType))
        {
            problemXmlMediaType = new OpenApiMediaType { Schema = new JsonSchema { Type = JsonObjectType.Object } };
            response.Content[ContentTypes.Applications.ProblemXml] = problemXmlMediaType;
        }
        problemXmlMediaType.Example = problemDetails;
    }

    // Example ProblemDetails objects for each status code
    private static readonly object _Status400ProblemDetails = new
    {
        type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
        title = "Bad Request",
        status = StatusCodes.Status400BadRequest,
        traceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
        errors = new { exampleProperty1 = new[] { "The property field is required" } },
    };
    private static readonly object _Status401ProblemDetails = new
    {
        type = "https://tools.ietf.org/html/rfc7235#section-3.1",
        title = "Unauthorized",
        status = StatusCodes.Status401Unauthorized,
        traceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
    };
    private static readonly object _Status403ProblemDetails = new
    {
        type = "https://tools.ietf.org/html/rfc7231#section-6.5.3",
        title = "Forbidden",
        status = StatusCodes.Status403Forbidden,
        traceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
    };
    private static readonly object _Status404ProblemDetails = new
    {
        type = "https://tools.ietf.org/html/rfc7231#section-6.5.4",
        title = "Not Found",
        status = StatusCodes.Status404NotFound,
        traceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
    };
    private static readonly object _Status406ProblemDetails = new
    {
        type = "https://tools.ietf.org/html/rfc7231#section-6.5.6",
        title = "Not Acceptable",
        status = StatusCodes.Status406NotAcceptable,
        traceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
    };
    private static readonly object _Status409ProblemDetails = new
    {
        type = "https://tools.ietf.org/html/rfc7231#section-6.5.8",
        title = "Conflict",
        status = StatusCodes.Status409Conflict,
        traceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
    };
    private static readonly object _Status415ProblemDetails = new
    {
        type = "https://tools.ietf.org/html/rfc7231#section-6.5.13",
        title = "Unsupported Media Type",
        status = StatusCodes.Status415UnsupportedMediaType,
        traceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
    };
    private static readonly object _Status422ProblemDetails = new
    {
        type = "https://tools.ietf.org/html/rfc4918#section-11.2",
        title = "Unprocessable Entity",
        status = StatusCodes.Status422UnprocessableEntity,
        traceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
    };
    private static readonly object _Status500ProblemDetails = new
    {
        type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
        title = "Internal Server Error",
        status = StatusCodes.Status500InternalServerError,
        traceId = "00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00",
    };
}
