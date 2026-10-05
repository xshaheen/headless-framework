// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Headless.Api.Features;

/// <summary>Extension members that gate Minimal API endpoints and route groups on features.</summary>
[PublicAPI]
public static class FeaturesEndpointConventionBuilderExtensions
{
    extension<TBuilder>(TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        /// <summary>
        /// Requires at least one of <paramref name="features"/> to be enabled before the endpoint runs; otherwise the
        /// request fails with <see cref="Headless.ConflictException"/> (409 through the Headless exception handler).
        /// </summary>
        /// <param name="features">The feature names, at least one.</param>
        /// <returns>The same endpoint convention builder.</returns>
        /// <remarks>
        /// The <c>[RequiresFeature]</c> attribute alone does not gate a Minimal API handler; call this instead. An
        /// endpoint carrying <see cref="DisableFeatureCheckAttribute"/> metadata skips the check, which lets one endpoint
        /// opt out of a gate on its route group.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="features"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="features"/> is empty.</exception>
        public TBuilder RequireFeatures(params string[] features)
        {
            return builder.RequireFeatures(requiresAll: false, features);
        }

        /// <summary>
        /// Requires <paramref name="features"/> to be enabled before the endpoint runs (all of them when
        /// <paramref name="requiresAll"/> is <see langword="true"/>, otherwise any one); otherwise the request fails with
        /// <see cref="Headless.ConflictException"/> (409 through the Headless exception handler).
        /// </summary>
        /// <param name="requiresAll">
        /// <see langword="true"/> to require every feature; <see langword="false"/> to require at least one.
        /// </param>
        /// <param name="features">The feature names, at least one.</param>
        /// <returns>The same endpoint convention builder.</returns>
        /// <remarks>
        /// The <c>[RequiresFeature]</c> attribute alone does not gate a Minimal API handler; call this instead. An
        /// endpoint carrying <see cref="DisableFeatureCheckAttribute"/> metadata skips the check, which lets one endpoint
        /// opt out of a gate on its route group.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="features"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="features"/> is empty.</exception>
        public TBuilder RequireFeatures(bool requiresAll, params string[] features)
        {
            Argument.IsNotNullOrEmpty(features);

            var requirement = new RequiresFeatureAttribute(features) { IsAnd = requiresAll };

            // The metadata makes the gate discoverable (and enforced by the MVC filter on controller endpoints); the
            // endpoint filter enforces it on Minimal API handlers.
            builder.WithMetadata(requirement);
            builder.AddEndpointFilter(new RequiresFeatureEndpointFilter(requirement));

            return builder;
        }
    }
}
