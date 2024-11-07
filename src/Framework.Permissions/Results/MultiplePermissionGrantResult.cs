// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Checks;

namespace Framework.Permissions.Results;

public sealed class MultiplePermissionGrantResult
{
    public bool AllGranted => Result.Values.All(x => x.Status is PermissionGrantStatus.Granted);

    public bool AllProhibited => Result.Values.All(x => x.Status is PermissionGrantStatus.Prohibited);

    public Dictionary<string, PermissionGrantResult> Result { get; }

    public MultiplePermissionGrantResult()
    {
        Result = new(StringComparer.Ordinal);
    }

    public MultiplePermissionGrantResult(
        IReadOnlyList<string> names,
        PermissionGrantStatus grantStatus = PermissionGrantStatus.Undefined
    )
    {
        Argument.IsNotNull(names);

        Result = new(StringComparer.Ordinal);

        var info = grantStatus switch
        {
            PermissionGrantStatus.Granted => PermissionGrantResult.Granted,
            PermissionGrantStatus.Prohibited => PermissionGrantResult.Prohibited,
            _ => PermissionGrantResult.Undefined,
        };

        foreach (var name in names)
        {
            Result.Add(name, info);
        }
    }
}
