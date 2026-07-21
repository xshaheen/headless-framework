// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Reflection;
using Headless.Checks;
using Headless.DistributedLocks;
using Headless.Messaging.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Messaging.Configuration;

/// <summary>
/// A marker service used internally to verify that the messaging service has been registered on a <see cref="IServiceCollection"/>.
/// This service is registered when <c>AddHeadlessMessaging()</c> is called during dependency injection setup.
/// </summary>
internal sealed class MessagingMarkerService
{
    /// <summary>
    /// Gets or sets the name identifier for the messaging service.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the version of the messaging assembly.
    /// </summary>
    public string Version { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MessagingMarkerService"/> class with the specified name.
    /// Automatically retrieves and stores the messaging assembly version information.
    /// </summary>
    /// <param name="name">The name identifier for the messaging service.</param>
    public MessagingMarkerService(string name)
    {
        Name = name;

        try
        {
            Version = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).ProductVersion!;
        }
#pragma warning disable ERP022
        catch
        {
            Version = "N/A"; // Fallback in case of any error retrieving version info
        }
#pragma warning restore ERP022
    }
}

/// <summary>
/// A marker service used internally to verify that a message storage extension (e.g., SQL Server, PostgreSQL, MySQL, MongoDB)
/// has been registered on a <see cref="IServiceCollection"/>.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="MessageStorageMarkerService"/> class with the specified storage name.
/// </remarks>
/// <param name="name">The name identifier for the storage extension.</param>
internal sealed class MessageStorageMarkerService(string name)
{
    /// <summary>
    /// Gets or sets the name identifier for the storage extension (e.g., "SqlServer", "PostgreSql", "MySql").
    /// </summary>
    public string Name { get; set; } = name;
}

/// <summary>
/// A marker service used internally to verify that a message transport extension (e.g., RabbitMQ, Kafka, Azure Service Bus, NATS)
/// has been registered on a <see cref="IServiceCollection"/>.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="MessageQueueMarkerService"/> class with the specified message queue name.
/// </remarks>
/// <param name="name">The name identifier for the message transport extension.</param>
internal sealed class MessageQueueMarkerService(string name)
{
    /// <summary>
    /// Gets or sets the name identifier for the message transport extension (e.g., "RabbitMQ", "Kafka", "AzureServiceBus").
    /// </summary>
    public string Name { get; set; } = name;
}

/// <summary>
/// Provides a fluent API for fine-grained configuration of messaging services within a dependency injection container.
/// This builder allows registration of middleware, custom subscriber assembly scanning, and other messaging extensions.
/// </summary>
/// <remarks>
/// The <see cref="MessagingBuilder"/> is typically obtained through the <c>AddHeadlessMessaging()</c> extension method on <see cref="IServiceCollection"/>,
/// enabling a fluent configuration experience for messaging setup. All builder methods return the builder instance to support method chaining.
/// </remarks>
/// <remarks>
/// Initializes a new instance of the <see cref="MessagingBuilder"/> class with the specified service collection.
/// </remarks>
/// <param name="services">The <see cref="IServiceCollection"/> where messaging services are being configured.</param>
[PublicAPI]
public sealed class MessagingBuilder(IServiceCollection services, MessagingOptions? options = null)
{
    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> where messaging services are registered and configured.
    /// </summary>
    /// <remarks>
    /// This collection is used by all builder methods to register necessary services, middleware, and extensions
    /// in the application's dependency injection container.
    /// </remarks>
    public IServiceCollection Services { get; } = services;

    /// <summary>Registers publish middleware that intercepts every publish operation on the bus lane.</summary>
    /// <typeparam name="T">
    /// The middleware implementation type. Must implement <see cref="IPublishMiddleware{TContext}"/> with a
    /// context type that derives from <see cref="PublishContext"/>.
    /// </typeparam>
    /// <returns>A <see cref="MiddlewareRegistration"/> handle for chaining priority configuration.</returns>
    public MiddlewareRegistration AddBusPublishMiddleware<T>()
        where T : class
    {
        var contextType = _GetMiddlewareContextType(typeof(T), typeof(IPublishMiddleware<>), typeof(PublishContext));
        var serviceType = typeof(IPublishMiddleware<>).MakeGenericType(contextType);

        return _AddMiddleware<T>(
            MiddlewareDirection.Publish,
            MiddlewareScope.Bus,
            serviceType,
            contextType,
            messageType: null,
            groupName: null,
            lane: MessageLane.Bus
        );
    }

