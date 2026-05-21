// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.MultiTenancy;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

[PublicAPI]
public static class EndpointConventionBuilderExtensions
{
    extension<TBuilder>(TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        public TBuilder AllowMissingTenant()
        {
            return builder.WithMetadata(new AllowMissingTenantAttribute());
        }

        public TBuilder RequireTenant()
        {
            return builder.WithMetadata(new RequireTenantAttribute());
        }

        public TBuilder SkipTenantResolution()
        {
            return builder.WithMetadata(new SkipTenantResolutionAttribute());
        }
    }
}
