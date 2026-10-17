// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using Headless.Api.Resources;
using Headless.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Constants = Headless.Api.HeadlessProblemDetailsConstants;

namespace Headless.OpenApi;

/// <summary>
/// Describes the body of each known error response (400, 401, 403, 404, 408, 409, 413, 422, 428, 429, 500, 501, 503)
/// with its typed Headless problem-details schema and a worked example under <c>application/problem+json</c>, and the
/// <c>Retry-After</c> header of 429 and 503.
/// </summary>
/// <remarks>
/// It types only responses the operation already declares, so it runs after the transformers that add the 401, 403,
/// 400, 409, 428, and 429 responses. The problem types are registered as shared components, so every operation
/// references one schema per status. A 400 the endpoint declared as <see cref="HttpValidationProblemDetails"/> gets
/// <see cref="BadRequestValidationProblemDetails"/>, the shape the validation filter writes. Other media types the
/// endpoint declared for the response keep their schema. The examples use the camelCase member names of the Headless web
/// JSON options; a test checks each one against its schema.
/// </remarks>
internal sealed class ProblemDetailsTransformer : IOpenApiOperationTransformer
{
    private const string _RetryAfterHeader = "Retry-After";

    public async Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        if (operation.Responses is not { Count: > 0 } responses)
        {
            return;
        }

        foreach (var (statusCode, response) in responses)
        {
            if (response is not OpenApiResponse concrete || !_Problems.TryGetValue(statusCode, out var problem))
            {
                continue;
            }

            if (
                string.Equals(statusCode, OpenApiStatusCodes.BadRequest, StringComparison.Ordinal)
                && _DeclaresValidationProblem(context)
            )
            {
                problem = _ValidationProblem;
            }

            IOpenApiSchema schema = await context.GetOrCreateSchemaAsync(
                problem.SchemaType,
                parameterDescription: null,
                cancellationToken
            );

            if (context.Document is { } document)
            {
                schema = OpenApiSchemaComponents.Resolve(document, schema);
            }

            var content = concrete.Content ??= new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal);
            content.TryAdd(ContentTypes.Applications.ProblemJson, new OpenApiMediaType());

            var mediaType = content[ContentTypes.Applications.ProblemJson];
            mediaType.Schema = schema;
            // A JSON node belongs to one parent, so every response gets its own copy.
            mediaType.Example = problem.Example.DeepClone();

