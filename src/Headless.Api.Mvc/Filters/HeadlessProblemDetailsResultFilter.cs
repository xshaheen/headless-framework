// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Headless.Api.Filters;

internal sealed class HeadlessProblemDetailsResultFilter(IOptions<ProblemDetailsOptions> options)
    : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (
            context.Result is not ObjectResult { Value: ProblemDetails problemDetails }
            || !_IsHeadlessProblemDetails(problemDetails)
        )
        {
            return;
        }

        options.Value.CustomizeProblemDetails?.Invoke(
            new ProblemDetailsContext { HttpContext = context.HttpContext, ProblemDetails = problemDetails }
        );
    }

    public void OnResultExecuted(ResultExecutedContext context) { }

    private static bool _IsHeadlessProblemDetails(ProblemDetails problemDetails)
    {
        return problemDetails.Extensions.ContainsKey("buildNumber")
            && problemDetails.Extensions.ContainsKey("commitNumber")
            && problemDetails.Extensions.ContainsKey("timestamp");
    }
}
