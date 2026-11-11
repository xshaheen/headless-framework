// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>One read stopped in flight: it has read its value and waits to return it.</summary>
internal sealed class HeldRun
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the read has taken its value and is waiting.</summary>
    public Task Entered => _entered.Task;

    public void Release() => _released.TrySetResult();

    public Task WaitAsync(CancellationToken cancellationToken)
    {
        _entered.TrySetResult();

        return _released.Task.WaitAsync(cancellationToken);
    }
}
