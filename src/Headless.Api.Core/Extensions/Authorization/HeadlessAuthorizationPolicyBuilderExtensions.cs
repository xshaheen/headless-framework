// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Checks;

namespace Microsoft.AspNetCore.Authorization;

/// <summary>
/// Extension methods for <see cref="AuthorizationPolicyBuilder"/> providing single-claim policy requirements.
/// </summary>
[PublicAPI]
public static class HeadlessAuthorizationPolicyBuilderExtensions
{
    /// <summary>
    /// Adds a <see cref="SingleClaimRequirement"/> to the policy that succeeds only when the principal
    /// carries exactly one claim of <paramref name="claimType"/> and its value equals <paramref name="value"/>
    /// using ordinal comparison.
    /// </summary>
    /// <param name="builder">The authorization policy builder.</param>
    /// <param name="claimType">The required claim type.</param>
    /// <param name="value">The required claim value.</param>
    /// <returns>The same policy builder for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="claimType"/> is <see langword="null"/> or whitespace.</exception>
    public static AuthorizationPolicyBuilder RequireSingleClaimValue(
        this AuthorizationPolicyBuilder builder,
        string claimType,
        string value
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNullOrWhiteSpace(claimType);
        Argument.IsNotNull(value);

        return builder.AddRequirements(
            new SingleClaimRequirement(claimType, v => string.Equals(v, value, StringComparison.Ordinal), value)
        );
    }

    /// <summary>
    /// Adds a <see cref="SingleClaimRequirement"/> to the policy that succeeds only when the principal
    /// carries exactly one claim of <paramref name="claimType"/> and its value satisfies <paramref name="predicate"/>.
    /// </summary>
    /// <param name="builder">The authorization policy builder.</param>
    /// <param name="claimType">The required claim type.</param>
    /// <param name="predicate">The predicate evaluated against the single claim's value.</param>
    /// <returns>The same policy builder for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="claimType"/> is <see langword="null"/> or whitespace.</exception>
    public static AuthorizationPolicyBuilder RequireSingleClaim(
        this AuthorizationPolicyBuilder builder,
        string claimType,
        Func<string, bool> predicate
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNullOrWhiteSpace(claimType);
        Argument.IsNotNull(predicate);

        return builder.AddRequirements(new SingleClaimRequirement(claimType, predicate));
    }
}
