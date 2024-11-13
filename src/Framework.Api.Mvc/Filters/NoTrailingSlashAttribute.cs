// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api.Abstractions;
using Framework.Kernel.Checks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Api.Mvc.Filters;

/// <summary>
/// Requires that a HTTP request does not contain a trailing slash. If it does, return a 404 Not Found. This is
/// useful if you are dynamically generating something which acts like it's a file on the web server.
/// E.g. /Robots.txt/ should not have a trailing slash and should be /Robots.txt. Note, that we also don't care if
/// it is upper-case or lower-case in this instance.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class NoTrailingSlashAttribute : Attribute, IResourceFilter
{
    private const char _SlashCharacter = '/';

    /// <summary>
    /// Executes the resource filter. Called after execution of the remainder of the pipeline.
    /// </summary>
    /// <param name="context">The <see cref="ResourceExecutedContext" />.</param>
    public void OnResourceExecuted(ResourceExecutedContext context) { }

    /// <summary>
    /// Executes the resource filter. Called before execution of the remainder of the pipeline. Determines whether
    /// a request contains a trailing slash and, if it does, calls the <see cref="_HandleTrailingSlashRequest"/>
    /// method.
    /// </summary>
    /// <param name="context">The <see cref="_HandleTrailingSlashRequest" />.</param>
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        Argument.IsNotNull(context);

        var path = context.HttpContext.Request.Path;

        if (!path.HasValue)
        {
            return;
        }

        if (path.Value[^1] == _SlashCharacter)
        {
            _HandleTrailingSlashRequest(context);
        }
    }

    /// <summary>
    /// Handles HTTP requests that have a trailing slash but are not meant to.
    /// </summary>
    /// <param name="context">The <see cref="ResourceExecutingContext" />.</param>
    private static void _HandleTrailingSlashRequest(ResourceExecutingContext context)
    {
        var creator = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsCreator>();
        var endpointNotFound = creator.EndpointNotFound(context.HttpContext);
        context.Result = new NotFoundObjectResult(endpointNotFound);
    }
}
