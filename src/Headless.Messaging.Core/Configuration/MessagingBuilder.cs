// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Reflection;
using Headless.Checks;
using Headless.Messaging.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Messaging.Configuration;

/// <summary>
/// A marker service used internally to verify that the messaging service has been registered on a <see cref="IServiceCollection"/>.
/// This service is registered when <c>AddHeadlessMessaging()</c> is called during dependency injection setup.
/// </summary>
public class MessagingMarkerService
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
public class MessageStorageMarkerService(string name)
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
public class MessageQueueMarkerService(string name)
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
public sealed class MessagingBuilder(IServiceCollection services)
{
    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> where messaging services are registered and configured.
    /// </summary>
    /// <remarks>
    /// This collection is used by all builder methods to register necessary services, middleware, and extensions
    /// in the application's dependency injection container.
    /// </remarks>
    public IServiceCollection Services { get; } = services;

    /// <summary>Registers object-typed publish middleware that runs around every publish operation.</summary>
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
            groupName: null
        );
    }

    /// <summary>Registers object-typed consume middleware that runs around every consume operation.</summary>
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
            groupName: null
        );
    }

    /// <summary>Registers publish middleware for a specific message type.</summary>
    public MiddlewareRegistration AddPublishMiddlewareFor<TMiddleware, TMessage>()
        where TMiddleware : class, IPublishMiddleware<PublishingContext<TMessage>>
    {
        var contextType = typeof(PublishingContext<TMessage>);
        var serviceType = typeof(IPublishMiddleware<>).MakeGenericType(contextType);

        return _AddMiddleware<TMiddleware>(
            MiddlewareDirection.Publish,
            MiddlewareScope.Message,
            serviceType,
            contextType,
            typeof(TMessage),
            groupName: null
        );
    }

    /// <summary>Registers consume middleware for a specific message type and consumer group.</summary>
    public MiddlewareRegistration AddConsumeMiddlewareFor<TMiddleware, TMessage>(string groupName)
        where TMiddleware : class, IConsumeMiddleware<ConsumeContext<TMessage>>
        where TMessage : class
    {
        Argument.IsNotNullOrWhiteSpace(groupName);

        var contextType = typeof(ConsumeContext<TMessage>);
        var serviceType = typeof(IConsumeMiddleware<>).MakeGenericType(contextType);

        return _AddMiddleware<TMiddleware>(
            MiddlewareDirection.Consume,
            MiddlewareScope.Message,
            serviceType,
            contextType,
            typeof(TMessage),
            groupName
        );
    }

    private MiddlewareRegistration _AddMiddleware<TMiddleware>(
        MiddlewareDirection direction,
        MiddlewareScope scope,
        Type serviceType,
        Type contextType,
        Type? messageType,
        string? groupName
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
                    groupName
                )
            );

        return new MiddlewareRegistration(this, descriptor);
    }

    private IMiddlewareDescriptorRegistry _GetOrAddRegistry()
    {
        var descriptor = Services.FirstOrDefault(static descriptor =>
            descriptor.ServiceType == typeof(IMiddlewareDescriptorRegistry)
        );

        if (descriptor?.ImplementationInstance is IMiddlewareDescriptorRegistry registry)
        {
            return registry;
        }

        registry = new MiddlewareDescriptorRegistry();
        Services.TryAddSingleton(registry);
        Services.TryAddSingleton<IMiddlewareDescriptorRegistry>(registry);

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
}
