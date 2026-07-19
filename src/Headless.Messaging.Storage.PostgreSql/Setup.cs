// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.CommitCoordination;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Messaging.Storage.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
        /// <summary>
        /// Configures PostgreSQL as the message storage using the specified connection string.
        /// </summary>
        /// <param name="connectionString">The PostgreSQL database connection string.</param>
        /// <returns>The setup builder for chaining.</returns>
        public MessagingSetupBuilder UsePostgreSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UsePostgreSql(opt => opt.ConnectionString = connectionString);
        }

        /// <summary>
        /// Configures PostgreSQL as the message storage, binding and validating
        /// <see cref="PostgreSqlOptions"/> from configuration.
        /// </summary>
        /// <param name="configuration">Configuration section containing <see cref="PostgreSqlOptions"/> values.</param>
        /// <returns>The setup builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UsePostgreSql(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            return _AddPostgreSqlStorageCore(
                setup,
                services =>
                    services
                        .AddOptions<PostgreSqlOptions, PostgreSqlOptionsValidator>()
                        .Bind(configuration)
                        .Configure(x => x.Version = setup.Options.Version),
                outboxProbe: options => configuration.Bind(options)
            );
        }

        /// <summary>
        /// Configures PostgreSQL as the message storage with custom options.
        /// </summary>
        /// <param name="configure">Action to configure PostgreSQL options.</param>
        /// <returns>The setup builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UsePostgreSql(Action<PostgreSqlOptions> configure)
        {
            Argument.IsNotNull(configure);

            configure += x => x.Version = setup.Options.Version;

            return _AddPostgreSqlStorageCore(
                setup,
                services => services.Configure<PostgreSqlOptions, PostgreSqlOptionsValidator>(configure),
                outboxProbe: configure
            );
        }

        /// <summary>
        /// Configures PostgreSQL as the message storage, configuring <see cref="PostgreSqlOptions"/>
        /// with access to the resolved service provider (for example to resolve secrets or connection
        /// settings from DI).
        /// </summary>
        /// <remarks>
        /// This overload targets the raw ADO.NET storage path. The transactional outbox auto-wiring
        /// requires <c>UseEntityFramework&lt;TContext&gt;()</c>, whose configuration is inspectable at
        /// registration time; a service-provider-dependent configure cannot be probed then.
        /// </remarks>
        /// <param name="configure">A delegate that configures <see cref="PostgreSqlOptions"/> using the service provider.</param>
        /// <returns>The setup builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UsePostgreSql(Action<PostgreSqlOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            configure += (x, _) => x.Version = setup.Options.Version;

            return _AddPostgreSqlStorageCore(
                setup,
                services => services.Configure<PostgreSqlOptions, PostgreSqlOptionsValidator>(configure),
                outboxProbe: null
            );
        }

        /// <summary>
        /// Configures Entity Framework integration for PostgreSQL messaging using the specified DbContext.
        /// </summary>
        /// <typeparam name="TContext">The DbContext type to use for transactions.</typeparam>
        /// <returns>The setup builder for chaining.</returns>
        public MessagingSetupBuilder UseEntityFramework<TContext>()
            where TContext : DbContext
        {
            return setup.UseEntityFramework<TContext>(opt => { });
        }

        /// <summary>
        /// Configures Entity Framework integration for PostgreSQL messaging with custom options.
        /// </summary>
        /// <typeparam name="TContext">The DbContext type to use for transactions.</typeparam>
        /// <param name="configure">Action to configure Entity Framework messaging options.</param>
        /// <returns>The setup builder for chaining.</returns>
        public MessagingSetupBuilder UseEntityFramework<TContext>(
            Action<PostgreSqlEntityFrameworkMessagingOptions> configure
        )
            where TContext : DbContext
        {
            Argument.IsNotNull(configure);

            Action<PostgreSqlOptions> combined = x =>
            {
                configure(x);
                x.Version = setup.Options.Version;
                x.DbContextType = typeof(TContext);
            };

            return _AddPostgreSqlStorageCore(
                setup,
                services => services.Configure<PostgreSqlOptions, PostgreSqlOptionsValidator>(combined),
                outboxProbe: combined
            );
        }
    }

    private static MessagingSetupBuilder _AddPostgreSqlStorageCore(
        MessagingSetupBuilder setup,
        Action<IServiceCollection> configureOptions,
        Action<PostgreSqlOptions>? outboxProbe
    )
    {
        setup.RegisterExtension(new PostgreSqlMessagesOptionsExtension(configureOptions, outboxProbe));

        return setup;
    }

    private sealed class PostgreSqlMessagesOptionsExtension(
        Action<IServiceCollection> configureOptions,
        Action<PostgreSqlOptions>? outboxProbe
    ) : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageStorageMarkerService("PostgreSql"));
            configureOptions(services);
            services.AddSingleton<IConfigureOptions<PostgreSqlOptions>, ConfigurePostgreSqlOptions>();

            services.AddSingleton<IDataStorage, PostgreSqlDataStorage>();
            services.AddSingleton<IStorageInitializer, PostgreSqlStorageInitializer>();

            _AddTransactionalOutbox(services);
        }

        // On-by-default transactional outbox: when the consumer chose the EF-context storage path
        // (UseEntityFramework<TContext>(), so DbContextType is non-null) and left EnableTransactionalOutbox at its
        // default (true), auto-wire commit coordination so a publish inside a coordinated transaction is atomic with
        // the DB write (outbox row written in that transaction, discarded on rollback) with zero consumer wiring.
        // AddCommitCoordination (called transitively by AddEntityFrameworkCommitCoordination) registers
        // ICurrentCommitCoordinator via an unconditional AddSingleton, so it wins over messaging's
        // TryAddSingleton<MessagingNullCommitCoordinator> fallback regardless of registration order. The raw-ADO
        // path (UsePostgreSql(connString), DbContextType null) is intentionally untouched — it has no DbContext to
        // attach the interceptor to and stays opt-in.
        private void _AddTransactionalOutbox(IServiceCollection services)
        {
            if (outboxProbe is null)
            {
                // Service-provider-dependent configuration cannot be probed at registration time. Only the EF
                // overloads (which always supply a probe) set DbContextType, so this is the raw-ADO path —
                // never auto-register.
                return;
            }

            // DbContextType / EnableTransactionalOutbox are set inside the captured configure action (only the EF
            // overload sets DbContextType), so materialize a throwaway options instance to read them without
            // depending on the DI-built options.
            var probe = new PostgreSqlOptions();
            outboxProbe(probe);

            if (probe.DbContextType is null)
            {
                return; // raw-ADO path (UsePostgreSql(connString)) — never auto-register.
            }

            if (!probe.EnableTransactionalOutbox)
            {
                return; // consumer opted out via UseEntityFramework<T>(o => o.EnableTransactionalOutbox = false).
            }

            // Wire commit coordination + the interceptor-attach config + the startup self-probe gate (which fails
            // loud — Warn by default, Strict opt-in — if the interceptor is enabled but not firing for this context).
            services.AddCommitCoordinationWithStartupGate(probe.DbContextType);
        }
    }
}
