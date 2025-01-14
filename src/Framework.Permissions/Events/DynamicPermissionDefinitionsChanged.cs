// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Domains;

namespace Framework.Permissions.Events;

[Message(EventName)]
public sealed class DynamicPermissionDefinitionsChanged : DistributedMessage
{
    public const string EventName = "permissions:dynamic-permission-definitions-changed";

    public required HashSet<string> Permissions { get; init; }
}
