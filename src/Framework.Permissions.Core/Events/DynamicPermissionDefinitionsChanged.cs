// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Domain;

namespace Framework.Permissions.Events;

public sealed class DynamicPermissionDefinitionsChanged : IDistributedMessage
{
    public required string UniqueId { get; init; }

    public required HashSet<string> Permissions { get; init; }
}