    /// <summary>Registers consume middleware that intercepts every consume operation on the bus lane.</summary>
    /// <typeparam name="T">
    /// The middleware implementation type. Must implement <see cref="IConsumeMiddleware{TContext}"/> with a
    /// context type that derives from <see cref="ConsumeContext"/>.
    /// </typeparam>
    /// <returns>A <see cref="MiddlewareRegistration"/> handle for chaining priority configuration.</returns>
    public MiddlewareRegistration AddBusConsumeMiddleware<T>()
        where T : class
    {
        var contextType = _GetMiddlewareContextType(typeof(T), typeof(IConsumeMiddleware<>), typeof(ConsumeContext));
        var serviceType = typeof(IConsumeMiddleware<>).MakeGenericType(contextType);

        return _AddMiddleware<T>(
            MiddlewareDirection.Consume,
            MiddlewareScope.Bus,
            serviceType,
            contextType,
            messageType: null,
            groupName: null,
            lane: MessageLane.Bus
        );
    }

    /// <summary>Registers publish middleware that intercepts publish operations for a specific message type.</summary>
    /// <typeparam name="TMiddleware">The middleware implementation type.</typeparam>
    /// <typeparam name="TMessage">The message type whose publish pipeline this middleware targets.</typeparam>
    /// <param name="lane">The message lane this middleware targets.</param>
    /// <returns>A <see cref="MiddlewareRegistration"/> handle for chaining priority configuration.</returns>
    public MiddlewareRegistration AddPublishMiddlewareFor<TMiddleware, TMessage>(MessageLane lane)
        where TMiddleware : class, IPublishMiddleware<PublishContext<TMessage>>
    {
        var contextType = typeof(PublishContext<TMessage>);
        var serviceType = typeof(IPublishMiddleware<>).MakeGenericType(contextType);

        return _AddMiddleware<TMiddleware>(
            MiddlewareDirection.Publish,
            MiddlewareScope.Message,
            serviceType,
            contextType,
            typeof(TMessage),
            groupName: null,
            lane
        );
    }

    /// <summary>Registers consume middleware that intercepts consume operations for a specific message type and consumer group.</summary>
    /// <typeparam name="TMiddleware">The middleware implementation type.</typeparam>
    /// <typeparam name="TMessage">The message type whose consume pipeline this middleware targets.</typeparam>
    /// <param name="groupName">The consumer group name this middleware is scoped to. Must not be null or whitespace.</param>
    /// <param name="lane">The message lane this middleware targets.</param>
    /// <returns>A <see cref="MiddlewareRegistration"/> handle for chaining priority configuration.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="groupName"/> is null or whitespace.</exception>
    public MiddlewareRegistration AddConsumeMiddlewareFor<TMiddleware, TMessage>(string groupName, MessageLane lane)
        where TMiddleware : class, IConsumeMiddleware<ConsumeContext<TMessage>>
        where TMessage : class
    {
        Argument.IsNotNullOrWhiteSpace(groupName);

        var contextType = typeof(ConsumeContext<TMessage>);
        var serviceType = typeof(IConsumeMiddleware<>).MakeGenericType(contextType);
        var resolvedGroupName = options?.ApplyGroupNamePrefix(groupName) ?? groupName;

        return _AddMiddleware<TMiddleware>(
            MiddlewareDirection.Consume,
            MiddlewareScope.Message,
            serviceType,
            contextType,
            typeof(TMessage),
            resolvedGroupName,
            lane
        );
    }

    private MiddlewareRegistration _AddMiddleware<TMiddleware>(
        MiddlewareDirection direction,
        MiddlewareScope scope,
        Type serviceType,
        Type contextType,
        Type? messageType,
        string? groupName,
        MessageLane lane
    )
        where TMiddleware : class
    {
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(serviceType, typeof(TMiddleware)));

        var descriptor = _GetOrAddRegistry()
            .AddOrGet(
                new MiddlewareDescriptorInput(
                    direction,
                    scope,
                    typeof(TMiddleware),
                    serviceType,
                    contextType,
                    messageType,
                    groupName,
                    lane
                )
            );

