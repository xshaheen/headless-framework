// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Security.Claims;
using Headless.Security;

namespace Tests;

/// <summary>
/// The users the test authentication scheme knows, by the name a request sends. A test changes or removes a user
/// while a stream is open to stand for a changed permission or an expired token.
/// </summary>
internal sealed class TestUsers
{
    private readonly ConcurrentDictionary<string, Claim[]> _users = new(StringComparer.Ordinal);

    /// <summary>Adds the user or replaces its claims.</summary>
    public void Set(string name, params (string Type, string Value)[] claims)
    {
        SetWithId(name, userId: name, claims);
    }

    /// <summary>
    /// Adds a user whose id differs from the name a request sends, so two names can stand for one user id in two
    /// tenants.
    /// </summary>
    public void SetWithId(string name, string userId, params (string Type, string Value)[] claims)
    {
        _users[name] =
        [
            new Claim(UserClaimTypes.UserId, userId),
            .. claims.Select(claim => new Claim(claim.Type, claim.Value)),
        ];
    }

    /// <summary>Makes the user's credential stop authenticating, as an expired token does.</summary>
    public void Expire(string name) => _users.TryRemove(name, out _);

    public Claim[]? Find(string name) => _users.GetValueOrDefault(name);
}
