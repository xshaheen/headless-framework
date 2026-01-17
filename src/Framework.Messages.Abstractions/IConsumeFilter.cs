// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Messages;

namespace Framework.Messages;

/// <summary>A filter that surrounds execution of the subscriber.</summary>
public interface IConsumeFilter
{
    /// <summary>Called before the subscriber executes.</summary>
    /// <param name="context">The <see cref="ExecutingContext" />.</param>
    ValueTask OnSubscribeExecutingAsync(ExecutingContext context);

    /// <summary>Called after the subscriber executes.</summary>
    /// <param name="context">The <see cref="ExecutedContext" />.</param>
    ValueTask OnSubscribeExecutedAsync(ExecutedContext context);

    /// <summary>Called after the subscriber has thrown an <see cref="System.Exception" />.</summary>
    /// <param name="context">The <see cref="ExceptionContext" />.</param>
    ValueTask OnSubscribeExceptionAsync(ExceptionContext context);
}

/// <summary>Abstract base class for ISubscribeFilter for use when implementing a subset of the interface methods.</summary>
public abstract class ConsumeFilter : IConsumeFilter
{
    /// <inheritdoc/>
    public virtual ValueTask OnSubscribeExecutingAsync(ExecutingContext context)
    {
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask OnSubscribeExecutedAsync(ExecutedContext context)
    {
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask OnSubscribeExceptionAsync(ExceptionContext context)
    {
        return ValueTask.CompletedTask;
    }
}

public class FilterContext(ConsumerContext context) : ConsumerContext(context);

public sealed class ExecutingContext(ConsumerContext context, object?[] arguments) : FilterContext(context)
{
    public object?[] Arguments { get; init; } = arguments;
}

public sealed class ExecutedContext(ConsumerContext context, object? result) : FilterContext(context)
{
    public object? Result { get; init; } = result;
}

public sealed class ExceptionContext(ConsumerContext context, Exception exception) : FilterContext(context)
{
    public Exception Exception { get; init; } = exception;

    public bool ExceptionHandled { get; init; }

    public object? Result { get; init; }
}
