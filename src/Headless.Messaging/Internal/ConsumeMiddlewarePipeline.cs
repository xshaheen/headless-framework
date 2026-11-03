// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Internal;

internal sealed class ConsumeMiddlewarePipeline(
    IServiceProvider serviceProvider,
    IRuntimeConsumerRegistry runtimeRegistry,
    IMiddlewareDescriptorRegistry? descriptorRegistry = null,
    ILogger<ConsumeMiddlewarePipeline>? logger = null,
    IConsumeContextAccessor? consumeContextAccessor = null
) : IConsumeMiddlewarePipeline
{
    // Cache the tracked-type HashSet per (registry, direction). The registry instance is stable for
    // the application's lifetime, so we never recompute the set on the hot consume path.
    private static readonly ConditionalWeakTable<
        IMiddlewareDescriptorRegistry,
        ConcurrentDictionary<MiddlewareDirection, HashSet<Type>>
    > _TrackedTypesByRegistry = [];

    // Registration existence is fixed once the container is built, so cache per closed context type whether any
    // direct middleware is registered at all and skip the scoped GetServices + array build on the (common)
    // zero-middleware consume path. A null probe (non-conforming container) always takes the slow path.
    private readonly IServiceProviderIsService? _serviceProbe = serviceProvider.GetService<IServiceProviderIsService>();

    private readonly ConcurrentDictionary<ConsumeMessageBinding, bool> _hasDirectMiddleware = new();

    public async Task<ConsumerExecutedResult> ExecuteAsync(
        ConsumerContext context,
        object messageInstance,
        CancellationToken cancellationToken = default
    )
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        return await ExecuteInScopeAsync(context, messageInstance, scope.ServiceProvider, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ConsumerExecutedResult> ExecuteInScopeAsync(
        ConsumerContext context,
        object messageInstance,
        IServiceProvider provider,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var descriptor = context.ConsumerDescriptor;
        var originHeaders = context.MediumMessage.Origin.Headers;
        var tenantId = TenantContextScope.ResolveTenantId(originHeaders, logger);

        // The descriptor remains authoritative. Validate the compatibility header before any middleware or
        // handler side effect, and warn when a recognized value disagrees with the registered lane.
        _ValidateLaneHeader(originHeaders, descriptor, logger);

        // The binding was captured where the consumer's message type was a generic argument, so the typed context and
        // typed middleware calls below are ordinary compiled generic code.
        var binding =
            descriptor.MessageBinding
            ?? throw new InvalidOperationException(
                $"Consumer {descriptor.ConsumerType.FullName ?? descriptor.ConsumerType.Name} of message "
                    + $"'{descriptor.MessageName}' declares no message type."
            );

        var consumeHeaders = new MessageHeader(originHeaders);
        var consumeContext = binding.CreateContext(
            messageInstance,
            context.MediumMessage,
            consumeHeaders,
            tenantId,
            descriptor.Lane
        );
        consumeContext.SetCancellationToken(cancellationToken);
        consumeContext.UnitOfWork = context.UnitOfWork;
        consumeContext.IsResponder = descriptor.IsResponder;
        var previousConsumeContext = consumeContextAccessor?.Current;

        try
        {
            consumeContextAccessor?.Current = consumeContext;

            var middleware = _ResolveMiddleware(provider, consumeContext, binding, descriptor);

            if (middleware.Length == 0)
            {
                // Zero-middleware fast path: with no ring to wrap there is no delegate chain to build and
                // no completion flag to track — that flag only feeds the middleware error filters below.
                await _InvokeInnerAsync(descriptor, consumeContext, provider).ConfigureAwait(false);
                consumeContext.MarkCompleted();
            }
            else
            {
                // Hoisted into a single StrongBox so the per-middleware wiring captures one loop-invariant
                // reference instead of allocating a fresh `() => innerRingCompleted` delegate per middleware.
                var innerRingCompleted = new StrongBox<bool>(value: false);

                Func<ValueTask> next = async () =>
                {
                    await _InvokeInnerAsync(descriptor, consumeContext, provider).ConfigureAwait(false);

                    innerRingCompleted.Value = true;
                    consumeContext.MarkCompleted();
                };

                for (var i = middleware.Length - 1; i >= 0; i--)
                {
                    var current = middleware[i];
                    var innerNext = next;
                    next = () => _InvokeAsync(current, binding, consumeContext, innerNext, innerRingCompleted);
                }

                await next().ConfigureAwait(false);
            }
        }
        finally
        {
            consumeContextAccessor?.Current = previousConsumeContext;
        }

        string? callbackName = null;

        // A responder answers through the reply, never through a callback, so a callback name the message carries is
        // ignored for it rather than answering the caller twice.
        if (!consumeContext.IsResponseSuppressed && !descriptor.IsResponder)
        {
            callbackName =
                consumeContext.ResponseDestination
                ?? (
                    consumeHeaders.TryGetValue(Headers.CallbackName, out var wireCallback)
                    && !string.IsNullOrWhiteSpace(wireCallback)
                        ? wireCallback
                        : null
                );
        }

        var callbackHeaders = consumeContext.ResponseHeaders;
        return new ConsumerExecutedResult(
            consumeContext.Response,
            consumeContext.ResponseMessageType,
            context.MediumMessage.Origin.Id,
            callbackName,
            callbackHeaders,
            consumeContext.ResponseCallbackName
        )
        {
            Reply = consumeContext.Reply,
            ReplyType = consumeContext.ReplyType,
        };
    }

    private async ValueTask _InvokeInnerAsync(
        ConsumerExecutorDescriptor descriptor,
        ConsumeContext consumeContext,
        IServiceProvider provider
    )
    {
        // An attribute-declared consumer runs its generated dispatch, which builds the class and calls the typed
        // ConsumeAsync.
        if (descriptor.Dispatch is { } dispatch)
        {
            await dispatch(provider, consumeContext, consumeContext.CancellationToken).ConfigureAwait(false);

            return;
        }

        if (
            descriptor.HandlerId is { Length: > 0 } handlerId
            && runtimeRegistry.TryGetInvoker(
                descriptor.MessageName,
                descriptor.SubscriptionName,
                handlerId,
                descriptor.Lane,
                out var runtimeInvoker
            )
        )
        {
            await runtimeInvoker
                .InvokeAsync(consumeContext, provider, consumeContext.CancellationToken)
                .ConfigureAwait(false);

            return;
        }

        // Consumers run through generated dispatch or a runtime subscription's delegate; reaching here means the
        // runtime subscription was detached after its delivery was selected.
        throw new InvalidOperationException(
            $"No handler is attached for consumer {descriptor.ConsumerType.FullName ?? descriptor.ConsumerType.Name} "
                + $"of message '{descriptor.MessageName}' on lane {descriptor.Lane}."
        );
    }

    private async ValueTask _InvokeAsync(
        object middleware,
        ConsumeMessageBinding binding,
        ConsumeContext context,
        Func<ValueTask> innerNext,
        StrongBox<bool> innerRingCompleted
    )
    {
        try
        {
            await _InvokeMiddlewareAsync(middleware, binding, context, innerNext).ConfigureAwait(false);
            context.MarkCompleted();
        }
        catch (Exception ex) when (innerRingCompleted.Value)
        {
            logger?.ConsumePostSuccessMiddlewareFailed(ex, middleware.GetType().FullName ?? middleware.GetType().Name);
            return;
        }
        catch (Exception ex) when (_ShouldRethrowOce(ex, context.CancellationToken))
        {
            throw new OperationCanceledException(context.CancellationToken);
        }

        if (context.CancellationToken.IsCancellationRequested)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static ValueTask _InvokeMiddlewareAsync(
        object middleware,
        ConsumeMessageBinding binding,
        ConsumeContext context,
        Func<ValueTask> next
    )
    {
        return middleware is IConsumeMiddleware<ConsumeContext> busMiddleware
            ? busMiddleware.InvokeAsync(context, next)
            : binding.InvokeMiddlewareAsync(middleware, context, next);
    }

    private object[] _ResolveMiddleware(
        IServiceProvider provider,
        ConsumeContext context,
        ConsumeMessageBinding binding,
        ConsumerExecutorDescriptor consumer
    )
    {
        var shared = _ResolveSharedMiddleware(provider, context, binding);
        var tuned = consumer.Middleware;
        if (tuned.Count == 0)
        {
            return shared;
        }

        // Middleware tuned onto one consumer runs innermost, after the global and per-message middleware.
        var middleware = new object[shared.Length + tuned.Count];
        shared.CopyTo(middleware, 0);
        for (var index = 0; index < tuned.Count; index++)
        {
            middleware[shared.Length + index] = provider.GetRequiredService(tuned[index]);
        }

        return middleware;
    }

    private object[] _ResolveSharedMiddleware(
        IServiceProvider provider,
        ConsumeContext context,
        ConsumeMessageBinding binding
    )
    {
        // _ResolveDirectMiddleware already materializes a fresh array; reuse it directly instead of copying again.
        var directMiddleware = _ResolveDirectMiddleware(provider, binding);

        if (descriptorRegistry is null)
        {
            return directMiddleware;
        }

        if (descriptorRegistry.TryGetConsumeDescriptors(context.MessageType, context.Lane, out var descriptors))
        {
            return
            [
                .. MiddlewareDescriptorResolver.Resolve(provider, descriptors),
                .. _GetUntrackedDirectMiddleware(directMiddleware, MiddlewareDirection.Consume),
            ];
        }

        // A lane with no descriptors still runs middleware registered straight into DI, but never middleware the
        // builder registered for the other lane: the registry is the only source of lane membership for those types.
        // Most hosts have nothing registered straight into DI, so skip the filter and its allocation for them.
        return directMiddleware.Length == 0
            ? directMiddleware
            : [.. _GetUntrackedDirectMiddleware(directMiddleware, MiddlewareDirection.Consume)];
    }

    private object[] _ResolveDirectMiddleware(IServiceProvider provider, ConsumeMessageBinding binding)
    {
        if (
            _serviceProbe is not null
            && !_hasDirectMiddleware.GetOrAdd(
                binding,
                static (binding, self) => self._HasAnyDirectMiddleware(binding),
                this
            )
        )
        {
            return [];
        }

        var busMiddleware = provider.GetServices<IConsumeMiddleware<ConsumeContext>>().Cast<object>();
        var typedMiddleware = provider
            .GetServices(binding.MiddlewareServiceType)
            .Where(static middleware => middleware is not null)
            .Cast<object>();

        return [.. busMiddleware, .. typedMiddleware];
    }

    private bool _HasAnyDirectMiddleware(ConsumeMessageBinding binding)
    {
        return _serviceProbe!.IsService(typeof(IConsumeMiddleware<ConsumeContext>))
            || _serviceProbe.IsService(binding.MiddlewareServiceType);
    }

    private IEnumerable<object> _GetUntrackedDirectMiddleware(
        IEnumerable<object> middleware,
        MiddlewareDirection direction
    )
    {
        var perDirection = _TrackedTypesByRegistry.GetValue(descriptorRegistry!, static _ => new());
        var trackedTypes = perDirection.GetOrAdd(
            direction,
            (dir, registry) =>
                [
                    .. registry
                        .Descriptors.Where(descriptor => descriptor.Direction == dir)
                        .Select(descriptor => descriptor.MiddlewareType),
                ],
            descriptorRegistry!
        );

        return middleware.Where(current => !trackedTypes.Contains(current.GetType()));
    }

    private static void _ValidateLaneHeader(
        IDictionary<string, string?> headers,
        ConsumerExecutorDescriptor descriptor,
        ILogger? logger
    )
    {
        if (!headers.TryGetValue(Headers.Intent, out var wireIntent) || string.IsNullOrWhiteSpace(wireIntent))
        {
            return;
        }

        MessageLane wireLane;
        try
        {
            wireLane = MessageLaneCompatibility.FromWireValue(wireIntent);
        }
        catch (InvalidOperationException)
        {
            logger?.UnsupportedMessageLaneHeader(descriptor.MessageName, wireIntent);
            throw;
        }

        var consumerLane = MessageLaneCompatibility.ToWireValue(descriptor.Lane);
        if (wireLane != descriptor.Lane)
        {
            logger?.ConsumeIntentMismatch(descriptor.MessageName, wireIntent, consumerLane);
        }
    }

    private static bool _ShouldRethrowOce(Exception exception, CancellationToken cancellationToken)
    {
        if (
            exception is OperationCanceledException operationCanceledException
            && operationCanceledException.CancellationToken == cancellationToken
        )
        {
            return true;
        }

        if (exception is AggregateException aggregateException)
        {
            return aggregateException.InnerExceptions.Any(inner => _ShouldRethrowOce(inner, cancellationToken));
        }

        return false;
    }
}

internal interface IConsumeMiddlewarePipeline
{
    Task<ConsumerExecutedResult> ExecuteInScopeAsync(
        ConsumerContext context,
        object messageInstance,
        IServiceProvider provider,
        CancellationToken cancellationToken = default
    );

    Task<ConsumerExecutedResult> ExecuteAsync(
        ConsumerContext context,
        object messageInstance,
        CancellationToken cancellationToken = default
    );
}
