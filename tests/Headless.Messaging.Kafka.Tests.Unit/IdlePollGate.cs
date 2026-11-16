// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;

namespace Tests;

/// <summary>
/// Models the idle poll of a real consumer: <c>Consume</c> blocks while the broker has nothing to deliver, instead of
/// returning <see langword="null"/> at once and spinning the poll loop.
/// </summary>
/// <remarks>
/// The gate is released by the test, never by elapsed time: <see cref="Grant"/> lets a chosen number of idle polls return,
/// and <see cref="Open"/> lets every later one return so a loop that is shutting down can observe its cancellation.
/// </remarks>
internal sealed class IdlePollGate : IDisposable
{
    private readonly SemaphoreSlim _permits = new(0);
    private volatile bool _open;

    /// <summary>Blocks the calling poll thread until a poll is granted or the gate is opened, then reports an empty poll.</summary>
    public ConsumeResult<string, byte[]> Idle()
    {
        if (!_open)
        {
            _permits.Wait();
        }

        return null!;
    }

    /// <summary>Lets <paramref name="polls"/> blocked or future idle polls return.</summary>
    public void Grant(int polls = 1)
    {
        _permits.Release(polls);
    }

    /// <summary>Lets every idle poll return from now on.</summary>
    public void Open()
    {
        _open = true;
        _permits.Release();
    }

    public void Dispose()
    {
        _permits.Dispose();
    }

    /// <summary>Opens the gate when <paramref name="cancellationToken"/> is cancelled, so the poll loop can stop.</summary>
    public CancellationTokenRegistration OpenWhenCancelled(CancellationToken cancellationToken)
    {
        return cancellationToken.Register(Open);
    }
}
