// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>AsyncLocal-backed tenant write guard bypass.</summary>
public sealed class TenantWriteGuardBypass : ITenantWriteGuardBypass
{
    private readonly AsyncLocal<BypassState?> _state = new();

    public bool IsActive => _state.Value?.IsActive == true;

    public IDisposable BeginBypass()
    {
        var state = _state.Value;

        if (state?.IsActive != true)
        {
            state = new BypassState();
            _state.Value = state;
        }

        state.AddRef();

        return new BypassScope(this, state);
    }

    private void _EndBypass(BypassState state)
    {
        state.Release();

        if (!state.IsActive && ReferenceEquals(_state.Value, state))
        {
            _state.Value = null;
        }
    }

    private sealed class BypassState
    {
        private int _isDisposed;
        private int _refCount;

        public bool IsActive => Volatile.Read(ref _isDisposed) == 0 && Volatile.Read(ref _refCount) > 0;

        public void AddRef()
        {
            Interlocked.Increment(ref _refCount);
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref _refCount) <= 0)
            {
                Volatile.Write(ref _isDisposed, 1);
            }
        }
    }

    private sealed class BypassScope(TenantWriteGuardBypass owner, BypassState state) : IDisposable
    {
        private int _isDisposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                owner._EndBypass(state);
            }
        }
    }
}
