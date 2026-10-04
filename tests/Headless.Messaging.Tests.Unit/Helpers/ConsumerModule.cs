// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Helpers;

/// <summary>
/// Registers one attribute-declared consumer class the way the generated <c>MessagingModule</c> does: one catalog entry
/// per <see cref="IConsume{TMessage}"/> the class implements, under the identity, lane, and policy of its
/// <see cref="BusConsumerAttribute"/> or <see cref="QueueConsumerAttribute"/>, dispatched to a fresh instance built from
/// the delivery's scope.
/// </summary>
/// <remarks>
/// A generated module covers every consumer of its assembly, so a host built from one would carry every test consumer in
/// this project, and the generator rejects the duplicate identities that independent tests legitimately reuse. This
/// module carries exactly one class, so each test composes only the consumers it needs.
/// </remarks>
/// <typeparam name="TConsumer">The attribute-declared consumer class.</typeparam>
public sealed class ConsumerModule<TConsumer> : IMessagingModule
    where TConsumer : class
{
#pragma warning disable CA1000 // False positive: implements IMessagingModule's static abstract member, which messaging calls through the TModule constraint, never through this generic type.
    public static void Register(MessagingCatalogBuilder catalog)
#pragma warning restore CA1000
    {
        var attribute =
            typeof(TConsumer).GetCustomAttribute<MessageConsumerAttribute>()
            ?? throw new InvalidOperationException(
                $"{typeof(TConsumer).FullName} needs a [BusConsumer] or [QueueConsumer] attribute to be registered."
            );

        var messageTypes = typeof(TConsumer)
            .GetInterfaces()
            .Where(static x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IConsume<>))
            .Select(static x => x.GetGenericArguments()[0]);

        var add = typeof(ConsumerModule<TConsumer>).GetMethod(
            nameof(_Add),
            BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly
        )!;

        foreach (var messageType in messageTypes)
        {
            add.MakeGenericMethod(typeof(TConsumer), messageType).Invoke(null, [catalog, attribute]);
        }
    }

    private static void _Add<TClass, TMessage>(MessagingCatalogBuilder catalog, MessageConsumerAttribute attribute)
        where TClass : class, IConsume<TMessage>
        where TMessage : class
    {
        if (attribute is BusConsumerAttribute bus)
        {
            // The generator hands messaging the hook only for an every-instance class that implements it.
            var hook =
                bus.EveryInstance && typeof(IOnSubscriptionEstablished).IsAssignableFrom(typeof(TClass))
                    ? _OnSubscriptionEstablished
                    : (SubscriptionEstablishedDispatch?)null;
            catalog.AddBusConsumer<TClass, TMessage>(bus.Identity, bus.EveryInstance, _Dispatch<TMessage>, hook);
        }
        else
        {
            catalog.AddQueueConsumer<TClass, TMessage>(attribute.Identity, _Dispatch<TMessage>);
        }
    }

    // Mirrors the generated factory: the scope's own registration wins, and only a constructed instance is the caller's.
    private static TConsumer _Create(IServiceProvider services, out bool created)
    {
        var registered = services.GetService<TConsumer>();
        created = registered is null;
        return registered ?? ActivatorUtilities.CreateInstance<TConsumer>(services);
    }

    // Mirrors the generated subscription hook: the consumer built through the same factory as its deliveries.
    private static async ValueTask _OnSubscriptionEstablished(
        IServiceProvider services,
        SubscriptionEstablishedContext context,
        CancellationToken cancellationToken
    )
    {
        var consumer = _Create(services, out var created);
        try
        {
            await ((IOnSubscriptionEstablished)consumer).OnSubscriptionEstablishedAsync(context, cancellationToken);
        }
        finally
        {
            if (created)
            {
                await _DisposeAsync(consumer);
            }
        }
    }

    // Mirrors the generated dispatch: an instance per delivery, the lifecycle hooks around the typed call, and disposal
    // of an instance it created.
    private static async ValueTask _Dispatch<TMessage>(
        IServiceProvider services,
        ConsumeContext context,
        CancellationToken cancellationToken
    )
        where TMessage : class
    {
        var consumer = _Create(services, out var created);
        try
        {
            if (consumer is IConsumerLifecycle lifecycle)
            {
                await lifecycle.OnStartingAsync(cancellationToken);
                try
                {
                    await ((IConsume<TMessage>)consumer).ConsumeAsync(
                        (ConsumeContext<TMessage>)context,
                        cancellationToken
                    );
                }
                finally
                {
#pragma warning disable ERP022 // Mirrors the generated dispatch: a failing stop hook must not mask the delivery's outcome.
                    try
                    {
                        await lifecycle.OnStoppingAsync(cancellationToken);
                    }
                    catch
                    {
                        // The delivery's own outcome wins.
                    }
#pragma warning restore ERP022
                }
            }
            else
            {
                await ((IConsume<TMessage>)consumer).ConsumeAsync((ConsumeContext<TMessage>)context, cancellationToken);
            }
        }
        finally
        {
            if (created)
            {
                await _DisposeAsync(consumer);
            }
        }
    }

    private static async ValueTask _DisposeAsync(TConsumer consumer)
    {
        switch (consumer)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync();
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }
}

public static class ConsumerModuleRegistration
{
    /// <summary>Adds one attribute-declared consumer class, as its assembly's generated module would.</summary>
    public static MessagingSetupBuilder AddConsumer<TConsumer>(this MessagingSetupBuilder setup)
        where TConsumer : class => setup.AddModule<ConsumerModule<TConsumer>>();

    /// <summary>Contributes one attribute-declared consumer class, as its assembly's generated module would.</summary>
    public static MessagingContributionBuilder AddConsumer<TConsumer>(this MessagingContributionBuilder messaging)
        where TConsumer : class => messaging.AddModule<ConsumerModule<TConsumer>>();
}
