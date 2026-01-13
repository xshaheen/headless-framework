// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api.ApiExplorer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using NSwag;
using NSwag.Generation.AspNetCore;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Framework.Api.OperationProcessors;

public sealed class UnauthorizedResponseOperationProcessor : IOperationProcessor
{
    private static readonly OpenApiResponse _UnauthorizedResponse = _CreateUnauthorizedResponse();

    public bool Process(OperationProcessorContext context)
    {
        if (context is not AspNetCoreOperationProcessorContext ctx)
        {
            return true;
        }

        var responses = ctx.OperationDescription.Operation.Responses;

        if (responses.ContainsKey(OpenApiStatusCodes.Unauthorized))
        {
            return true;
        }

        var actionDescriptor = ctx.ApiDescription.ActionDescriptor;
        var authorizationRequirements = actionDescriptor.FilterDescriptors.GetPolicyRequirements();

        if (actionDescriptor.EndpointMetadata.OfType<AllowAnonymousAttribute>().Any())
        {
            return true;
        }

        if (
            authorizationRequirements.OfType<DenyAnonymousAuthorizationRequirement>().Any()
            || actionDescriptor.EndpointMetadata.OfType<AuthorizeAttribute>().Any()
        )
        {
            responses.Add(OpenApiStatusCodes.Unauthorized, _UnauthorizedResponse);
        }

        return true;
    }

    private static OpenApiResponse _CreateUnauthorizedResponse()
    {
        return new()
        {
            Description = "Unauthorized - The user has not supplied the necessary credentials to access the resource.",
        };
    }
}
