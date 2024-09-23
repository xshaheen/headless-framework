// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Api.Core.ApiExplorer;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using NSwag;
using NSwag.Generation.AspNetCore;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Framework.Api.Swagger.Nswag.OperationProcessors;

public sealed class ForbiddenResponseOperationProcessor : IOperationProcessor
{
    private const string _ForbiddenStatusCode = "403";

    private static readonly OpenApiResponse _ForbiddenResponse =
        new() { Description = "Forbidden - The user does not have the necessary permissions to access the resource." };

    public bool Process(OperationProcessorContext context)
    {
        if (context is not AspNetCoreOperationProcessorContext ctx)
        {
            return false;
        }

        _Process(ctx);

        return true;
    }

    private static void _Process(AspNetCoreOperationProcessorContext context)
    {
        var operation = context.OperationDescription.Operation;
        var filterDescriptors = context.ApiDescription.ActionDescriptor.FilterDescriptors;
        var authorizationRequirements = filterDescriptors.GetPolicyRequirements();

        if (
            !operation.Responses.ContainsKey(_ForbiddenStatusCode)
            && authorizationRequirements.Any(requirement =>
                requirement
                    is ClaimsAuthorizationRequirement
                        or NameAuthorizationRequirement
                        or OperationAuthorizationRequirement
                        or RolesAuthorizationRequirement
                        or AssertionRequirement
            )
        )
        {
            operation.Responses.Add(_ForbiddenStatusCode, _ForbiddenResponse);
        }
    }
}
