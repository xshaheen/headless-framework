// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;

namespace Headless.Abstractions;

/// <summary>
/// <see cref="ITenantStorageScopeBypass"/> implementation backed by <see cref="AsyncLocal{T}"/>, so a bypass flows
/// into awaited calls and child tasks but never into unrelated async branches.
/// </summary>
public sealed class TenantStorageScopeBypass : ITenantStorageScopeBypass
{
    /// <summary>
    /// Gets the shared singleton instance. Register this instance so that every tenant-scoped store in a process reads
    /// the same <see cref="AsyncLocal{T}"/> slot; a second instance would track a bypass nobody else sees.
    /// </summary>
    public static TenantStorageScopeBypass Instance { get; } = new();

    private readonly AsyncLocal<bool> _isActive = new();

    private TenantStorageScopeBypass() { }

    /// <inheritdoc/>
    public bool IsActive => _isActive.Value;

    /// <inheritdoc/>
    public IDisposable BeginBypass()
    {
        var previous = _isActive.Value;
        _isActive.Value = true;

        return new BypassScope(this, previous);
    }

    private sealed class BypassScope(TenantStorageScopeBypass owner, bool previous) : IDisposable
    {
        private int _isDisposed;

        public void Dispose()
        {
            // Restoring the captured value, rather than decrementing a shared counter, keeps nested scopes and
            // parallel branches correct: each async flow owns its own copy of the AsyncLocal value.
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                owner._isActive.Value = previous;
            }
        }
    }
}
