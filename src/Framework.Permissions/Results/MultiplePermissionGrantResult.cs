// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Checks;
using Framework.Permissions.Models;

namespace Framework.Permissions.Results;

public sealed class MultiplePermissionGrantResult
{
    public bool AllGranted => Result.Values.All(x => x == PermissionGrantResult.Granted);

    public bool AllProhibited => Result.Values.All(x => x == PermissionGrantResult.Prohibited);

    public Dictionary<string, PermissionGrantResult> Result { get; }

    public MultiplePermissionGrantResult()
    {
        Result = new(StringComparer.Ordinal);
    }

    public MultiplePermissionGrantResult(
        string[] names,
        PermissionGrantResult grantResult = PermissionGrantResult.Undefined
    )
    {
        Argument.IsNotNull(names);
        Result = new(StringComparer.Ordinal);

        foreach (var name in names)
        {
            Result.Add(name, grantResult);
        }
    }
}
