using Framework.Kernel.BuildingBlocks;
using Framework.Kernel.Checks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Framework.Api.Swagger.Swashbuckle.OperationFilters;

/// <summary>Shows an example of a <see cref="ProblemDetails"/> containing errors.</summary>
/// <seealso cref="IOperationFilter" />
public sealed class ProblemDetailsOperationFilter : IOperationFilter
{
    #region ProblemDetails

    private const string _StatusCode400 = "400";
    private const string _StatusCode401 = "401";
    private const string _StatusCode403 = "403";
    private const string _StatusCode404 = "404";
    private const string _StatusCode406 = "406";
    private const string _StatusCode409 = "409";
    private const string _StatusCode415 = "415";
    private const string _StatusCode422 = "422";
    private const string _StatusCode500 = "500";

    /// <summary>
    /// Gets the 400 Bad Request example of a <see cref="ProblemDetails"/> response.
    /// </summary>
    public static OpenApiObject Status400ProblemDetails { get; } =
        new()
        {
            ["type"] = new OpenApiString("https://tools.ietf.org/html/rfc7231#section-6.5.1"),
            ["title"] = new OpenApiString("Bad Request"),
            ["status"] = new OpenApiInteger(StatusCodes.Status400BadRequest),
            ["traceId"] = new OpenApiString("00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00"),
            ["errors"] = new OpenApiObject
            {
                ["exampleProperty1"] = new OpenApiArray { new OpenApiString("The property field is required") },
            },
        };

    /// <summary>
    /// Gets the 401 Unauthorized example of a <see cref="ProblemDetails"/> response.
    /// </summary>
    public static OpenApiObject Status401ProblemDetails { get; } =
        new()
        {
            ["type"] = new OpenApiString("https://tools.ietf.org/html/rfc7235#section-3.1"),
            ["title"] = new OpenApiString("Unauthorized"),
            ["status"] = new OpenApiInteger(StatusCodes.Status401Unauthorized),
            ["traceId"] = new OpenApiString("00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00"),
        };

    /// <summary>
    /// Gets the 403 Forbidden example of a <see cref="ProblemDetails"/> response.
    /// </summary>
    public static OpenApiObject Status403ProblemDetails { get; } =
        new()
        {
            ["type"] = new OpenApiString("https://tools.ietf.org/html/rfc7231#section-6.5.3"),
            ["title"] = new OpenApiString("Forbidden"),
            ["status"] = new OpenApiInteger(StatusCodes.Status403Forbidden),
            ["traceId"] = new OpenApiString("00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00"),
        };

    /// <summary>
    /// Gets the 404 Not Found example of a <see cref="ProblemDetails"/> response.
    /// </summary>
    public static OpenApiObject Status404ProblemDetails { get; } =
        new()
        {
            ["type"] = new OpenApiString("https://tools.ietf.org/html/rfc7231#section-6.5.4"),
            ["title"] = new OpenApiString("Not Found"),
            ["status"] = new OpenApiInteger(StatusCodes.Status404NotFound),
            ["traceId"] = new OpenApiString("00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00"),
        };

    /// <summary>
    /// Gets the 406 Not Acceptable example of a <see cref="ProblemDetails"/> response.
    /// </summary>
    public static OpenApiObject Status406ProblemDetails { get; } =
        new()
        {
            ["type"] = new OpenApiString("https://tools.ietf.org/html/rfc7231#section-6.5.6"),
            ["title"] = new OpenApiString("Not Acceptable"),
            ["status"] = new OpenApiInteger(StatusCodes.Status406NotAcceptable),
            ["traceId"] = new OpenApiString("00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00"),
        };

    /// <summary>
    /// Gets the 409 Conflict example of a <see cref="ProblemDetails"/> response.
    /// </summary>
    public static OpenApiObject Status409ProblemDetails { get; } =
        new()
        {
            ["type"] = new OpenApiString("https://tools.ietf.org/html/rfc7231#section-6.5.8"),
            ["title"] = new OpenApiString("Conflict"),
            ["status"] = new OpenApiInteger(StatusCodes.Status409Conflict),
            ["traceId"] = new OpenApiString("00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00"),
        };

