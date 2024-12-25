// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api.Resources;
using Framework.Exceptions;
using Framework.Primitives;

// ReSharper disable once CheckNamespace
#pragma warning disable IDE0130
namespace Framework.BuildingBlocks.Abstractions;

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
