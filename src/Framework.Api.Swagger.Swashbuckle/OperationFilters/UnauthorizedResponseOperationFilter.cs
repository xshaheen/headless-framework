// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api.ApiExplorer;
using Framework.Kernel.Checks;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Framework.Api.Swagger.Swashbuckle.OperationFilters;

/// <summary>
/// Adds a 401 Unauthorized response to the Swagger response documentation when the authorization policy contains a
/// <see cref="DenyAnonymousAuthorizationRequirement"/>.
/// </summary>
/// <seealso cref="IOperationFilter" />
public sealed class UnauthorizedResponseOperationFilter : IOperationFilter
{
    private const string _UnauthorizedStatusCode = "401";

    private static readonly OpenApiResponse _UnauthorizedResponse =
        new()
        {
            Description = "Unauthorized - The user has not supplied the necessary credentials to access the resource.",
        };

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        Argument.IsNotNull(operation);
        Argument.IsNotNull(context);

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
