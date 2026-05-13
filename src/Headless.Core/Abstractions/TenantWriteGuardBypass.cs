// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>AsyncLocal-backed tenant write guard bypass.</summary>
internal sealed class TenantWriteGuardBypass : ITenantWriteGuardBypass
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
            var newCount = Interlocked.Decrement(ref _refCount);

            if (newCount < 0)
            {
                // Defensive clamp: a misuse (Release without AddRef, or duplicate dispose) must not
                // leave the counter negative, which would allow a future AddRef to land at 0 and
                // be observed as inactive.
                Interlocked.Exchange(ref _refCount, 0);
                return;
            }

            if (newCount == 0)
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
