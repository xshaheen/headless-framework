// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging;

namespace Tests;

public sealed class MessageCollector<TMessage> : IConsume<TMessage>
    where TMessage : class
{
    private readonly ConcurrentQueue<ConsumeContext<TMessage>> _receivedContexts = new();
    private readonly Lock _lock = new();
    private TaskCompletionSource<bool> _arrivedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyCollection<ConsumeContext<TMessage>> ReceivedContexts => _receivedContexts;

    public IReadOnlyCollection<TMessage> ReceivedMessages =>
        _receivedContexts.Select(context => context.Message).ToArray();

    public ValueTask ConsumeAsync(ConsumeContext<TMessage> context, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _receivedContexts.Enqueue(context);
            _arrivedTcs.TrySetResult(true);
            _arrivedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        return ValueTask.CompletedTask;
    }

    public async Task<bool> WaitForCountAsync(
        int count,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            while (true)
            {
                Task waitTask;

                lock (_lock)
                {
                    if (_receivedContexts.Count >= count)
                    {
                        return true;
                    }

                    waitTask = _arrivedTcs.Task;
                }

                await waitTask.WaitAsync(cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _receivedContexts.Clear();
            _arrivedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}

public sealed record CallbackRequestMessage(string Id, CallbackRequestMode Mode);

public enum CallbackRequestMode
{
    Normal,
    Rewrite,
    Remove,
    TypedNull,
    HeadersOnly,
}

public sealed record CallbackQueueRequestMessage(string Id, bool ReturnDeclaredContract = false);

public sealed record CallbackFailureRequestMessage(string Id);

public sealed record FanOutRequestMessage(string Id);

public sealed record IsolationRequestMessage(string Id);

public sealed record ChainRequestMessage(string Id);

public sealed record CallbackResponse(string RequestId, string SourceIntent);

public interface ICallbackResponseContract
{
    string RequestId { get; }

    string SourceIntent { get; }
}

public sealed record ConcreteCallbackResponse(string RequestId, string SourceIntent) : ICallbackResponseContract;

public sealed record RewrittenCallbackResponse(string RequestId);

public sealed record FanOutResponse(string RequestId, string Consumer);

public sealed record IsolationKeepResponse(string RequestId);

public sealed record IsolationRewriteResponse(string RequestId);

public sealed record ChainIntermediateResponse(string RequestId);

public sealed record ChainFinalResponse(string RequestId);

public sealed class CallbackRequestConsumer : IConsume<CallbackRequestMessage>
{
    private readonly Lock _lock = new();
    private bool _attempted;
    private TaskCompletionSource<bool> _attemptTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ValueTask ConsumeAsync(ConsumeContext<CallbackRequestMessage> context, CancellationToken cancellationToken)
    {
        switch (context.Message.Mode)
        {
            case CallbackRequestMode.Rewrite:
                context.Headers.RewriteCallback("rewritten-callback-response");
                context.SetResponse(new RewrittenCallbackResponse(context.Message.Id));
                break;
            case CallbackRequestMode.Remove:
                context.Headers.RemoveCallback();
                context.SetResponse(new CallbackResponse(context.Message.Id, context.IntentType.ToString()));
                break;
            case CallbackRequestMode.TypedNull:
                context.SetResponse<CallbackResponse>(null!);
                break;
            case CallbackRequestMode.HeadersOnly:
                break;
            default:
                context.SetResponse(new CallbackResponse(context.Message.Id, context.IntentType.ToString()));
                break;
        }

        lock (_lock)
        {
            _attempted = true;
            _attemptTcs.TrySetResult(true);
            _attemptTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        return ValueTask.CompletedTask;
    }

    public async Task<bool> WaitForAttemptAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            Task waitTask;

            lock (_lock)
            {
                // Observe-before-subscribe guard: if the attempt already fired (and swapped the TCS) before
                // the caller entered, return immediately instead of awaiting the fresh, never-signaled TCS.
                if (_attempted)
                {
                    return true;
                }

                waitTask = _attemptTcs.Task;
            }

            await waitTask.WaitAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _attempted = false;
            _attemptTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}

public sealed class CallbackQueueRequestConsumer : IConsume<CallbackQueueRequestMessage>
{
    public ValueTask ConsumeAsync(
        ConsumeContext<CallbackQueueRequestMessage> context,
        CancellationToken cancellationToken
    )
    {
        if (context.Message.ReturnDeclaredContract)
        {
            context.SetResponse<ICallbackResponseContract>(
                new ConcreteCallbackResponse(context.Message.Id, context.IntentType.ToString())
            );
        }
        else
        {
            context.SetResponse(new CallbackResponse(context.Message.Id, context.IntentType.ToString()));
        }

        return ValueTask.CompletedTask;
    }
}

public sealed record CallbackPublishSnapshot<TMessage>(
    Type DeclaredMessageType,
    Type ConcreteMessageType,
    TMessage? Content,
    IntentType IntentType
);

public sealed class CallbackPublishRecorder<TMessage>
{
    private readonly ConcurrentQueue<CallbackPublishSnapshot<TMessage>> _snapshots = new();

    public IReadOnlyCollection<CallbackPublishSnapshot<TMessage>> Snapshots => _snapshots;

    public void Record(PublishContext<TMessage> context)
    {
        _snapshots.Enqueue(
            new CallbackPublishSnapshot<TMessage>(
                context.MessageType,
                context.ConcreteMessageType,
                context.Content,
                context.IntentType
            )
        );
    }

    public void Clear()
    {
        _snapshots.Clear();
    }
}

public sealed class CallbackPublishMiddleware<TMessage>(CallbackPublishRecorder<TMessage> recorder)
    : IPublishMiddleware<PublishContext<TMessage>>
{
    public ValueTask InvokeAsync(PublishContext<TMessage> context, Func<ValueTask> next)
    {
        recorder.Record(context);
        return next();
    }
}

public sealed class CallbackFailureRequestConsumer : IConsume<CallbackFailureRequestMessage>
{
    private readonly Lock _lock = new();
    private bool _attempted;
    private TaskCompletionSource<bool> _attemptTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ValueTask ConsumeAsync(
        ConsumeContext<CallbackFailureRequestMessage> context,
        CancellationToken cancellationToken
    )
    {
        lock (_lock)
        {
            _attempted = true;
            _attemptTcs.TrySetResult(true);
            _attemptTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        context.SetResponse(new UnserializableCallbackResponse(context.Message.Id));
        return ValueTask.CompletedTask;
    }

    public async Task<bool> WaitForAttemptAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            Task waitTask;

            lock (_lock)
            {
                // Observe-before-subscribe guard: if the attempt already fired (and swapped the TCS) before
                // the caller entered, return immediately instead of awaiting the fresh, never-signaled TCS.
                if (_attempted)
                {
                    return true;
                }

                waitTask = _attemptTcs.Task;
            }

            await waitTask.WaitAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _attempted = false;
            _attemptTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}

public sealed class FanOutConsumerA : IConsume<FanOutRequestMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<FanOutRequestMessage> context, CancellationToken cancellationToken)
    {
        context.SetResponse(new FanOutResponse(context.Message.Id, "A"));
        return ValueTask.CompletedTask;
    }
}

public sealed class FanOutConsumerB : IConsume<FanOutRequestMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<FanOutRequestMessage> context, CancellationToken cancellationToken)
    {
        context.SetResponse(new FanOutResponse(context.Message.Id, "B"));
        return ValueTask.CompletedTask;
    }
}

public sealed class IsolationKeepConsumer : IConsume<IsolationRequestMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<IsolationRequestMessage> context, CancellationToken cancellationToken)
    {
        context.SetResponse(new IsolationKeepResponse(context.Message.Id));
        return ValueTask.CompletedTask;
    }
}

public sealed class IsolationRewriteConsumer : IConsume<IsolationRequestMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<IsolationRequestMessage> context, CancellationToken cancellationToken)
    {
        context.Headers.RewriteCallback("isolation-rewritten-callback");
        context.SetResponse(new IsolationRewriteResponse(context.Message.Id));
        return ValueTask.CompletedTask;
    }
}

public sealed class ChainRequestConsumer : IConsume<ChainRequestMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<ChainRequestMessage> context, CancellationToken cancellationToken)
    {
        context.SetResponseCallbackName("chain-final-callback");
        context.SetResponse(new ChainIntermediateResponse(context.Message.Id));
        return ValueTask.CompletedTask;
    }
}

public sealed class ChainIntermediateConsumer : IConsume<ChainIntermediateResponse>
{
    public ValueTask ConsumeAsync(
        ConsumeContext<ChainIntermediateResponse> context,
        CancellationToken cancellationToken
    )
    {
        context.SetResponse(new ChainFinalResponse(context.Message.RequestId));
        return ValueTask.CompletedTask;
    }
}

public sealed record UnserializableCallbackResponse(string RequestId)
{
    public string ThrowingValue => throw new InvalidOperationException("Callback response cannot serialize.");
}
