// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Kernel.Checks;

namespace Framework.Permissions.Results;

public sealed class MultiplePermissionGrantResult() : Dictionary<string, bool>(StringComparer.Ordinal)
{
    public bool AllGranted => Values.All(isGranted => isGranted);

    public bool AllProhibited => Values.All(isGranted => !isGranted);

    public MultiplePermissionGrantResult(IReadOnlyList<string> names, bool isGranted = false)
        : this()
    {
        Argument.IsNotNull(names);

        foreach (var name in names)
        {
            Add(name, isGranted);
        }
    }
}
