// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

[PublicAPI]
public static class HeadlessQueryFilters
{
    public const string MultiTenancyFilter = "MultiTenantFilter";
    public const string NotDeletedFilter = "NotDeletedFilter";
    public const string NotSuspendedFilter = "NotSuspendedFilter";
}
