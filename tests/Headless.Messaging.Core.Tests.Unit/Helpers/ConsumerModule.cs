// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Registration;
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
            catalog.AddBusConsumer<TClass, TMessage>(bus.Identity, bus.EveryInstance, _Dispatch<TMessage>);
        }
        else
        {
            catalog.AddQueueConsumer<TClass, TMessage>(attribute.Identity, _Dispatch<TMessage>);
        }
    }

    // Mirrors the generated dispatch: a fresh instance per delivery, the lifecycle hooks around the typed call, and
    // disposal of the instance it created.
    private static async ValueTask _Dispatch<TMessage>(
        IServiceProvider services,
        ConsumeContext context,
        CancellationToken cancellationToken
    )
        where TMessage : class
    {
        var consumer = ActivatorUtilities.CreateInstance<TConsumer>(services);
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
