// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Headless.Api;

/// <summary>
/// Refuses a request to an MVC controller or action unless it comes from the API's own origin or a trusted one, as
/// <see cref="HeadlessSameOriginOptions"/> configures, answering 403 with <c>g:cross_site_request</c>. Apply it to an
/// endpoint that authenticates through a cookie, such as a refresh or logout action.
/// </summary>
/// <remarks>
/// The attribute is an MVC authorization filter, so it runs before model binding. On a Minimal API endpoint or route
/// group, call <c>RequireSameOrigin()</c> instead: an attribute on a route handler is metadata only and checks
/// nothing.
/// </remarks>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class RequireSameOriginAttribute : Attribute, IAuthorizationFilter
{
    /// <inheritdoc />
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var problem = SameOriginRequestValidator.Validate(context.HttpContext);

        if (problem is not null)
        {
            context.Result = new ObjectResult(problem) { StatusCode = problem.Status };
        }
    }
}
