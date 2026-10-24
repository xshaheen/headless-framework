// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Headless.Api.Options;

/// <summary>
/// Answers 400 <c>g:invalid_parameter_value</c> when a request value could not be bound, naming the first value that
/// failed, instead of running the action with that parameter left at its default.
/// </summary>
/// <remarks>
/// Headless turns off the automatic <c>[ApiController]</c> 400 and clears MVC's validator providers, leaving rule
/// validation to FluentValidation. Model state then holds only binding failures: an unknown enum name, text where a
/// number belongs, a malformed or missing body. Without this filter the action ran anyway, so a filter such as
/// <c>?status=bogus</c> was silently ignored and the endpoint answered as if it had not been sent. It runs after
/// authorization, so a caller who may not reach the endpoint still gets 401 or 403.
/// </remarks>
internal sealed class ModelBindingFailureFilter(IProblemDetailsCreator problemDetailsCreator) : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid)
        {
            return;
        }

        var parameter = context
            .ModelState.Where(x => x.Value?.Errors.Count > 0)
            .Select(x => x.Key)
            .FirstOrDefault(key => !string.IsNullOrEmpty(key));

        var error = GeneralMessageDescriber.InvalidParameterValue(parameter ?? "body");

        context.Result = new ObjectResult(problemDetailsCreator.BadRequest(error.Description, error))
        {
            StatusCode = StatusCodes.Status400BadRequest,
        };
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
