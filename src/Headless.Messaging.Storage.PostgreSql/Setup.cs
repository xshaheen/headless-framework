// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;
using Headless.Constants;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
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
        /// <remarks>
        /// <paramref name="configuration"/> supplies the <see cref="PostgreSqlOptions"/> values directly, and
        /// the feature-owned schema is read from its <see cref="MessagingStorageOptions.SectionPath"/>
        /// subsection, so passing the configuration root binds <c>Headless:Messaging:Storage:Schema</c>.
        /// </remarks>
        /// <param name="configuration">Configuration containing <see cref="PostgreSqlOptions"/> values.</param>
        /// <returns>The setup builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null.</exception>
        public MessagingSetupBuilder UsePostgreSql(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);
            return _AddPostgreSqlStorageCore(
                setup,
                services =>
                {
                    services
                        .AddOptions<PostgreSqlOptions, PostgreSqlOptionsValidator>()
                        .Bind(configuration)
                        .Configure(options => options.Version = setup.Options.Version);

                    services
                        .AddOptions<MessagingStorageOptions>()
                        .Bind(configuration.GetSection(MessagingStorageOptions.SectionPath));
                }
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
                    supportsDelayedScheduling: true,
                    inboxCapability: MessagingInboxCapabilityTier.DurableDedupeOnly
                )
            );
            // The schema is feature-owned, so this provider only validates it, once, against PostgreSQL's
            // identifier rules. The EF-context storage path reuses this extension and is validated here too.
            services.AddOptions<MessagingStorageOptions, PostgreSqlMessagingStorageOptionsValidator>();
            configureOptions(services);
            services.AddSingleton<PostgreSqlDataStorage>();
            services.AddSingleton<IDataStorage>(sp => sp.GetRequiredService<PostgreSqlDataStorage>());
            services.AddSingleton<IDeliveryCoordinationResolver>(sp => sp.GetRequiredService<PostgreSqlDataStorage>());
            services.AddSingleton<IStorageInitializer, PostgreSqlStorageInitializer>();
        }
    }

    private sealed class PostgreSqlMessagingStorageOptionsValidator : AbstractValidator<MessagingStorageOptions>
    {
        public PostgreSqlMessagingStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).IsValidIdentifierFor(StorageProvider.PostgreSql);
        }
    }
}
