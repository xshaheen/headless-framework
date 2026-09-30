// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;

namespace Headless.Messaging.Benchmarks.Support;

/// <summary>The message payload dispatched through the consume pipeline in the benchmarks.</summary>
public sealed record BenchmarkPayload(string Value);

/// <summary>
/// A consumer that does no handler work, so a dispatch through its generated <see cref="MessageConsumerDispatch"/>
/// isolates the per-dispatch plumbing cost.
/// </summary>
[BusConsumer(Identity)]
internal sealed class NoOpBenchmarkConsumer : IConsume<BenchmarkPayload>
{
    public const string Identity = "benchmarks.no-op";

    public ValueTask ConsumeAsync(ConsumeContext<BenchmarkPayload> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

/// <summary>A pass-through consume middleware used to vary the registered middleware count per dispatch.</summary>
internal sealed class NoOpConsumeMiddleware : IConsumeMiddleware<ConsumeContext>
{
    public ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next)
    {
        return next();
    }
}

/// <summary>A pass-through publish middleware used to vary the registered middleware count per publish.</summary>
internal sealed class NoOpPublishMiddleware : IPublishMiddleware<PublishContext>
{
    public ValueTask InvokeAsync(PublishContext context, Func<ValueTask> next)
    {
        return next();
    }
}
