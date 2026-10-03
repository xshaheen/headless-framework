// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Primitives;

namespace Headless.Abstractions;

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
    public UserId? UserId => IsAuthenticated ? principal.GetUserId() : null;

    /// <inheritdoc/>
    public string? AccountType => IsAuthenticated ? principal.GetAccountType() : null;

    /// <inheritdoc/>
    public AccountId? AccountId => IsAuthenticated ? principal.GetAccountId() : null;

    /// <inheritdoc/>
    public IReadOnlySet<string> Roles => IsAuthenticated ? principal.GetRoles() : [];
}