    /// <summary>
    /// Gets the 415 Unsupported Media Type example of a <see cref="ProblemDetails"/> response.
    /// </summary>
    public static OpenApiObject Status415ProblemDetails { get; } =
        new()
        {
            ["type"] = new OpenApiString("https://tools.ietf.org/html/rfc7231#section-6.5.13"),
            ["title"] = new OpenApiString("Unsupported Media Type"),
            ["status"] = new OpenApiInteger(StatusCodes.Status415UnsupportedMediaType),
            ["traceId"] = new OpenApiString("00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00"),
        };

    /// <summary>
    /// Gets the 422 Unprocessable Entity example of a <see cref="ProblemDetails"/> response.
    /// </summary>
    public static OpenApiObject Status422ProblemDetails { get; } =
        new()
        {
            ["type"] = new OpenApiString("https://tools.ietf.org/html/rfc4918#section-11.2"),
            ["title"] = new OpenApiString("Unprocessable Entity"),
            ["status"] = new OpenApiInteger(StatusCodes.Status422UnprocessableEntity),
            ["traceId"] = new OpenApiString("00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00"),
        };

    /// <summary>
    /// Gets the 500 Internal Server Error example of a <see cref="ProblemDetails"/> response.
    /// </summary>
    public static OpenApiObject Status500ProblemDetails { get; } =
        new()
        {
            ["type"] = new OpenApiString("https://tools.ietf.org/html/rfc7231#section-6.6.1"),
            ["title"] = new OpenApiString("Internal Server Error"),
            ["status"] = new OpenApiInteger(StatusCodes.Status500InternalServerError),
            ["traceId"] = new OpenApiString("00-982607166a542147b435be3a847ddd71-fc75498eb9f09d48-00"),
        };

    #endregion

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        Argument.IsNotNull(operation);
        Argument.IsNotNull(context);

        foreach (var (statusCode, response) in operation.Responses)
        {
            _SetExampleResponseForKnownStatus(statusCode, response);
        }
    }

    private static void _SetExampleResponseForKnownStatus(string statusCode, OpenApiResponse response)
    {
        switch (statusCode)
        {
            case _StatusCode400:
                _SetDefaultAndExample(response, Status400ProblemDetails);

                break;
            case _StatusCode401:
                _SetDefaultAndExample(response, Status401ProblemDetails);

                break;
            case _StatusCode403:
                _SetDefaultAndExample(response, Status403ProblemDetails);

                break;
            case _StatusCode404:
                _SetDefaultAndExample(response, Status404ProblemDetails);

                break;
            case _StatusCode406:
                _SetDefaultAndExample(response, Status406ProblemDetails);

                break;
            case _StatusCode409:
                _SetDefaultAndExample(response, Status409ProblemDetails);

                break;
            case _StatusCode415:
                _SetDefaultAndExample(response, Status415ProblemDetails);

                break;
            case _StatusCode422:
                _SetDefaultAndExample(response, Status422ProblemDetails);

                break;
            case _StatusCode500:
                _SetDefaultAndExample(response, Status500ProblemDetails);

                break;
        }
    }

    private static void _SetDefaultAndExample(OpenApiResponse value, IOpenApiAny problemDetails)
    {
        if (value.Content is null)
        {
            return;
        }

        if (value.Content.TryGetValue(ContentTypes.Applications.ProblemJson, out var problemJsonMediaType))
        {
            problemJsonMediaType.Example = problemDetails;
        }

        if (value.Content.TryGetValue(ContentTypes.Applications.ProblemXml, out var problemXmlMediaType))
        {
            problemXmlMediaType.Example = problemDetails;
        }
    }
}
