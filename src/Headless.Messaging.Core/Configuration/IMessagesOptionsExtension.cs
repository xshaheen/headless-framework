// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Configuration;

/// <summary>
/// Defines an extension mechanism for customizing messaging configuration during dependency injection setup.
/// Implementations of this interface allow third-party libraries and custom code to register services
/// and configure messaging functionality without modifying the core messaging assembly.
/// </summary>
/// <remarks>
/// Extensions are registered through <see cref="MessagingOptions.RegisterExtension(IMessagesOptionsExtension)"/>
/// and are executed during the <c>AddMessaging()</c> service registration process.
/// This allows modular and composable configuration of storage backends, transport implementations, and other messaging components.
/// </remarks>
public interface IMessagesOptionsExtension
{
    /// <summary>
    /// Called during messaging service registration to add and configure child services in the dependency injection container.
    /// </summary>
    /// <remarks>
    /// Implementations should use <c>TryAdd</c> or <c>Replace</c> extension methods to register services,
    /// allowing other extensions or user code to override registrations if needed.
    /// </remarks>
    /// <param name="services">
    /// The <see cref="IServiceCollection"/> where services should be registered.
    /// This is the same collection being configured for the application's dependency injection.
    /// </param>
    /// <example>
    /// <code>
    /// public void AddServices(IServiceCollection services)
    /// {
    ///     services.TryAddSingleton&lt;IDataStorage, SqlServerDataStorage&gt;();
    ///     services.TryAddSingleton&lt;ITransport, RabbitMQTransport&gt;();
    /// }
    /// </code>
    /// </example>
    void AddServices(IServiceCollection services);
}
