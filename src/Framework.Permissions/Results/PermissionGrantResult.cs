// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Framework.Permissions.Results;

public sealed class PermissionGrantResult
{
    private PermissionGrantResult() { }

    public required PermissionGrantStatus Status { get; init; }

    public required IReadOnlyCollection<string> ProviderKeys { get; init; }

    public static PermissionGrantResult Granted(IReadOnlyCollection<string> providerKeys)
    {
        Argument.IsNotNullOrEmpty(providerKeys);

        return new PermissionGrantResult { Status = PermissionGrantStatus.Granted, ProviderKeys = providerKeys };
    }

    public static PermissionGrantResult Prohibited(IReadOnlyCollection<string> providerKeys)
    {
        Argument.IsNotNullOrEmpty(providerKeys);

        return new PermissionGrantResult { Status = PermissionGrantStatus.Prohibited, ProviderKeys = providerKeys };
    }

    public static PermissionGrantResult Undefined(IReadOnlyCollection<string> providerKeys)
    {
        Argument.IsNotNull(providerKeys);

        return new PermissionGrantResult { Status = PermissionGrantStatus.Undefined, ProviderKeys = providerKeys };
    }
}
