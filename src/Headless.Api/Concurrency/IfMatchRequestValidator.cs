// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Headless.Api;

internal static class IfMatchRequestValidator
{
    public static ProblemDetails? Validate(HttpContext context)
    {
        var value = context.Request.Headers[HeaderNames.IfMatch];
        if (value.Count == 0)
        {
            var required = GeneralMessageDescriber.IfMatchRequired();
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status428PreconditionRequired,
                Title = "Precondition Required",
                Detail = required.Description,
                Extensions = { ["error"] = required },
            };
            context.RequestServices.GetRequiredService<IProblemDetailsCreator>().Normalize(problem);
            return problem;
        }

        if (value.Count != 1 || !EntityTag.TryParse(value[0], out var entityTag) || entityTag.IsWeak)
        {
            var invalid = GeneralMessageDescriber.IfMatchInvalid();

            return context
                .RequestServices.GetRequiredService<IProblemDetailsCreator>()
                .BadRequest(invalid.Description, invalid);
        }

        var options = context.RequestServices.GetRequiredService<IOptions<EntityTagConcurrencyOptions>>().Value;
        if (options.IfMatchValidator is not null && !options.IfMatchValidator(entityTag))
        {
            return context
                .RequestServices.GetRequiredService<IProblemDetailsCreator>()
                .BadRequest(Messages.problem_if_match_unsupported, GeneralMessageDescriber.IfMatchInvalid());
        }

        context.RequestServices.GetRequiredService<IfMatchContext>().EntityTag = entityTag;
        return null;
    }
}
