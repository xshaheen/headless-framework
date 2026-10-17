// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Tests;

/// <summary>A message of a backlog that a host restart cuts in two.</summary>
[PublicAPI]
public sealed record BacklogMessage(string Id);

/// <summary>A message whose handler always fails, so its delivery runs out of retries and ends terminal.</summary>
[PublicAPI]
public sealed record TerminalFailureMessage(string Id);

/// <summary>
/// Counts handler runs by message ID. The test owns one instance and registers it in every host it builds, so the
/// counts survive a host restart and show a message that ran on both sides of it.
/// </summary>
[PublicAPI]
public sealed class HandlerRunLedger
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, int> _started = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _completed = new(StringComparer.Ordinal);
    private TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _gateOpen = true;

    /// <summary>Holds every gated handler until <see cref="OpenGate"/> runs or its delivery is cancelled.</summary>
    public void CloseGate()
    {
        lock (_lock)
        {
            _gateOpen = false;
            _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>Releases every gated handler, held or still to come.</summary>
    public void OpenGate()
    {
        lock (_lock)
        {
            _gateOpen = true;
            _gate.TrySetResult();
        }
    }

    /// <summary>Waits while the gate is closed; returns at once when it is open.</summary>
    public Task WaitForGateAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return _gateOpen ? Task.CompletedTask : _gate.Task.WaitAsync(cancellationToken);
        }
    }

    public void RecordStarted(string id) => _Increment(_started, id);

    public void RecordCompleted(string id) => _Increment(_completed, id);

    public int Started(string id)
    {
        lock (_lock)
        {
            return _started.GetValueOrDefault(id);
        }
    }

    public int Completed(string id)
    {
        lock (_lock)
        {
            return _completed.GetValueOrDefault(id);
        }
    }

    /// <summary>Returns a copy of the completed runs by message ID.</summary>
    public IReadOnlyDictionary<string, int> CompletedRuns()
    {
        lock (_lock)
        {
            return new Dictionary<string, int>(_completed, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Waits until <paramref name="condition"/> holds over the ledger, re-checking on every recorded run. Returns
    /// <see langword="false"/> when <paramref name="timeout"/> passes first, so a test can also assert that nothing more
    /// happened within a window.
    /// </summary>
    public async Task<bool> WaitUntilAsync(
        Func<HandlerRunLedger, bool> condition,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            while (true)
            {
                Task changed;

                lock (_lock)
                {
                    changed = _changed.Task;
                }

                if (condition(this))
                {
                    return true;
                }

                await changed.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private void _Increment(Dictionary<string, int> runs, string id)
    {
        TaskCompletionSource changed;

        lock (_lock)
        {
            runs[id] = runs.GetValueOrDefault(id) + 1;
            changed = _changed;
            _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        changed.TrySetResult();
    }
}

/// <summary>
/// Completes a backlog message once the ledger's gate is open. While the gate is closed the handler stays in flight
/// until host shutdown cancels it, which is how a test leaves a delivery cut off by a restart.
/// </summary>
[PublicAPI]
public sealed class BacklogConsumer(HandlerRunLedger ledger) : IConsume<BacklogMessage>
{
    public async ValueTask ConsumeAsync(ConsumeContext<BacklogMessage> context, CancellationToken cancellationToken)
    {
        ledger.RecordStarted(context.Message.Id);
        await ledger.WaitForGateAsync(cancellationToken).ConfigureAwait(false);
        ledger.RecordCompleted(context.Message.Id);
    }
}

/// <summary>Records each attempt, then fails with an exception the retry classification treats as transient.</summary>
[PublicAPI]
public sealed class TerminalFailureConsumer(HandlerRunLedger ledger) : IConsume<TerminalFailureMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<TerminalFailureMessage> context, CancellationToken cancellationToken)
    {
        ledger.RecordStarted(context.Message.Id);

        throw new TimeoutException($"Simulated failure for message {context.MessageId}");
    }
}
