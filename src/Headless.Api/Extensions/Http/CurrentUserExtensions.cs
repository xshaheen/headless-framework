// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api.Resources;
using Headless.Exceptions;
using AccountId = Headless.Primitives.AccountId;
using UserId = Headless.Primitives.UserId;

// ReSharper disable once CheckNamespace
#pragma warning disable IDE0130
namespace Headless.Abstractions;

public static class CurrentUserExtensions
{
    public static AccountId GetRequiredAccountId(this ICurrentUser user)
    {
        return user.AccountId ?? throw new ConflictException(GeneralMessageDescriber.NotAuthorized());
    }

    public static UserId GetRequiredUserId(this ICurrentUser user)
    {
        return user.UserId ?? throw new ConflictException(GeneralMessageDescriber.NotAuthorized());
    }
}
