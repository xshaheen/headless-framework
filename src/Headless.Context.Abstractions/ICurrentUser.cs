// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Primitives;

namespace Headless.Context;

/// <summary>
/// Provides access to identity and claims for the currently authenticated principal.
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// Gets the underlying <see cref="ClaimsPrincipal"/>, or <see langword="null"/> when no principal is available.
    /// </summary>
    ClaimsPrincipal? Principal { get; }

    /// <summary>
    /// Gets a value indicating whether the current principal is authenticated.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Gets the strongly typed user identifier extracted from claims, or <see langword="null"/> when absent.
    /// </summary>
    UserId? UserId { get; }

    /// <summary>
    /// Gets the account type string extracted from claims, or <see langword="null"/> when absent.
    /// </summary>
    string? AccountType { get; }

    /// <summary>
    /// Gets the strongly typed account identifier extracted from claims, or <see langword="null"/> when absent.
    /// </summary>
    AccountId? AccountId { get; }

    /// <summary>
    /// Gets the set of role names assigned to the current principal.
    /// </summary>
    IReadOnlySet<string> Roles { get; }

    /// <summary>
    /// Finds the first claim of the specified type from the current principal using ordinal comparison.
    /// </summary>
    /// <param name="claimType">The claim type to search for.</param>
    /// <returns>The first matching <see cref="Claim"/>, or <see langword="null"/> when no match is found.</returns>
    Claim? FindClaim(string claimType)
    {
        return Principal?.Claims.FirstOrDefault(c => string.Equals(c.Type, claimType, StringComparison.Ordinal));
    }

    /// <summary>
    /// Finds all claims of the specified type from the current principal using ordinal comparison.
    /// </summary>
    /// <param name="claimType">The claim type to search for.</param>
    /// <returns>A read-only list of all matching <see cref="Claim"/> instances.</returns>
    IReadOnlyList<Claim> FindClaims(string claimType)
    {
        var principal = Principal;

        if (principal is null)
        {
            return [];
        }

        // Manual loop rather than Where(...).ToArray(): claim lookups run several times per request and the
        // predicate captures `claimType`, so the LINQ form allocates a closure, a delegate, and an iterator
        // even when nothing matches — which is the usual outcome.
        List<Claim>? matches = null;

        foreach (var claim in principal.Claims)
        {
            if (string.Equals(claim.Type, claimType, StringComparison.Ordinal))
            {
                (matches ??= []).Add(claim);
            }
        }

        return matches ?? [];
    }
}

/// <summary>
/// Implements <see cref="ICurrentUser"/> for unauthenticated contexts with no claims.
/// </summary>
public sealed class NullCurrentUser : ICurrentUser
{
    /// <inheritdoc/>
    public ClaimsPrincipal? Principal => null;

    /// <inheritdoc/>
    public bool IsAuthenticated => false;

    /// <inheritdoc/>
    public UserId? UserId => null;

    /// <inheritdoc/>
    public string? AccountType => null;

    /// <inheritdoc/>
    public AccountId? AccountId => null;

    /// <inheritdoc/>
    public IReadOnlySet<string> Roles => ImmutableHashSet<string>.Empty;
}
