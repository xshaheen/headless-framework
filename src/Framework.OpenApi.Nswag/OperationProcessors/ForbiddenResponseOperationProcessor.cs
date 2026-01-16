// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api.ApiExplorer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using NSwag;
using NSwag.Generation.AspNetCore;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Framework.Api.OperationProcessors;

public sealed class ForbiddenResponseOperationProcessor : IOperationProcessor
{
    private static readonly OpenApiResponse _ForbiddenResponse = _CreateForbiddenResponse();

    public bool Process(OperationProcessorContext context)
    {
        if (context is not AspNetCoreOperationProcessorContext ctx)
        {
            return true;
        }

        var responses = ctx.OperationDescription.Operation.Responses;

        if (responses.ContainsKey(OpenApiStatusCodes.Forbidden))
        {
            return true;
        }

        var actionDescriptor = ctx.ApiDescription.ActionDescriptor;

        if (actionDescriptor.EndpointMetadata.OfType<AllowAnonymousAttribute>().Any())
        {
            return true;
        }

        var authorizeAttribute = actionDescriptor.EndpointMetadata.OfType<AuthorizeAttribute>().ToList();

        if (authorizeAttribute.Exists(attribute => attribute.Policy is not null || attribute.Roles is not null))
        {
            responses.Add(OpenApiStatusCodes.Forbidden, _ForbiddenResponse);

            return true;
        }

        var authorizationRequirements = actionDescriptor.FilterDescriptors.GetPolicyRequirements();

        if (
            authorizationRequirements.Any(requirement =>
                requirement
                    is ClaimsAuthorizationRequirement
                        or NameAuthorizationRequirement
                        or OperationAuthorizationRequirement
                        or RolesAuthorizationRequirement
                        or AssertionRequirement
            )
        )
        {
            responses.Add(OpenApiStatusCodes.Forbidden, _ForbiddenResponse);
        }

        return true;
    }

    private static OpenApiResponse _CreateForbiddenResponse()
    {
        return new()
        {
            Description = "Forbidden - The user does not have the necessary permissions to access the resource.",
        };
    }
}
