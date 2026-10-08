// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.SQS;
using Amazon.SQS.Model;

namespace Headless.Messaging.Aws;

/// <summary>
/// Coalesces the deletes of one queue into <c>DeleteMessageBatch</c> calls of up to ten entries.
/// </summary>
/// <remarks>
/// Group commit, not a timer: the first delete goes out at once, and the deletes that arrive while a call is in flight
/// leave together in the next one. A lone delete therefore waits for nothing, and concurrent handlers share calls.
/// </remarks>
internal sealed class SqsDeleteBatcher(IAmazonSQS sqsClient, string queueUrl, Action<string> onInvalidReceiptHandle)
{
    internal const int MaxBatchSize = 10;
    private const string _InvalidReceiptHandleCode = "ReceiptHandleIsInvalid";

    private readonly Lock _lock = new();
    private readonly Queue<PendingDelete> _pending = new();
    private bool _flushing;

    public Task DeleteAsync(string receiptHandle, CancellationToken cancellationToken)
    {
        var pending = new PendingDelete(receiptHandle);
        bool startFlush;

        lock (_lock)
        {
            _pending.Enqueue(pending);
            startFlush = !_flushing;
            _flushing = true;
        }

        if (startFlush)
        {
            // Every pending delete is completed by the flush, so nothing is lost by not awaiting it here.
            _ = _FlushAsync();
        }

        return pending.Completion.Task.WaitAsync(cancellationToken);
    }

    private async Task _FlushAsync()
    {
        while (true)
        {
            PendingDelete[] batch;

            lock (_lock)
            {
                if (_pending.Count == 0)
                {
                    _flushing = false;
                    return;
                }

                batch = new PendingDelete[Math.Min(_pending.Count, MaxBatchSize)];
                for (var i = 0; i < batch.Length; i++)
                {
                    batch[i] = _pending.Dequeue();
                }
            }

            if (batch.Length == 1)
            {
                await _DeleteOneAsync(batch[0]).ConfigureAwait(false);
            }
            else
            {
                await _DeleteBatchAsync(batch).ConfigureAwait(false);
            }
        }
    }

    // Settlement is must-complete, so the broker call never takes a caller's token: a caller that stops waiting
    // leaves the delete running for the others in its batch.
    private async Task _DeleteOneAsync(PendingDelete pending)
    {
        try
        {
            await sqsClient
                .DeleteMessageAsync(queueUrl, pending.ReceiptHandle, CancellationToken.None)
                .ConfigureAwait(false);
            pending.Completion.TrySetResult();
        }
        catch (ReceiptHandleIsInvalidException e)
        {
            onInvalidReceiptHandle(e.Message);
            pending.Completion.TrySetResult();
        }
        catch (Exception e)
        {
            pending.Completion.TrySetException(e);
        }
    }

    private async Task _DeleteBatchAsync(PendingDelete[] batch)
    {
        DeleteMessageBatchResponse response;

        try
        {
            var entries = new List<DeleteMessageBatchRequestEntry>(batch.Length);
            for (var i = 0; i < batch.Length; i++)
            {
                entries.Add(
                    new DeleteMessageBatchRequestEntry(i.ToString(CultureInfo.InvariantCulture), batch[i].ReceiptHandle)
                );
            }

            response = await sqsClient
                .DeleteMessageBatchAsync(queueUrl, entries, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            foreach (var pending in batch)
            {
                pending.Completion.TrySetException(e);
            }

            return;
        }

        // The SDK leaves an empty result list null.
        foreach (var failed in response.Failed ?? [])
        {
            var pending = batch[int.Parse(failed.Id, CultureInfo.InvariantCulture)];

            if (string.Equals(failed.Code, _InvalidReceiptHandleCode, StringComparison.Ordinal))
            {
                onInvalidReceiptHandle(failed.Message);
                pending.Completion.TrySetResult();
                continue;
            }

            pending.Completion.TrySetException(
                new AmazonSQSException($"SQS did not delete the message: {failed.Code}: {failed.Message}")
                {
                    ErrorCode = failed.Code,
                }
            );
        }

        foreach (var succeeded in response.Successful ?? [])
        {
            batch[int.Parse(succeeded.Id, CultureInfo.InvariantCulture)].Completion.TrySetResult();
        }

        // An entry SQS reported neither way was not confirmed deleted; failing it lets the core reject and redeliver.
        foreach (var pending in batch)
        {
            pending.Completion.TrySetException(
                new AmazonSQSException("SQS reported no outcome for the message in a delete batch.")
            );
        }
    }

    private sealed class PendingDelete(string receiptHandle)
    {
        public string ReceiptHandle { get; } = receiptHandle;

        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
