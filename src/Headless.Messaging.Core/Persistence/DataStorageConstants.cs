// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;

namespace Headless.Messaging.Persistence;

internal static class DataStorageConstants
{
    public const int OwnerColumnMaxLength = 512;

    public static string? GetOwnerTag(this INodeMembership membership) => membership.Identity?.ToString();

    public static object GetOwnerParameterValue(this INodeMembership membership, DateTime? lockedUntil) =>
        lockedUntil is null ? DBNull.Value : membership.GetOwnerTag() ?? (object)DBNull.Value;
}
