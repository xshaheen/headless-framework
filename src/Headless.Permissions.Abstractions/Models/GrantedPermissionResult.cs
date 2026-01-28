// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Permissions.Models;

public sealed class GrantedPermissionResult(string name, bool isGranted)
{
    public string Name { get; } = Argument.IsNotNull(name);

    public bool IsGranted { get; internal set; } = isGranted;

    public List<GrantPermissionProvider> Providers { get; } = [];
}

public sealed class GrantPermissionProvider(string name, IReadOnlyCollection<string> keys)
{
    public string Name { get; } = Argument.IsNotNull(name);

    public IReadOnlyCollection<string> Keys { get; } = Argument.IsNotNull(keys);
}