        return new MiddlewareRegistration(this, descriptor);
    }

    private IMiddlewareDescriptorRegistry _GetOrAddRegistry()
    {
        return GetOrAddMiddlewareDescriptorRegistry(Services);
    }

    internal static IMiddlewareDescriptorRegistry GetOrAddMiddlewareDescriptorRegistry(IServiceCollection services)
    {
        Argument.IsNotNull(services);

        var registry = services
            .Where(static descriptor =>
                descriptor.ServiceType == typeof(IMiddlewareDescriptorRegistry)
                || descriptor.ServiceType == typeof(MiddlewareDescriptorRegistry)
            )
            .Select(static descriptor => descriptor.ImplementationInstance)
            .OfType<IMiddlewareDescriptorRegistry>()
            .FirstOrDefault();

        if (registry is not null)
        {
            return registry;
        }

        registry = new MiddlewareDescriptorRegistry();
        services.RemoveAll<MiddlewareDescriptorRegistry>();
        services.RemoveAll<IMiddlewareDescriptorRegistry>();
        services.AddSingleton((MiddlewareDescriptorRegistry)registry);
        services.AddSingleton(registry);

        return registry;
    }

    private static Type _GetMiddlewareContextType(Type middlewareType, Type openMiddlewareType, Type baseContextType)
    {
        var contextTypes = middlewareType
            .GetInterfaces()
            .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == openMiddlewareType)
            .Select(type => type.GetGenericArguments()[0])
            .ToArray();

        if (contextTypes.Length == 0)
        {
            throw new ArgumentException(
                $"Middleware `{middlewareType.FullName}` must implement `{openMiddlewareType.Name}`.",
                nameof(middlewareType)
            );
        }

        if (contextTypes.Length > 1)
        {
            throw new ArgumentException(
                $"Middleware `{middlewareType.FullName}` must implement exactly one `{openMiddlewareType.Name}` interface.",
                nameof(middlewareType)
            );
        }

        var contextType = contextTypes[0];

        if (!baseContextType.IsAssignableFrom(contextType))
        {
            throw new ArgumentException(
                $"Middleware `{middlewareType.FullName}` context `{contextType.FullName}` must derive from `{baseContextType.FullName}`.",
                nameof(middlewareType)
            );
        }

        return contextType;
    }

    /// <summary>
    /// Registers an <see cref="IDistributedLock"/> instance for messaging's isolated lock scope
    /// and enables <see cref="MessagingOptions.UseStorageLock"/>.
    /// </summary>
    /// <param name="provider">The lock provider instance to use for distributed retry coordination.</param>
    /// <remarks>
    /// Messaging keeps its lock provider under an internal keyed-DI key so it never conflicts with
    /// any other <see cref="IDistributedLock"/> registered at the application level.
    /// Calling this method implicitly sets <c>UseStorageLock = true</c>.
    /// Last-wins: calling this method (or its factory overload) more than once replaces any prior
    /// messaging lock provider registration.
    /// </remarks>
    public MessagingBuilder UseDistributedLock(IDistributedLock provider)
    {
        Argument.IsNotNull(provider);

        _RemoveExistingMessagingLockProvider();
        Services.AddKeyedSingleton(MessagingKeys.LockProvider, provider);
        Services.Configure<MessagingOptions>(o => o.UseStorageLock = true);
        return this;
    }

    /// <summary>
    /// Registers a factory-resolved <see cref="IDistributedLock"/> for messaging's isolated lock scope
    /// and enables <see cref="MessagingOptions.UseStorageLock"/>.
    /// </summary>
    /// <param name="factory">A factory delegate that receives the <see cref="IServiceProvider"/> and returns the lock provider.</param>
    /// <remarks>
    /// Use this overload when the provider itself depends on other DI-registered services.
    /// Messaging keeps its lock provider under an internal keyed-DI key so it never conflicts with
    /// any other <see cref="IDistributedLock"/> registered at the application level.
    /// Calling this method implicitly sets <c>UseStorageLock = true</c>.
    /// Last-wins: calling this method (or its instance overload) more than once replaces any prior
    /// messaging lock provider registration.
    /// </remarks>
    public MessagingBuilder UseDistributedLock(Func<IServiceProvider, IDistributedLock> factory)
    {
        Argument.IsNotNull(factory);

        _RemoveExistingMessagingLockProvider();
        Services.AddKeyedSingleton<IDistributedLock>(MessagingKeys.LockProvider, (sp, _) => factory(sp));
        Services.Configure<MessagingOptions>(o => o.UseStorageLock = true);
        return this;
    }

    private void _RemoveExistingMessagingLockProvider()
    {
        for (var i = Services.Count - 1; i >= 0; i--)
        {
            var descriptor = Services[i];
            if (
                descriptor.ServiceType == typeof(IDistributedLock)
                && descriptor.IsKeyedService
                && Equals(descriptor.ServiceKey, MessagingKeys.LockProvider)
            )
            {
                Services.RemoveAt(i);
            }
        }
    }
}
