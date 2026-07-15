// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;

namespace Headless.Messaging.Persistence;

internal static class DataStorageConstants
{
    // Allows a Kubernetes DNS-label node id (253), '@', and a long decimal incarnation with margin.
    public const int MinimumOwnerColumnMaxLength = 300;

    public const int OwnerColumnMaxLength = 512;

    public static string? GetOwnerTag(this INodeMembership membership)
    {
        return membership.Identity?.ToString();
    }

    public static object GetOwnerParameterValue(this INodeMembership membership, DateTimeOffset? lockedUntil)
    {
        return lockedUntil is null ? DBNull.Value : membership.GetOwnerTag() ?? (object)DBNull.Value;
    }
}
