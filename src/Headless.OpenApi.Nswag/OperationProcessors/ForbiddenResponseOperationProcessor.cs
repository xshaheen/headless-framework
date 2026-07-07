// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.ApiExplorer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using NSwag;
using NSwag.Generation.AspNetCore;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Headless.OpenApi.Nswag.OperationProcessors;

/// <summary>
/// NSwag operation processor that adds a 403 Forbidden response entry to operations whose authorization
/// metadata indicates that role or policy enforcement can produce a forbidden result.
/// </summary>
/// <remarks>
/// <para>
/// The 403 response is added when all of the following conditions hold:
/// <list type="bullet">
///   <item><description>
///     The operation context is an <c>AspNetCoreOperationProcessorContext</c>.
///   </description></item>
///   <item><description>A 403 response is not already present in the operation.</description></item>
///   <item><description>The endpoint does not carry <c>[AllowAnonymous]</c>.</description></item>
///   <item><description>
///     At least one <c>[Authorize]</c> attribute has a non-null <c>Policy</c> or <c>Roles</c>, OR the
///     endpoint's policy requirements include claims-, role-, name-, operation-, or assertion-based
///     requirements.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// Endpoints that require authentication but carry no role or policy constraints (plain <c>[Authorize]</c>)
/// can only produce 401, not 403, so no 403 entry is added for them.
/// </para>
/// </remarks>
public sealed class ForbiddenResponseOperationProcessor : IOperationProcessor
{
    private static readonly OpenApiResponse _ForbiddenResponse = _CreateForbiddenResponse();

    /// <summary>
    /// Conditionally adds a 403 Forbidden response to the current operation.
    /// </summary>
    /// <param name="context">The NSwag operation processor context for the current operation.</param>
    /// <returns>Always <see langword="true"/> so that subsequent processors continue to run.</returns>
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
