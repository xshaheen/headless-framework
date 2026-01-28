// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;

namespace Headless.Permissions.Events;

public sealed class DynamicPermissionDefinitionsChanged : IDistributedMessage
{
    public required string UniqueId { get; init; }

    public required HashSet<string> Permissions { get; init; }
}
