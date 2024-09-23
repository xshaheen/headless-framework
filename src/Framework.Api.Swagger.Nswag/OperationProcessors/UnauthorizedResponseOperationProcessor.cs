// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Api.Core.ApiExplorer;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using NSwag;
using NSwag.Generation.AspNetCore;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Framework.Api.Swagger.Nswag.OperationProcessors;

public sealed class UnauthorizedResponseOperationProcessor : IOperationProcessor
{
    private const string _UnauthorizedStatusCode = "401";

    private static readonly OpenApiResponse _UnauthorizedResponse =
        new()
        {
            Description = "Unauthorized - The user has not supplied the necessary credentials to access the resource.",
        };

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
            !operation.Responses.ContainsKey(_UnauthorizedStatusCode)
            && authorizationRequirements.OfType<DenyAnonymousAuthorizationRequirement>().Any()
        )
        {
            operation.Responses.Add(_UnauthorizedStatusCode, _UnauthorizedResponse);
        }
    }
}
