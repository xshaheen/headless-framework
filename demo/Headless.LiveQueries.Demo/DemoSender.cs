// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Runtime.ExceptionServices;
using Mediator;

namespace Demo;

/// <summary>
/// Demo code, not part of the framework: a stand-in for the sender the Mediator source generator emits, which
/// this demo does not reference. It finds the <see cref="IRequestHandler{TRequest,TResponse}"/> of a request in
/// the current scope by reflection and calls it. An application references <c>Mediator.SourceGenerator</c> and
/// writes none of this.
/// </summary>
public sealed class DemoSender(IServiceProvider services) : ISender
{
    public ValueTask<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default
    )
    {
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(request.GetType(), typeof(TResponse));
        var handler = services.GetRequiredService(handlerType);
        var handle = handlerType.GetMethod(nameof(IRequestHandler<,>.Handle))!;

        try
        {
            return (ValueTask<TResponse>)handle.Invoke(handler, [request, cancellationToken])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is { } inner)
        {
            ExceptionDispatchInfo.Throw(inner);

            throw;
        }
    }

    public ValueTask<TResponse> Send<TResponse>(
        ICommand<TResponse> command,
        CancellationToken cancellationToken = default
    )
    {
        throw _OnlyRequests();
    }

    public ValueTask<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        throw _OnlyRequests();
    }

    public ValueTask<object?> Send(object message, CancellationToken cancellationToken = default)
    {
        throw _OnlyRequests();
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamQuery<TResponse> query,
        CancellationToken cancellationToken = default
    )
    {
        throw _OnlyRequests();
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default
    )
    {
        throw _OnlyRequests();
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamCommand<TResponse> command,
        CancellationToken cancellationToken = default
    )
    {
        throw _OnlyRequests();
    }

    public IAsyncEnumerable<object?> CreateStream(object message, CancellationToken cancellationToken = default)
    {
        throw _OnlyRequests();
    }

    private static NotSupportedException _OnlyRequests()
    {
        return new NotSupportedException("The demo sender dispatches IRequest<TResponse> messages only.");
    }
}
