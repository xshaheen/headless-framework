// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.ApiExplorer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using NSwag;
using NSwag.Generation.AspNetCore;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Headless.Api.OperationProcessors;

/// <summary>
/// NSwag operation processor that adds a 401 Unauthorized response entry to operations that require
/// authentication.
/// </summary>
/// <remarks>
/// <para>
/// The 401 response is added when all of the following conditions hold:
/// <list type="bullet">
///   <item><description>
///     The operation context is an <c>AspNetCoreOperationProcessorContext</c>.
///   </description></item>
///   <item><description>A 401 response is not already present in the operation.</description></item>
///   <item><description>The endpoint does not carry <c>[AllowAnonymous]</c>.</description></item>
///   <item><description>
///     The endpoint's policy requirements include a <c>DenyAnonymousAuthorizationRequirement</c>, OR the
///     endpoint metadata contains at least one <c>[Authorize]</c> attribute.
///   </description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class UnauthorizedResponseOperationProcessor : IOperationProcessor
{
    private static readonly OpenApiResponse _UnauthorizedResponse = _CreateUnauthorizedResponse();

    /// <summary>
    /// Conditionally adds a 401 Unauthorized response to the current operation.
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
