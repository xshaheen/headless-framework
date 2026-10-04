// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Checks;
using Microsoft.AspNetCore.Authorization;

namespace Headless.Api;

/// <summary>
/// ASP.NET Core authorization requirement that requires a principal to possess exactly one claim of a given
/// type satisfying a predicate.
/// </summary>
[PublicAPI]
public sealed class SingleClaimRequirement : AuthorizationHandler<SingleClaimRequirement>, IAuthorizationRequirement
{
    /// <summary>The claim type that must appear exactly once on the principal.</summary>
    public string ClaimType { get; }

    /// <summary>The predicate evaluated against the single claim's value.</summary>
    public Func<string, bool> Predicate { get; }

    /// <summary>The optional expected claim value when configured with an exact value requirement.</summary>
    public string? RequiredValue { get; }

    /// <summary>Initializes a new instance of <see cref="SingleClaimRequirement"/> with a claim type and predicate.</summary>
    /// <param name="claimType">The required claim type.</param>
    /// <param name="predicate">The predicate evaluated against the single claim's value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="claimType"/> is <see langword="null"/> or whitespace.</exception>
    public SingleClaimRequirement(string claimType, Func<string, bool> predicate)
        : this(claimType, predicate, requiredValue: null) { }

    /// <summary>Initializes a new instance of <see cref="SingleClaimRequirement"/> with a claim type, predicate, and expected value.</summary>
    /// <param name="claimType">The required claim type.</param>
    /// <param name="predicate">The predicate evaluated against the single claim's value.</param>
    /// <param name="requiredValue">The expected value for diagnostics, if known.</param>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="claimType"/> is <see langword="null"/> or whitespace.</exception>
    internal SingleClaimRequirement(string claimType, Func<string, bool> predicate, string? requiredValue)
    {
        ClaimType = Argument.IsNotNullOrWhiteSpace(claimType);
        Predicate = Argument.IsNotNull(predicate);
        RequiredValue = requiredValue;
    }

    /// <inheritdoc/>
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SingleClaimRequirement requirement
    )
    {
        Argument.IsNotNull(context);
        Argument.IsNotNull(requirement);

        if (context.User is null)
        {
            return Task.CompletedTask;
        }

        Claim? singleClaim = null;
        var count = 0;

        foreach (var claim in context.User.FindAll(requirement.ClaimType))
        {
            count++;

            if (count > 1)
            {
                break;
            }

            singleClaim = claim;
        }

        if (count == 1 && requirement.Predicate(singleClaim!.Value))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return RequiredValue is not null
            ? $"SingleClaimRequirement: Claim.Type={ClaimType} and Claim.Value={RequiredValue}"
            : $"SingleClaimRequirement: Claim.Type={ClaimType}";
    }
}
