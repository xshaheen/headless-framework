// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api.Options;

/// <summary>
/// The <see cref="ApiBehaviorOptions.InvalidModelStateResponseFactory"/> Headless installs: 400
/// <c>g:invalid_parameter_value</c> naming the first route, query, header, or form value that could not be bound, or
/// the plain bad-request problem when the body could not be read.
/// </summary>
/// <remarks>
/// Headless clears MVC's validator providers and leaves rule validation to FluentValidation, so model state holds only
/// binding failures: an unknown enum name, text where a number belongs, a malformed or missing body. ASP.NET Core's own
/// <c>[ApiController]</c> filter calls this factory only when model state is invalid, after authorization, so a valid
/// request costs nothing and a caller who may not reach the action still gets 401 or 403.
/// </remarks>
internal static class ModelBindingFailureResponse
{
    public static IActionResult Create(ActionContext context)
    {
        string? parameter = null;

        foreach (var (key, entry) in context.ModelState)
        {
            if (entry.Errors.Count > 0 && !_IsBodyKey(key))
            {
                parameter = key;

                break;
            }
        }

        var problemDetailsCreator = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsCreator>();

        // A body that is missing or does not read as JSON is not a parameter value: answer the bad-request problem a
        // Minimal API endpoint answers for the same body.
        var problem = parameter is null
            ? problemDetailsCreator.BadRequest()
            : _InvalidParameter(problemDetailsCreator, parameter);

        return new ObjectResult(problem) { StatusCode = StatusCodes.Status400BadRequest };
    }

    private static ProblemDetails _InvalidParameter(IProblemDetailsCreator problemDetailsCreator, string parameter)
    {
        var error = GeneralMessageDescriber.InvalidParameterValue(parameter);

        return problemDetailsCreator.BadRequest(error.Description, error);
    }

    // The JSON input formatter files a missing body under the empty key and a member it cannot read under its JSON
    // path, which always starts at the document root ("$.status").
    private static bool _IsBodyKey(string key) => key.Length == 0 || key[0] == '$';
}
