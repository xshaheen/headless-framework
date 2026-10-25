// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api.Options;

/// <summary>
/// Answers 400 <c>g:invalid_parameter_value</c> when a route, query, header, or form value could not be bound, naming
/// the first value that failed, and the plain bad-request problem when the body could not be read, instead of running
/// the action with the value left at its default.
/// </summary>
/// <remarks>
/// Headless turns off the automatic <c>[ApiController]</c> 400 and clears MVC's validator providers, leaving rule
/// validation to FluentValidation. Model state then holds only binding failures: an unknown enum name, text where a
/// number belongs, a malformed or missing body. Without this filter the action ran anyway, so a filter such as
/// <c>?status=bogus</c> was silently ignored and the endpoint answered as if it had not been sent. It runs after
/// authorization, so a caller who may not reach the endpoint still gets 401 or 403.
/// </remarks>
internal sealed class ModelBindingFailureFilter : IActionFilter
{
    public static readonly ModelBindingFailureFilter Instance = new();

    private ModelBindingFailureFilter() { }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid)
        {
            return;
        }

        string? parameter = null;

        foreach (var (key, entry) in context.ModelState)
        {
            if (entry.Errors.Count > 0 && !_IsBodyKey(key))
            {
                parameter = key;

                break;
            }
        }

        // A body that is missing or does not read as JSON is not a parameter value: answer the bad-request problem a
        // Minimal API endpoint answers for the same body.
        // Resolved only on the failure path, so a valid request costs nothing here.
        var problemDetailsCreator = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsCreator>();
        var problem = parameter is null
            ? problemDetailsCreator.BadRequest()
            : _InvalidParameter(problemDetailsCreator, parameter);

        context.Result = new ObjectResult(problem) { StatusCode = StatusCodes.Status400BadRequest };
    }

    public void OnActionExecuted(ActionExecutedContext context) { }

    private static ProblemDetails _InvalidParameter(IProblemDetailsCreator problemDetailsCreator, string parameter)
    {
        var error = GeneralMessageDescriber.InvalidParameterValue(parameter);

        return problemDetailsCreator.BadRequest(error.Description, error);
    }

    // The JSON input formatter files a missing body under the empty key and a member it cannot read under its JSON
    // path, which always starts at the document root ("$.status").
    private static bool _IsBodyKey(string key) => key.Length == 0 || key[0] == '$';
}
