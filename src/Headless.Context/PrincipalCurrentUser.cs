// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Primitives;

namespace Headless.Context;

/// <summary>
/// <see cref="ICurrentUser"/> implementation that resolves identity from an explicit
/// <see cref="ClaimsPrincipal"/> supplied at construction time. Intended for per-request
/// or per-operation scopes where the principal is known upfront (for example, from
/// <c>HttpContext.User</c>).
/// </summary>
/// <remarks>
/// <see cref="UserId"/>, <see cref="AccountId"/>, and <see cref="Roles"/> are read from the principal once, on first
/// access, and reused: an authorization check reads them repeatedly from one short-lived instance. Claims added to the
/// principal after that first read are not seen.
/// </remarks>
public sealed class PrincipalCurrentUser(ClaimsPrincipal? principal) : ICurrentUser
{
    // Null is a valid computed id, so each memoized value carries its own computed flag, published after the value so
    // a reader that sees the flag also sees the value. A race computes the same value twice, which is harmless.
    private bool _userIdComputed;
    private bool _accountIdComputed;

    /// <inheritdoc/>
    public ClaimsPrincipal? Principal => principal;

    /// <inheritdoc/>
    public bool IsAuthenticated => principal?.Identity?.IsAuthenticated == true;

    /// <inheritdoc/>
    public UserId? UserId
    {
        get
        {
            if (!Volatile.Read(ref _userIdComputed))
            {
                field = IsAuthenticated ? principal.GetUserId() : null;
                Volatile.Write(ref _userIdComputed, true);
            }

            return field;
        }
    }

    /// <inheritdoc/>
    public string? AccountType => IsAuthenticated ? principal.GetAccountType() : null;

    /// <inheritdoc/>
    public AccountId? AccountId
    {
        get
        {
            if (!Volatile.Read(ref _accountIdComputed))
            {
                field = IsAuthenticated ? principal.GetAccountId() : null;
                Volatile.Write(ref _accountIdComputed, true);
            }

            return field;
        }
    }

    /// <inheritdoc/>
    public IReadOnlySet<string> Roles => field ??= IsAuthenticated ? principal.GetRoles() : [];
}
