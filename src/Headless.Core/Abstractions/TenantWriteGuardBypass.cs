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

        // TryAddRef atomically rejects a state that already went terminal, closing the TOCTOU window
        // between the active-check and the increment. AsyncLocal can flow the same BypassState object
        // into parallel branches, so the increment must be race-safe against a concurrent last release;
        // if the shared state died, install a fresh one rather than returning an inactive ("zombie") scope.
        if (state is null || !state.TryAddRef())
        {
            state = new BypassState();
            state.TryAddRef();
            _state.Value = state;
        }

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
        // 0 = freshly created (no refs yet); > 0 = active ref count; -1 = terminal (cannot be revived).
        private int _refCount;

        public bool IsActive => Volatile.Read(ref _refCount) > 0;

        /// <summary>Atomically increments the ref count unless the state is terminal.</summary>
        /// <returns><c>true</c> if a ref was taken; <c>false</c> if the state is terminal.</returns>
        public bool TryAddRef()
        {
            int current;

            do
            {
                current = Volatile.Read(ref _refCount);

                if (current < 0)
                {
                    return false;
                }
            } while (Interlocked.CompareExchange(ref _refCount, current + 1, current) != current);

            return true;
        }

        /// <summary>Releases a ref; the last release latches the state terminal so it cannot be revived.</summary>
        public void Release()
        {
            int current;
            int next;

            do
            {
                current = Volatile.Read(ref _refCount);

                if (current <= 0)
                {
                    // Defensive no-op: already terminal, or a Release without a matching AddRef.
                    return;
                }

                next = current == 1 ? -1 : current - 1;
            } while (Interlocked.CompareExchange(ref _refCount, next, current) != current);
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
