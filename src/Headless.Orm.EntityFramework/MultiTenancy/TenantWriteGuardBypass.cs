// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Core;

namespace Headless.EntityFramework.MultiTenancy;

/// <summary>AsyncLocal-backed tenant write guard bypass.</summary>
public sealed class TenantWriteGuardBypass : ITenantWriteGuardBypass
{
    private readonly AsyncLocal<bool> _isActive = new();

    public bool IsActive => _isActive.Value;

    public IDisposable BeginBypass()
    {
        var wasActive = _isActive.Value;
        _isActive.Value = true;

        return DisposableFactory.Create(() => _isActive.Value = wasActive);
    }
}
