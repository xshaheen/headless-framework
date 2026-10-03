// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Primitives;

namespace Headless.Abstractions;

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
