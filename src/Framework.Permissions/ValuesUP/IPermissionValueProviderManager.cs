// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Permissions.ValueProviders;

namespace Framework.Permissions.Values;

public interface IPermissionValueProviderManager
{
    IReadOnlyList<IPermissionValueProvider> ValueProviders { get; }
}
