// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.EntityFramework.MultiTenancy;

/// <summary>Tracks an operation-local bypass for intentional host or admin tenant-owned writes.</summary>
public interface ITenantWriteGuardBypass
{
    /// <summary>Gets a value indicating whether the current async operation is bypassing the tenant write guard.</summary>
    bool IsActive { get; }

    /// <summary>Begins a scoped bypass and restores the previous state when disposed.</summary>
    [MustDisposeResource]
    IDisposable BeginBypass();
}
