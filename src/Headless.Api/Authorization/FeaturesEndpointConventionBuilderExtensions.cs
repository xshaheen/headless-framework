// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Features;
using Microsoft.AspNetCore.Builder;

namespace Headless.Api;

/// <summary>
/// Extension members on <see cref="IEndpointConventionBuilder"/> that gate endpoints and route groups on features,
/// the Minimal API counterpart of <see cref="RequiresFeatureAttribute"/> and <see cref="DisableFeatureCheckAttribute"/>.
/// </summary>
/// <remarks>
/// Each call only adds the attribute as endpoint metadata, so ASP.NET Core's authorization middleware enforces it
/// through the feature handler <c>AddHeadlessFeatures</c> registers. Unlike <c>RequireAuthorization</c>, a feature
/// gate adds no authenticated-user requirement and keeps the fallback policy in force.
/// </remarks>
[PublicAPI]
public static class FeaturesEndpointConventionBuilderExtensions
{
    extension<TBuilder>(TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        /// <summary>Requires at least one of <paramref name="features"/> to be enabled before the endpoint runs.</summary>
        /// <param name="features">The feature names, at least one.</param>
        /// <returns>The same endpoint convention builder.</returns>
        /// <remarks>
        /// A disabled feature fails authorization; the Headless status-codes rewriter answers it with 409 and
        /// <c>g:feature_currently_not_available</c>. Each call adds its own requirement, so a requirement on a route
        /// group and another on an endpoint in it must both pass.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="features"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="features"/> is empty.</exception>
        public TBuilder RequireFeatures(params string[] features)
        {
            return builder.RequireFeatures(requiresAll: false, features);
        }

        /// <summary>
        /// Requires <paramref name="features"/> to be enabled before the endpoint runs: all of them when
        /// <paramref name="requiresAll"/> is <see langword="true"/>, otherwise any one.
        /// </summary>
        /// <param name="requiresAll">
        /// <see langword="true"/> to require every feature; <see langword="false"/> to require at least one.
        /// </param>
        /// <param name="features">The feature names, at least one.</param>
        /// <returns>The same endpoint convention builder.</returns>
        /// <remarks>
        /// A disabled feature fails authorization; the Headless status-codes rewriter answers it with 409 and
        /// <c>g:feature_currently_not_available</c>. Each call adds its own requirement, so a requirement on a route
        /// group and another on an endpoint in it must both pass.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="features"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="features"/> is empty.</exception>
        public TBuilder RequireFeatures(bool requiresAll, params string[] features)
        {
            Argument.IsNotNullOrEmpty(features);

            return builder.WithMetadata(new RequiresFeatureAttribute(features) { IsAnd = requiresAll });
        }

        /// <summary>
        /// Skips every feature requirement on the endpoint, including those inherited from its route group or a
        /// route convention. Other authorization policies still apply.
        /// </summary>
        /// <returns>The same endpoint convention builder.</returns>
        public TBuilder DisableFeatureCheck()
        {
            return builder.WithMetadata(new DisableFeatureCheckAttribute());
        }
    }
}