            switch (statusCode)
            {
                case OpenApiStatusCodes.TooManyRequests:
                    _AddRetryAfterHeader(concrete, required: true);
                    break;
                case OpenApiStatusCodes.ServiceUnavailable:
                    _AddRetryAfterHeader(concrete, required: false);
                    break;
            }
        }
    }

    // ProducesValidationProblem() and the AddValidation() filter declare the native validation problem, whose errors are
    // plain messages; the Headless 400 shape would describe a body the endpoint never writes.
    private static bool _DeclaresValidationProblem(OpenApiOperationTransformerContext context)
    {
        return context.Description.SupportedResponseTypes.Any(responseType =>
            responseType.StatusCode == StatusCodes.Status400BadRequest
            && responseType.Type is { } type
            && typeof(HttpValidationProblemDetails).IsAssignableFrom(type)
        );
    }

    // Both writers of a 429 (the rate limiter rejection and TooManyRequestsException) always send the header; a 503
    // sends it only when the server knows when to retry.
    private static void _AddRetryAfterHeader(OpenApiResponse response, bool required)
    {
        var headers = response.Headers ??= new Dictionary<string, IOpenApiHeader>(StringComparer.OrdinalIgnoreCase);

        if (headers.Keys.Any(name => string.Equals(name, _RetryAfterHeader, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        headers[_RetryAfterHeader] = new OpenApiHeader
        {
            Description = "The number of seconds to wait before retrying the request.",
            Required = required,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
        };
    }

    #region Examples

    // The example text is English on purpose: a document can be cached and served to every reader, so text resolved
    // under the culture of whichever request generated it first would reach readers of every other culture.

    private static readonly Dictionary<string, (Type SchemaType, JsonObject Example)> _Problems = new(
        StringComparer.Ordinal
    )
    {
        [OpenApiStatusCodes.BadRequest] = (
            typeof(BadRequestProblemDetails),
            _Example(
                Constants.Types.BadRequest,
                Constants.Titles.BadRequest,
                StatusCodes.Status400BadRequest,
                "The request could not be read. Its body is empty or malformed."
            )
        ),
        [OpenApiStatusCodes.Unauthorized] = (
            typeof(UnauthorizedProblemDetails),
            _Example(
                Constants.Types.Unauthorized,
                Constants.Titles.Unauthorized,
                StatusCodes.Status401Unauthorized,
                "You must be authenticated to access this resource."
            )
        ),
        [OpenApiStatusCodes.Forbidden] = (
            typeof(ForbiddenProblemDetails),
            _Example(
                Constants.Types.Forbidden,
                Constants.Titles.Forbidden,
                StatusCodes.Status403Forbidden,
                "You do not have permission to access this resource."
            )
        ),
        [OpenApiStatusCodes.NotFound] = (
            typeof(EntityNotFoundProblemDetails),
            _Example(
                Constants.Types.EntityNotFound,
                Constants.Titles.EntityNotFound,
                StatusCodes.Status404NotFound,
                "The requested resource was not found."
            )
        ),
        [OpenApiStatusCodes.Conflict] = (
            typeof(ConflictProblemDetails),
            _Example(
                Constants.Types.Conflict,
                Constants.Titles.Conflict,
                StatusCodes.Status409Conflict,
                "The request violates one or more business rules.",
                ("errors", new JsonArray(Error("business_error", "Some business rule failed.")))
            )
        ),
        [OpenApiStatusCodes.UnprocessableEntity] = (
            typeof(UnprocessableEntityProblemDetails),
            _Example(
                Constants.Types.UnprocessableEntity,
                Constants.Titles.UnprocessableEntity,
                StatusCodes.Status422UnprocessableEntity,
                "One or more validation errors occurred.",
                (
                    "errors",
                    new JsonObject
                    {
                        ["email"] = new JsonArray(
                            Error("auth:invalid_email_format", "The email address format is invalid."),
                            Error("auth:email_already_exists", "The specified email address is already in use.")
                        ),
                        ["username"] = new JsonArray(
                            Error("auth:username_too_short", "The username must be at least 6 characters long.")
                        ),
                    }
                )
            )
        ),
        // The runtime 428 (IfMatchRequestValidator) has no type or title constant, so this mirrors its literals.
        [OpenApiStatusCodes.PreconditionRequired] = (
            typeof(PreconditionRequiredProblemDetails),
            _Example(
                "about:blank",
                "Precondition Required",
                StatusCodes.Status428PreconditionRequired,
                "A strong If-Match entity tag is required.",
                ("error", Error(GeneralErrorCodes.IfMatchRequired, "A strong If-Match entity tag is required."))
            )
        ),
        [OpenApiStatusCodes.TooManyRequests] = (
            typeof(TooManyRequestsProblemDetails),
            _Example(
                Constants.Types.TooManyRequests,
                Constants.Titles.TooManyRequests,
                StatusCodes.Status429TooManyRequests,
                "Too many requests. Try again later.",
                ("retryAfter", 60)
            )
        ),
        [OpenApiStatusCodes.RequestTimeout] = (
            typeof(RequestTimeoutProblemDetails),
            _Example(
                Constants.Types.RequestTimeout,
                Constants.Titles.RequestTimeout,
                StatusCodes.Status408RequestTimeout,
                "The request timed out."
            )
        ),
        [OpenApiStatusCodes.PayloadTooLarge] = (
            typeof(PayloadTooLargeProblemDetails),
            _Example(
                Constants.Types.PayloadTooLarge,
                Constants.Titles.PayloadTooLarge,
                StatusCodes.Status413PayloadTooLarge,
                "The request payload is too large to process."
            )
        ),
        [OpenApiStatusCodes.InternalServerError] = (
            typeof(InternalServerErrorProblemDetails),
            _Example(
                Constants.Types.InternalError,
                Constants.Titles.InternalError,
                StatusCodes.Status500InternalServerError,
                "An error occurred while processing your request."
            )
        ),
        [OpenApiStatusCodes.NotImplemented] = (
            typeof(NotImplementedProblemDetails),
            _Example(
                Constants.Types.NotImplemented,
                Constants.Titles.NotImplemented,
                StatusCodes.Status501NotImplemented,
                "This functionality is not implemented."
            )
        ),
        [OpenApiStatusCodes.ServiceUnavailable] = (
            typeof(ServiceUnavailableProblemDetails),
            _Example(
                Constants.Types.ServiceUnavailable,
                Constants.Titles.ServiceUnavailable,
                StatusCodes.Status503ServiceUnavailable,
                "The service is temporarily unavailable. Try again later.",
                ("retryAfter", 30)
            )
        ),
    };

    // The native validation problem keeps its own title and carries no detail; the Headless normalization adds the rest.
    private static readonly (Type SchemaType, JsonObject Example) _ValidationProblem = (
        typeof(BadRequestValidationProblemDetails),
        _WithoutDetail(
            _Example(
                Constants.Types.BadRequest,
                "One or more validation errors occurred.",
                StatusCodes.Status400BadRequest,
                detail: "",
                (
                    "errors",
                    new JsonObject
                    {
                        ["name"] = new JsonArray("The name field is required."),
                        ["email"] = new JsonArray("The email field is not a valid e-mail address."),
                    }
                )
            )
        )
    );

    internal static JsonObject Error(string code, string description)
    {
        return new JsonObject { ["code"] = code, ["description"] = description };
    }

    private static JsonObject _WithoutDetail(JsonObject example)
    {
        example.Remove("detail");

        return example;
    }

    private static JsonObject _Example(
        string type,
        string title,
        int status,
        string detail,
        params (string Name, JsonNode? Value)[] members
    )
    {
        var example = new JsonObject
        {
            ["type"] = type,
            ["title"] = title,
            ["status"] = status,
            ["detail"] = detail,
            ["instance"] = "/public/some-endpoint",
        };

        foreach (var (name, value) in members)
        {
            example[name] = value;
        }

        example["traceId"] = "<trace-id>";
        example["buildNumber"] = "<version>";
        example["commitNumber"] = "<commit>";
        example["timestamp"] = "2024-01-01T12:00:00+00:00";

        return example;
    }

    #endregion
}
