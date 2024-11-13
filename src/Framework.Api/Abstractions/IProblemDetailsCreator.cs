// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Framework.Api.Constants;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Framework.Api.Abstractions;

public interface IProblemDetailsCreator
{
    ProblemDetails EndpointNotFound(HttpContext context);

    ProblemDetails EntityNotFound(HttpContext context, string entity, string key);

    ProblemDetails MalformedSyntax(HttpContext context);

    ProblemDetails UnprocessableEntity(
        HttpContext context,
        IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>> errorDescriptors
    );

    ProblemDetails Conflict(HttpContext context, IEnumerable<ErrorDescriptor> errorDescriptors);

    ProblemDetails InternalError(HttpContext context, string stackTrace);
}

public sealed class ProblemDetailsCreator(IBuildInformationAccessor buildInformationAccessor, IClock clock)
    : IProblemDetailsCreator
{
    public ProblemDetails EndpointNotFound(HttpContext context)
    {
        return new ProblemDetails
        {
            Type = $"/errors/{ProblemDetailTitles.EndpointNotFounded}",
            Title = ProblemDetailTitles.EndpointNotFounded,
            Status = StatusCodes.Status404NotFound,
            Detail = $"The requested endpoint '{context.Request.Path}' was not found.",
            Instance = context.Request.Path.Value ?? "",
            Extensions =
            {
                ["timestamp"] = clock.UtcNow.ToString("O"),
                ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier,
                ["buildNumber"] = buildInformationAccessor.GetBuildNumber(),
                ["commitNumber"] = buildInformationAccessor.GetCommitNumber(),
            },
        };
    }

    public ProblemDetails EntityNotFound(HttpContext context, string entity, string key)
    {
        return new ProblemDetails
        {
            Type = $"/errors/{ProblemDetailTitles.EntityNotFounded}",
            Status = StatusCodes.Status404NotFound,
            Title = ProblemDetailTitles.EntityNotFounded,
            Detail = $"The requested entity does not exist. There is no entity matches '{entity}:{key}'.",
            Instance = context.Request.Path.Value ?? "",
            Extensions =
            {
                ["timestamp"] = clock.UtcNow.ToString("O"),
                ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier,
                ["params"] = new { entity, key },
                ["buildNumber"] = buildInformationAccessor.GetBuildNumber(),
                ["commitNumber"] = buildInformationAccessor.GetCommitNumber(),
            },
        };
    }

    public ProblemDetails MalformedSyntax(HttpContext context)
    {
        return new ProblemDetails
        {
            Type = $"/errors/{ProblemDetailTitles.BadRequest}",
            Title = ProblemDetailTitles.BadRequest,
            Status = StatusCodes.Status400BadRequest,
            Detail =
                "Failed to parse. The request body is empty or could not be understood by the server due to malformed syntax.",
            Instance = context.Request.Path.Value ?? "",
            Extensions =
            {
                ["timestamp"] = clock.UtcNow.ToString("O"),
                ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier,
                ["buildNumber"] = buildInformationAccessor.GetBuildNumber(),
                ["commitNumber"] = buildInformationAccessor.GetCommitNumber(),
            },
        };
    }

    public ProblemDetails UnprocessableEntity(
        HttpContext context,
        IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>> errorDescriptors
    )
    {
        return new ProblemDetails
        {
            Type = $"/errors/{ProblemDetailTitles.ValidationProblem}",
            Title = ProblemDetailTitles.ValidationProblem,
            Status = StatusCodes.Status422UnprocessableEntity,
            Detail = "One or more validation errors occurred.",
            Instance = context.Request.Path.Value ?? "",
            Extensions =
            {
                ["errors"] = errorDescriptors,
                ["timestamp"] = clock.UtcNow.ToString("O"),
                ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier,
                ["buildNumber"] = buildInformationAccessor.GetBuildNumber(),
                ["commitNumber"] = buildInformationAccessor.GetCommitNumber(),
            },
        };
    }

    public ProblemDetails Conflict(HttpContext context, IEnumerable<ErrorDescriptor> errorDescriptors)
    {
        return new ProblemDetails
        {
            Type = $"/errors/{ProblemDetailTitles.ConflictRequest}",
            Status = StatusCodes.Status409Conflict,
            Title = ProblemDetailTitles.ConflictRequest,
            Detail = "Conflict request",
            Instance = context.Request.Path.Value ?? "",
            Extensions =
            {
                ["errors"] = errorDescriptors,
                ["timestamp"] = clock.UtcNow.ToString("O"),
                ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier,
                ["buildNumber"] = buildInformationAccessor.GetBuildNumber(),
                ["commitNumber"] = buildInformationAccessor.GetCommitNumber(),
            },
        };
    }

    public ProblemDetails InternalError(HttpContext context, string stackTrace)
    {
        return new ProblemDetails
        {
            Type = $"/errors/{ProblemDetailTitles.UnhandledException}",
            Title = ProblemDetailTitles.UnhandledException,
            Status = StatusCodes.Status500InternalServerError,
            Detail = "An error occurred while processing your request.",
            Instance = context.Request.Path.Value ?? "",
            Extensions =
            {
                ["timestamp"] = clock.UtcNow.ToString("O"),
                ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier,
                ["stackTrace"] = stackTrace,
                ["buildNumber"] = buildInformationAccessor.GetBuildNumber(),
                ["commitNumber"] = buildInformationAccessor.GetCommitNumber(),
            },
        };
    }
}
