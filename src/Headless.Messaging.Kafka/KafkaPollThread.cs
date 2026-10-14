// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Confluent.Kafka;

namespace Headless.Messaging.Kafka;

/// <summary>
/// Owns the one thread a Kafka consumer blocks in <c>Consume</c> on. librdkafka's poll is a blocking call, so running
/// it on the thread pool pins one pool thread per consumer for the consumer's lifetime. A dedicated thread keeps the
/// pool free and lets the async poll loop await each poll instead.
/// </summary>
internal sealed class KafkaPollThread : IDisposable
{
    // Disposed by the poll thread itself once it drains: disposing here could race a poll still enqueuing.
#pragma warning disable CA2213 // Disposed on the poll thread after CompleteAdding, not in Dispose
    private readonly BlockingCollection<PollRequest> _requests = [];
#pragma warning restore CA2213
    private readonly Thread _thread;

    public KafkaPollThread(string name)
    {
        _thread = new Thread(_Run) { IsBackground = true, Name = name };
        _thread.Start();
    }

    /// <summary>Runs one blocking poll on the dedicated thread; a fault propagates to the awaiting caller unchanged.</summary>
    public Task<ConsumeResult<string, byte[]>?> PollAsync(Func<ConsumeResult<string, byte[]>?> poll)
    {
        var completion = new TaskCompletionSource<ConsumeResult<string, byte[]>?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        try
        {
            _requests.Add(new PollRequest(poll, completion));
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
        {
            // Disposed: the loop that owned this thread is over.
            completion.SetCanceled();
        }

        return completion.Task;
    }

    public void Dispose()
    {
        // The thread finishes the poll in progress, then drains out; it never outlives the loop by more than one poll.
        _requests.CompleteAdding();
    }

    private void _Run()
    {
        foreach (var request in _requests.GetConsumingEnumerable())
        {
            try
            {
                request.Completion.SetResult(request.Poll());
            }
            catch (Exception e)
            {
                request.Completion.SetException(e);
            }
        }

        _requests.Dispose();
    }

    private readonly record struct PollRequest(
        Func<ConsumeResult<string, byte[]>?> Poll,
        TaskCompletionSource<ConsumeResult<string, byte[]>?> Completion
    );
}
