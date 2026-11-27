// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.MultiTenancy;

/// <summary>AsyncLocal-backed tenant write guard bypass.</summary>
/// <remarks>
/// Register one instance per host as a singleton. The bypass state lives in an instance field, so two
/// instances do not see each other's scopes: a store that resolves a different instance from the one the
/// caller used would still guard the write. <c>AddHeadlessTenancy</c> and <c>AddHeadlessDbContextServices</c>
/// both register it with <c>TryAddSingleton</c>; a store outside those paths registers it the same way.
/// </remarks>
[PublicAPI]
public sealed class TenantWriteGuardBypass : ITenantWriteGuardBypass
{
    private readonly AsyncLocal<BypassScope?> _scope = new();

    /// <inheritdoc/>
    public bool IsActive => _FindActiveScope(_scope.Value) is not null;

    /// <inheritdoc/>
    [MustDisposeResource]
    public IDisposable BeginBypass()
    {
        // Child flows inherit the current frame, so a nested bypass needs its own frame to keep the
        // child's lifetime independent of the parent's.
        var scope = new BypassScope(this, _FindActiveScope(_scope.Value));
        _scope.Value = scope;

        return scope;
    }

    private static BypassScope? _FindActiveScope(BypassScope? scope)
    {
        while (scope is { IsDisposed: true })
        {
            scope = scope.Parent;
        }

        return scope;
    }

    private sealed class BypassScope(TenantWriteGuardBypass owner, BypassScope? parent) : IDisposable
    {
        private int _isDisposed;

        public BypassScope? Parent { get; } = parent;

        public bool IsDisposed => Volatile.Read(ref _isDisposed) != 0;

        public void Dispose()
        {
            // Revoke this frame in every flow that inherited it. Only change this flow's slot when it
            // still points at this frame so a live nested scope remains in effect.
            Volatile.Write(ref _isDisposed, 1);

            if (ReferenceEquals(owner._scope.Value, this))
            {
                owner._scope.Value = _FindActiveScope(Parent);
            }
        }
    }
}
