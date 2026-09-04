// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Headless.Api.Concurrency;

internal static class IfMatchRequestValidator
{
    public static ProblemDetails? Validate(HttpContext context)
    {
        var value = context.Request.Headers[HeaderNames.IfMatch];
        if (value.Count == 0)
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status428PreconditionRequired,
                Title = "Precondition Required",
                Detail = "A strong If-Match entity tag is required.",
                Extensions = { ["error"] = GeneralMessageDescriber.IfMatchRequired() },
            };
            context.RequestServices.GetRequiredService<IProblemDetailsCreator>().Normalize(problem);
            return problem;
        }

        if (value.Count != 1 || !EntityTag.TryParse(value[0], out var entityTag) || entityTag.IsWeak)
        {
            return context
                .RequestServices.GetRequiredService<IProblemDetailsCreator>()
                .BadRequest(
                    "If-Match must contain exactly one strong entity tag.",
                    GeneralMessageDescriber.IfMatchInvalid()
                );
        }

        context.RequestServices.GetRequiredService<IfMatchContext>().EntityTag = entityTag;
        return null;
    }
}
