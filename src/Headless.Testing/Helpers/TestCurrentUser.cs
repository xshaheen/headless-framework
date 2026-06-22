// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Abstractions;
using AccountId = Headless.Primitives.AccountId;
using UserId = Headless.Primitives.UserId;

namespace Headless.Testing.Helpers;

/// <summary>
/// Mutable <see cref="ICurrentUser"/> implementation for tests. All properties are directly
/// settable; roles are managed via <see cref="WritableRoles"/>.
/// </summary>
[PublicAPI]
public sealed class TestCurrentUser : ICurrentUser
{
    /// <summary>The claims principal for the current user. Defaults to an empty principal.</summary>
    public ClaimsPrincipal Principal { get; set; } = new();

    /// <summary>Controls whether the user is considered authenticated.</summary>
    public bool IsAuthenticated { get; set; }

    /// <summary>The current user's identifier, or <see langword="null"/> when anonymous.</summary>
    public UserId? UserId { get; set; }

    /// <summary>The account type discriminator, or <see langword="null"/> when not set.</summary>
    public string? AccountType { get; set; }

    /// <summary>The current user's account identifier, or <see langword="null"/> when not set.</summary>
    public AccountId? AccountId { get; set; }

    /// <summary>
    /// Read-only view of <see cref="WritableRoles"/>. Uses ordinal (case-sensitive) comparison,
    /// matching the production <c>ICurrentUser</c> contract.
    /// </summary>
    public IReadOnlySet<string> Roles => WritableRoles;

    /// <summary>
    /// Mutable role set. Add or remove roles here to control what <see cref="Roles"/> returns.
    /// Uses ordinal string comparison.
    /// </summary>
    public HashSet<string> WritableRoles { get; } = new(StringComparer.Ordinal);
}
