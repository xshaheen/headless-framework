// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api.ApiExplorer;
using Framework.Checks;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Framework.Api.Swagger.Swashbuckle.OperationFilters;

/// <summary>Adds claims from any authorization policy's <see cref="ClaimsAuthorizationRequirement"/>'s.</summary>
/// <seealso cref="IOperationFilter" />
public sealed class ClaimsOperationFilter : IOperationFilter
{
    private const string _OAuth2OpenApiReferenceId = "oauth2";

    private static readonly OpenApiSecurityScheme _OAuth2OpenApiSecurityScheme =
        new()
        {
            Reference = new OpenApiReference { Id = _OAuth2OpenApiReferenceId, Type = ReferenceType.SecurityScheme },
        };

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        Argument.IsNotNull(operation);
        Argument.IsNotNull(context);

        var filterDescriptors = context.ApiDescription.ActionDescriptor.FilterDescriptors;
        var authorizationRequirements = filterDescriptors.GetPolicyRequirements();

        var claimTypes = authorizationRequirements
            .OfType<ClaimsAuthorizationRequirement>()
            .Select(x => x.ClaimType)
            .ToList();

        if (claimTypes.Count != 0)
        {
            operation.Security = [new() { { _OAuth2OpenApiSecurityScheme, claimTypes } }];
        }
    }
}
