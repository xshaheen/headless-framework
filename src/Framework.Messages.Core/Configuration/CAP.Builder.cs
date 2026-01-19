// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Reflection;
using Framework.Checks;
using Framework.Messages.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Framework.Messages.Configuration;

/// <summary>
/// A marker service used internally to verify that the CAP service has been registered on a <see cref="IServiceCollection"/>.
/// This service is registered when <c>AddCap()</c> is called during dependency injection setup.
/// </summary>
public class CapMarkerService
{
    /// <summary>
    /// Gets or sets the name identifier for the CAP service.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the version of the CAP assembly.
    /// </summary>
    public string Version { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CapMarkerService"/> class with the specified name.
    /// Automatically retrieves and stores the CAP assembly version information.
    /// </summary>
    /// <param name="name">The name identifier for the CAP service.</param>
    public CapMarkerService(string name)
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
/// A marker service used internally to verify that a CAP storage extension (e.g., SQL Server, PostgreSQL, MySQL, MongoDB)
/// has been registered on a <see cref="IServiceCollection"/>.
/// </summary>
public class CapStorageMarkerService
{
    /// <summary>
    /// Gets or sets the name identifier for the storage extension (e.g., "SqlServer", "PostgreSql", "MySql").
    /// </summary>
    public string Name { get; set; }

    //public IDictionary<string, string> MetaData { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CapStorageMarkerService"/> class with the specified storage name.
    /// </summary>
    /// <param name="name">The name identifier for the storage extension.</param>
    public CapStorageMarkerService(string name)
    {
        Name = name;
    }
}

/// <summary>
/// A marker service used internally to verify that a CAP message transport extension (e.g., RabbitMQ, Kafka, Azure Service Bus, NATS)
/// has been registered on a <see cref="IServiceCollection"/>.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="CapMessageQueueMakerService"/> class with the specified message queue name.
/// </remarks>
/// <param name="name">The name identifier for the message transport extension.</param>
public class CapMessageQueueMakerService(string name)
{
    /// <summary>
    /// Gets or sets the name identifier for the message transport extension (e.g., "RabbitMQ", "Kafka", "AzureServiceBus").
    /// </summary>
    public string Name { get; set; } = name;
}

/// <summary>
/// Provides a fluent API for fine-grained configuration of CAP services within a dependency injection container.
/// This builder allows registration of subscriber filters, custom subscriber assembly scanning, and other CAP extensions.
/// </summary>
/// <remarks>
/// The <see cref="CapBuilder"/> is typically obtained through the <c>AddCap()</c> extension method on <see cref="IServiceCollection"/>,
/// enabling a fluent configuration experience for CAP setup. All builder methods return the builder instance to support method chaining.
/// </remarks>
/// <remarks>
/// Initializes a new instance of the <see cref="CapBuilder"/> class with the specified service collection.
/// </remarks>
/// <param name="services">The <see cref="IServiceCollection"/> where CAP services are being configured.</param>
public sealed class CapBuilder(IServiceCollection services)
{
    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> where CAP services are registered and configured.
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
    /// The filter is registered with a scoped lifetime, meaning a new instance is created per request/scope.
    /// </typeparam>
    /// <returns>The current <see cref="CapBuilder"/> instance to support fluent method chaining.</returns>
    /// <remarks>
    /// Multiple filters can be registered by calling this method multiple times. Filters are executed in the order
    /// they are registered, allowing for layered processing of subscriber messages.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddCap(options =>
    /// {
    ///     options.UseRabbitMQ(r => r.HostName = "localhost");
    ///     options.UseSqlServer("connection_string");
    /// })
    /// .AddSubscribeFilter&lt;LoggingFilter&gt;()
    /// .AddSubscribeFilter&lt;ExceptionHandlingFilter&gt;();
    /// </code>
    /// </example>
    public CapBuilder AddSubscribeFilter<T>()
        where T : class, IConsumeFilter
    {
        Services.TryAddScoped<IConsumeFilter, T>();
        return this;
    }
}
