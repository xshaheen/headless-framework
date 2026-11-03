// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;

namespace Headless.Messaging.Persistence;

internal static class DataStorageConstants
{
    // The longest owner tag coordination can issue: a full-length cluster name and node id plus the incarnation.
    public const int MinimumOwnerColumnMaxLength = NodeOwnerTag.MaxLength;

    public const int OwnerColumnMaxLength = 512;

    public static object GetOwnerParameterValue(this INodeMembership membership, DateTimeOffset? lockedUntil)
    {
        return lockedUntil is null ? DBNull.Value : membership.GetOwnerTag() ?? (object)DBNull.Value;
    }
}
