// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using AccountId = Headless.Primitives.AccountId;
using UserId = Headless.Primitives.UserId;

namespace Headless.Abstractions;

/// <summary>
/// Provides access to the identity and claims of the currently authenticated principal.
/// Implementations resolve claims from whatever principal is active in the current context
/// (HTTP request, background job, test harness, etc.).
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// Gets the raw <see cref="ClaimsPrincipal"/> for the current user, or <c>null</c> when
    /// no authenticated principal is available.
    /// </summary>
    ClaimsPrincipal? Principal { get; }

    /// <summary>
    /// Gets a value indicating whether the current principal carries an authenticated identity
    /// (<see cref="ClaimsIdentity.IsAuthenticated"/> is <c>true</c>). Do not use this as an
    /// authorization gate on its own.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Gets the strongly-typed user identifier extracted from the current principal's claims,
    /// or <c>null</c> when the claim is absent or the principal is not authenticated.
    /// </summary>
    UserId? UserId { get; }

    /// <summary>
    /// Gets the account type string extracted from the current principal's claims (for example
    /// <c>"user"</c>, <c>"service"</c>), or <c>null</c> when the claim is absent.
    /// </summary>
    string? AccountType { get; }

    /// <summary>
    /// Gets the strongly-typed account identifier extracted from the current principal's claims,
    /// or <c>null</c> when the claim is absent.
    /// </summary>
    AccountId? AccountId { get; }

    /// <summary>
    /// Gets the set of role names assigned to the current principal. Returns an empty set when
    /// the principal is not authenticated or carries no role claims.
    /// </summary>
    IReadOnlySet<string> Roles { get; }

    /// <summary>
    /// Returns the first claim of the specified type from the current principal, using ordinal
    /// comparison, or <c>null</c> when no matching claim exists.
    /// </summary>
    /// <param name="claimType">The claim type to search for.</param>
    /// <returns>The first matching <see cref="Claim"/>, or <c>null</c>.</returns>
    Claim? FindClaim(string claimType)
    {
        return Principal?.Claims.FirstOrDefault(c => string.Equals(c.Type, claimType, StringComparison.Ordinal));
    }

    /// <summary>
    /// Returns all claims of the specified type from the current principal, using ordinal
    /// comparison. Returns an empty list when the principal is <c>null</c> or carries no
    /// matching claims.
    /// </summary>
    /// <param name="claimType">The claim type to search for.</param>
    /// <returns>A read-only list of all matching <see cref="Claim"/> instances.</returns>
    IReadOnlyList<Claim> FindClaims(string claimType)
    {
        return Principal?.Claims.Where(c => string.Equals(c.Type, claimType, StringComparison.Ordinal)).ToArray() ?? [];
    }
}

/// <summary>
/// A no-op <see cref="ICurrentUser"/> implementation that always reports an unauthenticated
/// user with no claims. Useful as a default registration in anonymous or background contexts.
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

/// <summary>
/// <see cref="ICurrentUser"/> implementation that resolves identity from an explicit
/// <see cref="ClaimsPrincipal"/> supplied at construction time. Intended for per-request
/// or per-operation scopes where the principal is known upfront (for example, from
/// <c>HttpContext.User</c>).
/// </summary>
public sealed class PrincipalCurrentUser(ClaimsPrincipal? principal) : ICurrentUser
{
    /// <inheritdoc/>
    public ClaimsPrincipal? Principal => principal;

    /// <inheritdoc/>
    public bool IsAuthenticated => principal?.Identity?.IsAuthenticated == true;

    /// <inheritdoc/>
    public UserId? UserId => principal.GetUserId();

    /// <inheritdoc/>
    public string? AccountType => principal.GetAccountType();

    /// <inheritdoc/>
    public AccountId? AccountId => principal.GetAccountId();

    /// <inheritdoc/>
    public IReadOnlySet<string> Roles => principal.GetRoles();
}
