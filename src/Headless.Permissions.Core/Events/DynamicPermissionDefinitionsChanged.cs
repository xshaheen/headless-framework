// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Permissions.Events;

public sealed class DynamicPermissionDefinitionsChanged
{
    public required string UniqueId { get; init; }

    public required HashSet<string> Permissions { get; init; }
}
