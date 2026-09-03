// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Headless.Api.Concurrency;

internal sealed class IfMatchActionFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.ActionDescriptor.EndpointMetadata.OfType<RequireIfMatchAttribute>().Any())
        {
            _ = await next().ConfigureAwait(false);
            return;
        }

        var value = context.HttpContext.Request.Headers[HeaderNames.IfMatch];
        if (value.Count == 0)
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status428PreconditionRequired,
                Title = "Precondition Required",
                Detail = "A strong If-Match entity tag is required.",
                Extensions = { ["error"] = GeneralMessageDescriber.IfMatchRequired() },
            };
            context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsCreator>().Normalize(problem);
            context.Result = new ObjectResult(problem) { StatusCode = problem.Status };
            return;
        }

        if (!EntityTagCodec.TryParseStrong(value.ToString(), out var etag))
        {
            var creator = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsCreator>();
            context.Result = new BadRequestObjectResult(
                creator.BadRequest(
                    "If-Match must contain exactly one strong Base64 entity tag.",
                    GeneralMessageDescriber.IfMatchInvalid()
                )
            );
            return;
        }

        context.HttpContext.RequestServices.GetRequiredService<IfMatchContext>().ETag = etag;
        _ = await next().ConfigureAwait(false);
    }
}
