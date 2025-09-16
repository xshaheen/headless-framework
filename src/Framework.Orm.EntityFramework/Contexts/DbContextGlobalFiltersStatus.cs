// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Nito.Disposables;

namespace Framework.Orm.EntityFramework.Contexts;

public sealed class DbContextGlobalFiltersStatus
{
    public bool IsDeleteFilterEnabled { get; private set; } = true;

    public bool IsSuspendedFilterEnabled { get; private set; } = true;

    public bool IsTenantFilterEnabled { get; private set; } = true;

    internal string GetCacheKey()
    {
        return $"{IsDeleteFilterEnabled}:{IsSuspendedFilterEnabled}:{IsTenantFilterEnabled}";
    }

    [MustDisposeResource]
    public IDisposable ChangeDeleteFilterEnabled(bool isEnabled)
    {
        var previous = IsDeleteFilterEnabled;
        IsDeleteFilterEnabled = isEnabled;

        return Disposable.Create(() => IsDeleteFilterEnabled = previous);
    }

    [MustDisposeResource]
    public IDisposable ChangeSuspendedFilterEnabled(bool isEnabled)
    {
        var previous = IsSuspendedFilterEnabled;
        IsSuspendedFilterEnabled = isEnabled;

        return Disposable.Create(() => IsSuspendedFilterEnabled = previous);
    }

    [MustDisposeResource]
    public IDisposable ChangeTenantFilterEnabled(bool isEnabled)
    {
        var previous = IsTenantFilterEnabled;
        IsTenantFilterEnabled = isEnabled;

        return Disposable.Create(() => IsTenantFilterEnabled = previous);
    }
}
