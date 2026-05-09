// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Reflection;
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
/// This builder allows registration of subscriber filters, custom subscriber assembly scanning, and other messaging extensions.
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
    /// This collection is used by all builder methods to register necessary services, filters, and extensions
    /// in the application's dependency injection container.
    /// </remarks>
    public IServiceCollection Services { get; } = services;

    /// <summary>
    /// Registers a subscriber filter that will be applied to all subscriber method executions.
    /// Filters can be used for cross-cutting concerns such as logging, error handling, and transaction management.
    /// </summary>
    /// <typeparam name="T">
    /// The type of the filter to register. Must implement <see cref="IConsumeFilter"/> and be instantiable.
    /// The filter is registered with a scoped lifetime; a new instance is created per consumed message.
    /// </typeparam>
    /// <returns>The current <see cref="MessagingBuilder"/> instance to support fluent method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Multiple filter types can be registered by calling this method with different type arguments. The
    /// executing phase runs in registration order, and the executed and exception phases run in reverse,
    /// matching ASP.NET Core MVC filter pipeline semantics.
    /// </para>
    /// <para>
    /// Registration is idempotent under the same type argument — calling
    /// <c>AddSubscribeFilter&lt;T&gt;()</c> twice with the same <typeparamref name="T"/> registers the
    /// filter once. Calls with different type arguments register additional filters.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddHeadlessMessaging(options =>
    /// {
    ///     options.UseRabbitMq(r => r.HostName = "localhost");
    ///     options.UseSqlServer("connection_string");
    /// })
    /// .AddSubscribeFilter&lt;LoggingFilter&gt;()
    /// .AddSubscribeFilter&lt;ExceptionHandlingFilter&gt;();
    /// </code>
    /// </example>
    public MessagingBuilder AddSubscribeFilter<T>()
        where T : class, IConsumeFilter
    {
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IConsumeFilter, T>());
        return this;
    }

    /// <summary>
    /// Registers a publish filter applied to every <see cref="IMessagePublisher.PublishAsync"/> and
    /// <see cref="IScheduledPublisher.PublishDelayAsync"/> call.
    /// </summary>
    /// <typeparam name="T">
    /// The filter type. Must implement <see cref="IPublishFilter"/>. Registered with scoped lifetime;
    /// a new instance is created per publish operation by the pipeline's per-call DI scope.
    /// </typeparam>
    /// <returns>The current <see cref="MessagingBuilder"/> instance to support fluent method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Multiple filter types execute the publishing phase in registration order; the published and
    /// exception phases run in reverse, mirroring <see cref="AddSubscribeFilter{T}"/> and ASP.NET Core
    /// MVC filter pipeline semantics.
    /// </para>
    /// <para>
    /// Registration is idempotent under the same type argument. Filters can mutate
    /// <see cref="PublishingContext.Options"/> via the <c>with</c> expression; the mutated value is
    /// passed to <see cref="MessagePublishRequestFactory"/> and inherits the existing 4-case integrity policy.
    /// </para>
    /// </remarks>
    public MessagingBuilder AddPublishFilter<T>()
        where T : class, IPublishFilter
    {
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IPublishFilter, T>());
        return this;
    }
}
