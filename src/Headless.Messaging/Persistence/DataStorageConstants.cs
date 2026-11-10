// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;

namespace Headless.Messaging.Persistence;

internal static class DataStorageConstants
{
    // The longest owner tag coordination can issue: a full-length cluster name and node id plus the incarnation. The
    // holder column must store every tag as it is, so the dashboard can group rows by node and an operator can read
    // which node holds a row.
    public const int MinimumLeaseHolderMaxLength = NodeOwnerTag.MaxLength;

    public const int LeaseHolderMaxLength = 512;

    /// <summary>The row-lease holder that stands for this node: its owner tag, or null with no membership identity.</summary>
    public static string? GetLeaseHolder(this INodeMembership membership)
    {
        return membership.GetOwnerTag();
    }
}
