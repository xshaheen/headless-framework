using FluentValidation;
using FluentValidation.Results;
using Framework.Api.Abstractions;
using Framework.Exceptions;
using Framework.Primitives;

namespace Framework.Api.Demo.Endpoints;

public static class ProblemsEndpoints
{
    public static void MapProblemsEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("minimal").AddExceptionFilter();

        api.MapGet("malformed-syntax", (IProblemDetailsCreator factory) => Results.Problem(factory.MalformedSyntax()));
        api.MapGet("authorized", () => Results.Ok()).RequireAuthorization();

        api.MapPost(
            "entity-not-found",
            () =>
            {
                throw new EntityNotFoundException("Entity", "Key");
            }
        );

        api.MapPost(
            "internal-error",
            () =>
            {
                throw new InvalidOperationException("This is a test exception.");
            }
        );

        api.MapPost(
            "conflict",
            () =>
            {
                throw new ConflictException(new ErrorDescriptor("error-code", "Error message"));
            }
        );

        api.MapPost(
            "unprocessable",
            () =>
            {
                throw new ValidationException([new("Property", "Error message") { ErrorCode = "error-code" }]);
            }
        );
    }
}
