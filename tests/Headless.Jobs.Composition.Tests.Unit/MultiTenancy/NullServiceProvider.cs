// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.MultiTenancy;

/// <summary>Resolves nothing — exercises the null-tolerant tenancy middleware dispatch path.</summary>
internal sealed class NullServiceProvider : IServiceProvider
{
    public static readonly NullServiceProvider Instance = new();

    public object? GetService(Type serviceType) => null;
}
