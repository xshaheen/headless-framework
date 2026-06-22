// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Benchmarks.Support;

/// <summary>The message payload dispatched through the consume pipeline in the benchmarks.</summary>
public sealed record BenchmarkPayload(string Value);

/// <summary>
/// A no-op <see cref="IMessageDispatcher"/>. Registered in the benchmark service provider so the consume
/// pipeline's reflection dispatch fallback (no runtime invoker) resolves a target that does no handler work,
/// isolating the per-dispatch plumbing cost.
/// </summary>
internal sealed class NoOpMessageDispatcher : IMessageDispatcher
{
    public Task DispatchAsync<TMessage>(ConsumeContext<TMessage> context, CancellationToken cancellationToken)
        where TMessage : class => Task.CompletedTask;

    public Task DispatchInScopeAsync<TMessage>(
        IServiceProvider serviceProvider,
        ConsumeContext<TMessage> context,
        CancellationToken cancellationToken
    )
        where TMessage : class => Task.CompletedTask;

    public Task DispatchInScopeAsync<TMessage>(
        IServiceProvider serviceProvider,
        ConsumerExecutorDescriptor descriptor,
        ConsumeContext<TMessage> context,
        CancellationToken cancellationToken
    )
        where TMessage : class => Task.CompletedTask;
}

/// <summary>A pass-through consume middleware used to vary the registered middleware count per dispatch.</summary>
internal sealed class NoOpConsumeMiddleware : IConsumeMiddleware<ConsumeContext>
{
    public ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next) => next();
}
