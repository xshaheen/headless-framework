// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Checks;

namespace Framework.Permissions.Results;

public sealed class MultiplePermissionGrantStatusResult()
    : Dictionary<string, PermissionGrantResult>(StringComparer.Ordinal)
{
    public bool AllGranted => Values.All(x => x.Status is PermissionGrantStatus.Granted);

    public bool AllProhibited => Values.All(x => x.Status is PermissionGrantStatus.Prohibited);

    public MultiplePermissionGrantStatusResult(
        IReadOnlyList<string> names,
        IReadOnlyCollection<string> providerKeys,
        PermissionGrantStatus grantStatus
    )
        : this()
    {
        Argument.IsNotNull(names);
        Argument.IsInEnum(grantStatus);

        var info = grantStatus switch
        {
            PermissionGrantStatus.Granted => PermissionGrantResult.Granted(providerKeys),
            PermissionGrantStatus.Prohibited => PermissionGrantResult.Prohibited(providerKeys),
            _ => PermissionGrantResult.Undefined(providerKeys),
        };

        foreach (var name in names)
        {
            Add(name, info);
        }
    }
}
