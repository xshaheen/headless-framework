// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Messaging.Storage.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging;

[PublicAPI]
public static class SetupSqlServerMessaging
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>Configures SQL Server message storage with a connection string.</summary>
        /// <param name="connectionString">The SQL Server connection string.</param>
        /// <returns>The setup builder for chaining.</returns>
        /// <exception cref="ArgumentException"><paramref name="connectionString"/> is null or whitespace.</exception>
        public MessagingSetupBuilder UseSqlServer(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);
            return setup.UseSqlServer(options => options.ConnectionString = connectionString);
        }

        /// <summary>Configures SQL Server message storage from a configuration section.</summary>
        /// <param name="configuration">Configuration containing <see cref="SqlServerOptions"/> values.</param>
        /// <returns>The setup builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null.</exception>
        public MessagingSetupBuilder UseSqlServer(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);
            return _AddSqlServerStorageCore(
                setup,
                services =>
                    services
                        .AddOptions<SqlServerOptions, SqlServerOptionsValidator>()
                        .Bind(configuration)
                        .Configure(options => options.Version = setup.Options.Version)
            );
        }

        /// <summary>Configures SQL Server message storage with an options action.</summary>
        /// <param name="configure">Action that configures <see cref="SqlServerOptions"/>.</param>
        /// <returns>The setup builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
        public MessagingSetupBuilder UseSqlServer(Action<SqlServerOptions> configure)
        {
            Argument.IsNotNull(configure);
            configure += options => options.Version = setup.Options.Version;
            return _AddSqlServerStorageCore(
                setup,
                services => services.Configure<SqlServerOptions, SqlServerOptionsValidator>(configure)
            );
        }

        /// <summary>Configures SQL Server message storage with access to the service provider.</summary>
        /// <param name="configure">Action that configures <see cref="SqlServerOptions"/> using resolved services.</param>
        /// <returns>The setup builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
        public MessagingSetupBuilder UseSqlServer(Action<SqlServerOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);
            configure += (options, _) => options.Version = setup.Options.Version;
            return _AddSqlServerStorageCore(
                setup,
                services => services.Configure<SqlServerOptions, SqlServerOptionsValidator>(configure)
            );
        }
    }

    private static MessagingSetupBuilder _AddSqlServerStorageCore(
        MessagingSetupBuilder setup,
        Action<IServiceCollection> configureOptions
    )
    {
        setup.RegisterExtension(new SqlServerMessagesOptionsExtension(configureOptions));
        return setup;
    }

    internal sealed class SqlServerMessagesOptionsExtension(Action<IServiceCollection> configureOptions)
        : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageStorageMarkerService("SqlServer"));
            configureOptions(services);
            services.AddSingleton<IDataStorage, SqlServerDataStorage>();
            services.AddSingleton<IStorageInitializer, SqlServerStorageInitializer>();
        }
    }
}
