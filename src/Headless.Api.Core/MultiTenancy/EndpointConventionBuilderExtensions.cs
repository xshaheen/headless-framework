// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.MultiTenancy;
using Microsoft.AspNetCore.Builder;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api;

/// <summary>
/// Extension members on <see cref="IEndpointConventionBuilder"/> for applying Headless tenancy
/// metadata to Minimal-API endpoints and route groups.
/// </summary>
[PublicAPI]
public static class EndpointConventionBuilderExtensions
{
    extension<TBuilder>(TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        /// <summary>
        /// Adds <see cref="AllowMissingTenantAttribute"/> to the endpoint, permitting requests to
        /// proceed even when no tenant is resolved. Overrides <see cref="RequireTenant"/> when
        /// registered after it (last-wins semantics on the metadata collection).
        /// </summary>
        /// <returns>The same endpoint convention builder.</returns>
        public TBuilder AllowMissingTenant()
        {
            return builder.WithMetadata(new AllowMissingTenantAttribute());
        }

        /// <summary>
        /// Adds <see cref="RequireTenantAttribute"/> to the endpoint, requiring a resolved tenant
        /// for authorization to succeed. Overrides <see cref="AllowMissingTenant"/> when registered
        /// after it (last-wins semantics on the metadata collection).
        /// </summary>
        /// <returns>The same endpoint convention builder.</returns>
        public TBuilder RequireTenant()
        {
            return builder.WithMetadata(new RequireTenantAttribute());
        }

        /// <summary>
        /// Adds <see cref="SkipTenantResolutionAttribute"/> to the endpoint, instructing
        /// <c>TenantResolutionMiddleware</c> to bypass claim-based tenant resolution entirely.
        /// </summary>
        /// <returns>The same endpoint convention builder.</returns>
        public TBuilder SkipTenantResolution()
        {
            return builder.WithMetadata(new SkipTenantResolutionAttribute());
        }
    }
}
