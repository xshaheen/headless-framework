// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api.Abstractions;
using Framework.Checks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Api.Filters;

/// <summary>
/// Requires that an HTTP request does not contain a trailing slash. If it does, return a 404 Not Found. This is
/// useful if you are dynamically generating something which acts like it's a file on the web server.
/// E.g. /Robots.txt/ should not have a trailing slash and should be /Robots.txt. Note, that we also don't care if
/// it is upper-case or lower-case in this instance.
/// </summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class NoTrailingSlashAttribute : Attribute, IAsyncResourceFilter
{
    private const char _SlashCharacter = '/';

    public Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        Argument.IsNotNull(context);

        var path = context.HttpContext.Request.Path;

        if (!path.HasValue || path.Value[^1] != _SlashCharacter)
        {
            return next();
        }

        return _HandleTrailingSlashRequest(context);
    }

    private static async Task _HandleTrailingSlashRequest(ResourceExecutingContext context)
    {
        var serviceProvider = context.HttpContext.RequestServices;
        var factory = serviceProvider.GetRequiredService<IProblemDetailsCreator>();
        var problemDetails = factory.EndpointNotFound();
        await Results.Problem(problemDetails).ExecuteAsync(context.HttpContext);
    }
}
