// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Features;

namespace Microsoft.AspNetCore.Authorization;

/// <summary>
/// Extension members on <see cref="AuthorizationPolicyBuilder"/> that gate a policy on features, so a Minimal API
/// endpoint or route group writes <c>.RequireAuthorization(policy =&gt; policy.RequireFeatures("Reports"))</c>, the same
/// way it requires a permission.
/// </summary>
/// <remarks>
/// A policy built this way adds no authenticated-user requirement, so anonymous callers are gated too. As with any
/// <c>RequireAuthorization(...)</c> call, though, the endpoint then carries authorization data of its own and the
/// fallback policy no longer applies to it. To keep the fallback policy in force, add the attribute as metadata
/// instead: <c>.WithMetadata(new RequiresFeatureAttribute("Reports"))</c>, or put <see cref="RequiresFeatureAttribute"/>
/// on the handler.
/// </remarks>
[PublicAPI]
public static class HeadlessFeaturesAuthorizationPolicyBuilderExtensions
{
    extension(AuthorizationPolicyBuilder builder)
    {
        /// <summary>Requires at least one of <paramref name="features"/> to be enabled.</summary>
        /// <param name="features">The feature names, at least one.</param>
        /// <returns>The same policy builder.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="features"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="features"/> is empty.</exception>
        public AuthorizationPolicyBuilder RequireFeatures(params string[] features)
        {
            return builder.RequireFeatures(requiresAll: false, features);
        }

        /// <summary>
        /// Requires <paramref name="features"/> to be enabled: all of them when <paramref name="requiresAll"/> is
        /// <see langword="true"/>, otherwise any one.
        /// </summary>
        /// <param name="requiresAll">
        /// <see langword="true"/> to require every feature; <see langword="false"/> to require at least one.
        /// </param>
        /// <param name="features">The feature names, at least one.</param>
        /// <returns>The same policy builder.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="features"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="features"/> is empty.</exception>
        public AuthorizationPolicyBuilder RequireFeatures(bool requiresAll, params string[] features)
        {
            Argument.IsNotNull(builder);

            return builder.AddRequirements(new FeatureRequirement(features, requiresAll));
        }
    }
}
