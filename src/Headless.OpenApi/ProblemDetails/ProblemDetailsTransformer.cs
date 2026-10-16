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
/// Describes the body of each known error response (400, 401, 403, 404, 409, 422, 428, 429) with its typed Headless
/// problem-details schema and a worked example under <c>application/problem+json</c>.
/// </summary>
/// <remarks>
/// It types only responses the operation already declares, so it runs after the transformers that add the 401, 403,
/// 400, and 428 responses. The problem types are registered as shared components, so every operation references one
/// schema per status. Other media types the endpoint declared for the response keep their schema. The examples use the
/// camelCase member names of the Headless web JSON options; a test checks each one against its schema.
/// </remarks>
internal sealed class ProblemDetailsTransformer : IOpenApiOperationTransformer
{
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
        }
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
    };

    internal static JsonObject Error(string code, string description)
    {
        return new JsonObject { ["code"] = code, ["description"] = description };
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
