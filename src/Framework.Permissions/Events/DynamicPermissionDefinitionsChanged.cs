// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Kernel.Domains;

namespace Framework.Permissions.Events;

[DistributedMessage(EventName)]
public sealed class DynamicPermissionDefinitionsChanged : DistributedMessage
{
    public const string EventName = "permissions:dynamic-permission-definitions-changed";

    public required HashSet<string> Permissions { get; init; }
}
