// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Resources;
using Headless.Exceptions;
using AccountId = Headless.Primitives.AccountId;
using UserId = Headless.Primitives.UserId;

// ReSharper disable once CheckNamespace
#pragma warning disable IDE0130
namespace Headless.Abstractions;

/// <summary>
/// Extension methods on <see cref="ICurrentUser"/> that assert identity fields are present and
/// throw <see cref="Headless.Exceptions.UnauthorizedException"/> (HTTP 401) when they are not.
/// </summary>
public static class CurrentUserExtensions
{
    /// <summary>Returns the current user's account ID.</summary>
    /// <param name="user">The current user context.</param>
    /// <returns>The non-null <see cref="AccountId"/>.</returns>
    /// <exception cref="Headless.Exceptions.UnauthorizedException">
    /// <see cref="ICurrentUser.AccountId"/> is <see langword="null"/> — the caller is not authenticated
    /// or the token does not contain an account identifier.
    /// </exception>
    public static AccountId GetRequiredAccountId(this ICurrentUser user)
    {
        return user.AccountId ?? throw new UnauthorizedException(GeneralMessageDescriber.NotAuthorized());
    }

    /// <summary>Returns the current user's user ID.</summary>
    /// <param name="user">The current user context.</param>
    /// <returns>The non-null <see cref="UserId"/>.</returns>
    /// <exception cref="Headless.Exceptions.UnauthorizedException">
    /// <see cref="ICurrentUser.UserId"/> is <see langword="null"/> — the caller is not authenticated
    /// or the token does not contain a user identifier.
    /// </exception>
    public static UserId GetRequiredUserId(this ICurrentUser user)
    {
        return user.UserId ?? throw new UnauthorizedException(GeneralMessageDescriber.NotAuthorized());
    }
}
