// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.SQS;
using Amazon.SQS.Model;

namespace Headless.Messaging.Aws;

/// <summary>
/// Coalesces the rejects of one queue into <c>ChangeMessageVisibilityBatch</c> calls of up to ten entries.
/// </summary>
/// <remarks>
/// Group commit like <see cref="SqsDeleteBatcher"/>: the first reject goes out at once, and the rejects that arrive
/// while a call is in flight, or while it waits for the visibility gate, leave together in the next one. Every call is
/// sent while holding <paramref name="visibilityGate"/>, the gate the heartbeat extends visibility under, so an
/// extension that read a message as unsettled finishes before the reject of that message is sent and can never hide it
/// again afterwards.
/// </remarks>
internal sealed class SqsRejectBatcher(
    IAmazonSQS sqsClient,
    string queueUrl,
    SemaphoreSlim visibilityGate,
    int visibilitySeconds,
    Action<string> onMessageNotInflight
)
{
    private const string _MessageNotInflightCode = "MessageNotInflight";

    private readonly Lock _lock = new();
    private readonly Queue<PendingReject> _pending = new();
    private bool _flushing;

    public Task RejectAsync(string receiptHandle, CancellationToken cancellationToken)
    {
        return RejectAsync(receiptHandle, visibilitySeconds, cancellationToken);
    }

    /// <summary>Rejects a message so SQS hides it for <paramref name="hiddenSeconds"/> before it is received again.</summary>
    public Task RejectAsync(string receiptHandle, int hiddenSeconds, CancellationToken cancellationToken)
    {
        var pending = new PendingReject(receiptHandle, hiddenSeconds);
        bool startFlush;

        lock (_lock)
        {
            _pending.Enqueue(pending);
            startFlush = !_flushing;
            _flushing = true;
        }

        if (startFlush)
        {
            // Every pending reject is completed by the flush, so nothing is lost by not awaiting it here.
            _ = _FlushAsync();
        }

        return pending.Completion.Task.WaitAsync(cancellationToken);
    }

    private async Task _FlushAsync()
    {
        while (true)
        {
            PendingReject[] batch;
            var gateHeld = false;

            try
            {
                // Taken before the batch is cut: rejects that arrive while this waits for the gate join the same call.
                await visibilityGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                gateHeld = true;

                lock (_lock)
                {
                    if (_pending.Count == 0)
                    {
                        _flushing = false;
                        return;
                    }

                    batch = new PendingReject[Math.Min(_pending.Count, SqsDeleteBatcher.MaxBatchSize)];
                    for (var i = 0; i < batch.Length; i++)
                    {
                        batch[i] = _pending.Dequeue();
                    }
                }

                await _SendAsync(batch).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // The gate can be gone when a handler outlives shutdown. Nobody observes this flush, so a fault must
                // reach the callers; otherwise every reject on the queue would wait forever behind a flush that stopped.
                lock (_lock)
                {
                    while (_pending.Count > 0)
                    {
                        _pending.Dequeue().Completion.TrySetException(e);
                    }

                    _flushing = false;
                }

                return;
            }
            finally
            {
                if (gateHeld)
                {
                    _ReleaseGate();
                }
            }
        }
    }

    private void _ReleaseGate()
    {
        try
        {
            visibilityGate.Release();
        }
        catch (ObjectDisposedException)
        {
            // Shutdown disposed the gate while this handler was still running.
        }
    }

    private async Task _SendAsync(PendingReject[] batch)
    {
        try
        {
            if (batch.Length == 1)
            {
                await _RejectOneAsync(batch[0]).ConfigureAwait(false);
            }
            else
            {
                await _RejectBatchAsync(batch).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            foreach (var pending in batch)
            {
                pending.Completion.TrySetException(e);
            }
        }
    }

    // Settlement is must-complete, so the broker call never takes a caller's token: a caller that stops waiting
    // leaves the reject running for the others in its batch.
    private async Task _RejectOneAsync(PendingReject pending)
    {
        try
        {
            await sqsClient
                .ChangeMessageVisibilityAsync(
                    queueUrl,
                    pending.ReceiptHandle,
                    pending.VisibilitySeconds,
                    CancellationToken.None
                )
                .ConfigureAwait(false);
            pending.Completion.TrySetResult();
        }
        catch (MessageNotInflightException e)
        {
            onMessageNotInflight(e.Message);
            pending.Completion.TrySetResult();
        }
    }

    private async Task _RejectBatchAsync(PendingReject[] batch)
    {
        var entries = new List<ChangeMessageVisibilityBatchRequestEntry>(batch.Length);
        for (var i = 0; i < batch.Length; i++)
        {
            entries.Add(
                new ChangeMessageVisibilityBatchRequestEntry(
                    i.ToString(CultureInfo.InvariantCulture),
                    batch[i].ReceiptHandle
                )
                {
                    VisibilityTimeout = batch[i].VisibilitySeconds,
                }
            );
        }

        var response = await sqsClient
            .ChangeMessageVisibilityBatchAsync(queueUrl, entries, CancellationToken.None)
            .ConfigureAwait(false);

        // The SDK leaves an empty result list null.
        foreach (var failed in response.Failed ?? [])
        {
            var pending = batch[int.Parse(failed.Id, CultureInfo.InvariantCulture)];

            if (string.Equals(failed.Code, _MessageNotInflightCode, StringComparison.Ordinal))
            {
                onMessageNotInflight(failed.Message);
                pending.Completion.TrySetResult();
                continue;
            }

            pending.Completion.TrySetException(
                new AmazonSQSException($"SQS did not change the message visibility: {failed.Code}: {failed.Message}")
                {
                    ErrorCode = failed.Code,
                }
            );
        }

        foreach (var succeeded in response.Successful ?? [])
        {
            batch[int.Parse(succeeded.Id, CultureInfo.InvariantCulture)].Completion.TrySetResult();
        }

        // An entry SQS reported neither way is not confirmed rejected; failing it lets the caller see it.
        foreach (var pending in batch)
        {
            pending.Completion.TrySetException(
                new AmazonSQSException("SQS reported no outcome for the message in a visibility batch.")
            );
        }
    }

    private sealed class PendingReject(string receiptHandle, int visibilitySeconds)
    {
        public string ReceiptHandle { get; } = receiptHandle;

        public int VisibilitySeconds { get; } = visibilitySeconds;

        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
