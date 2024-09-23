// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Api.Core.ApiExplorer;
using Framework.Kernel.Checks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Framework.Api.Swagger.Swashbuckle.OperationFilters;

/// <summary>
/// Adds a 403 Forbidden response to the Swagger response documentation when the authorization policy contains a
/// <see cref="ClaimsAuthorizationRequirement"/>, <see cref="NameAuthorizationRequirement"/>,
/// <see cref="OperationAuthorizationRequirement"/>, <see cref="RolesAuthorizationRequirement"/> or
/// <see cref="AssertionRequirement"/>.
/// </summary>
/// <seealso cref="IOperationFilter" />
public sealed class ForbiddenResponseOperationFilter : IOperationFilter
{
    private const string _ForbiddenStatusCode = "403";

    private static readonly OpenApiResponse _ForbiddenResponse =
        new() { Description = "Forbidden - The user does not have the necessary permissions to access the resource." };

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        Argument.IsNotNull(operation);
        Argument.IsNotNull(context);

        var filterDescriptors = context.ApiDescription.ActionDescriptor.FilterDescriptors;
        var authorizationRequirements = filterDescriptors.GetPolicyRequirements();

        if (
            !operation.Responses.ContainsKey(_ForbiddenStatusCode)
            && _HasAuthorizationRequirement(authorizationRequirements)
        )
        {
            operation.Responses.Add(_ForbiddenStatusCode, _ForbiddenResponse);
        }
    }

    private static bool _HasAuthorizationRequirement(IEnumerable<IAuthorizationRequirement> authorizationRequirements)
    {
        foreach (var authorizationRequirement in authorizationRequirements)
        {
            if (
                authorizationRequirement
                is ClaimsAuthorizationRequirement
                    or NameAuthorizationRequirement
                    or OperationAuthorizationRequirement
                    or RolesAuthorizationRequirement
                    or AssertionRequirement
            )
            {
                return true;
            }
        }

        return false;
    }
}
