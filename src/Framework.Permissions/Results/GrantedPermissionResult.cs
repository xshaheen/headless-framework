// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Checks;

namespace Framework.Permissions.Results;

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
