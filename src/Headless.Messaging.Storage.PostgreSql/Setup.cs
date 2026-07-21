// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Messaging.Storage.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging;

/// <summary>
/// Extension methods for configuring PostgreSQL as the messaging storage backend.
/// </summary>
[PublicAPI]
public static class SetupPostgreSqlMessaging
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>Configures PostgreSQL message storage with a connection string.</summary>
        /// <param name="connectionString">The PostgreSQL connection string.</param>
        /// <returns>The setup builder for chaining.</returns>
        /// <exception cref="ArgumentException"><paramref name="connectionString"/> is null or whitespace.</exception>
        public MessagingSetupBuilder UsePostgreSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);
            return setup.UsePostgreSql(options => options.ConnectionString = connectionString);
        }

        /// <summary>Configures PostgreSQL message storage from a configuration section.</summary>
        /// <param name="configuration">Configuration containing <see cref="PostgreSqlOptions"/> values.</param>
        /// <returns>The setup builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null.</exception>
        public MessagingSetupBuilder UsePostgreSql(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);
            return _AddPostgreSqlStorageCore(
                setup,
                services =>
                    services
                        .AddOptions<PostgreSqlOptions, PostgreSqlOptionsValidator>()
                        .Bind(configuration)
                        .Configure(options => options.Version = setup.Options.Version)
            );
        }

        /// <summary>Configures PostgreSQL message storage with an options action.</summary>
        /// <param name="configure">Action that configures <see cref="PostgreSqlOptions"/>.</param>
        /// <returns>The setup builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
        public MessagingSetupBuilder UsePostgreSql(Action<PostgreSqlOptions> configure)
        {
            Argument.IsNotNull(configure);
            configure += options => options.Version = setup.Options.Version;
            return _AddPostgreSqlStorageCore(
                setup,
                services => services.Configure<PostgreSqlOptions, PostgreSqlOptionsValidator>(configure)
            );
        }

        /// <summary>Configures PostgreSQL message storage with access to the service provider.</summary>
        /// <param name="configure">Action that configures <see cref="PostgreSqlOptions"/> using resolved services.</param>
        /// <returns>The setup builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
        public MessagingSetupBuilder UsePostgreSql(Action<PostgreSqlOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);
            configure += (options, _) => options.Version = setup.Options.Version;
            return _AddPostgreSqlStorageCore(
                setup,
                services => services.Configure<PostgreSqlOptions, PostgreSqlOptionsValidator>(configure)
            );
        }
    }

    private static MessagingSetupBuilder _AddPostgreSqlStorageCore(
        MessagingSetupBuilder setup,
        Action<IServiceCollection> configureOptions
    )
    {
        setup.RegisterExtension(new PostgreSqlMessagesOptionsExtension(configureOptions));
        return setup;
    }

    internal sealed class PostgreSqlMessagesOptionsExtension(Action<IServiceCollection> configureOptions)
        : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageStorageMarkerService("PostgreSql"));
            services.AddMessagingProviderCapabilities(
                MessagingProviderCapabilities.Storage(
                    "PostgreSql",
                    [MessageLane.Bus, MessageLane.Queue],
                    supportsDelayedScheduling: true
                )
            );
            configureOptions(services);
            services.AddSingleton<IDataStorage, PostgreSqlDataStorage>();
            services.AddSingleton<IStorageInitializer, PostgreSqlStorageInitializer>();
        }
    }
}
