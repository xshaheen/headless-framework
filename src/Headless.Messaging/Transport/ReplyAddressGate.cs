// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Transport;

/// <summary>
/// The reply address a broker-backed <see cref="IReplyListener"/> hands out: unresolved while its channel is not live,
/// resolved once it is, and withdrawn again when the channel is lost, so callers wait instead of sending a request whose
/// reply reaches nobody.
/// </summary>
/// <remarks>
/// Each condition a listener passes runs under the gate's lock, the same lock a retraction takes, so a provider's
/// connection check and the address it guards change together.
/// </remarks>
internal sealed class ReplyAddressGate
{
    private readonly Lock _lock = new();
    private TaskCompletionSource<string> _address = _NewSource();
    private bool _closed;

    /// <summary>Waits for the current address.</summary>
    public ValueTask<string> WaitAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<string> address;
        lock (_lock)
        {
            address = _address;
        }

        return new ValueTask<string>(address.Task.WaitAsync(cancellationToken));
    }

    /// <summary>Hands <paramref name="address"/> to every waiting and future caller.</summary>
    public void Publish(string address)
    {
        lock (_lock)
        {
            _address.TrySetResult(address);
        }
    }

    /// <summary>Hands <paramref name="address"/> out only while <paramref name="isLive"/> holds under the lock.</summary>
    public void Publish(string address, Func<bool> isLive)
    {
        lock (_lock)
        {
            if (isLive())
            {
                _address.TrySetResult(address);
            }
        }
    }

    /// <summary>Withdraws a handed-out address, so later callers wait for the channel to be live again.</summary>
    public void Retract()
    {
        lock (_lock)
        {
            if (!_closed && _address.Task.IsCompleted)
            {
                _address = _NewSource();
            }
        }
    }

    /// <summary>Withdraws a handed-out address unless <paramref name="isStillLive"/> holds under the lock.</summary>
    public void Retract(Func<bool> isStillLive)
    {
        lock (_lock)
        {
            if (_closed || !_address.Task.IsCompleted || isStillLive())
            {
                return;
            }

            _address = _NewSource();
        }
    }

    /// <summary>
    /// Fails a caller still waiting for an address with <see cref="ObjectDisposedException"/> and stops any later
    /// retraction from opening a new wait.
    /// </summary>
    public void FailOnDispose(string objectName)
    {
        lock (_lock)
        {
            _closed = true;

            if (_address.TrySetException(new ObjectDisposedException(objectName)))
            {
                // Nobody may be waiting; observe the exception so it is not reported as unobserved.
                _ = _address.Task.Exception;
            }
        }
    }

    private static TaskCompletionSource<string> _NewSource()
    {
        return new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
