// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.CommitCoordination;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Messaging.Storage.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging;

[PublicAPI]
public static class SetupSqlServerMessaging
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>
        /// Configures the messaging outbox to use SQL Server with a raw ADO.NET connection string.
        /// The connection string is stored in <c>SqlServerOptions.ConnectionString</c> and used
        /// directly without an EF Core <c>DbContext</c>. The transactional outbox is not available
        /// on this path; use <c>UseEntityFramework&lt;TContext&gt;()</c> for atomic outbox support.
        /// </summary>
        /// <param name="connectionString">The SQL Server connection string.</param>
        /// <returns>The builder for chaining.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="connectionString"/> is null or whitespace.</exception>
        public MessagingSetupBuilder UseSqlServer(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UseSqlServer(opt => opt.ConnectionString = connectionString);
        }

        /// <summary>
        /// Configures the messaging outbox to use SQL Server, binding and validating
        /// <see cref="SqlServerOptions"/> from configuration.
        /// </summary>
        /// <param name="configuration">Configuration section containing <see cref="SqlServerOptions"/> values.</param>
        /// <returns>The builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseSqlServer(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            return _AddSqlServerStorageCore(
                setup,
                services =>
                    services
                        .AddOptions<SqlServerOptions, SqlServerOptionsValidator>()
                        .Bind(configuration)
                        .Configure(x => x.Version = setup.Options.Version),
                outboxProbe: options => configuration.Bind(options)
            );
        }

        /// <summary>
        /// Configures the messaging outbox to use SQL Server, delegating all option configuration
        /// to the supplied <paramref name="configure"/> action.
        /// </summary>
        /// <param name="configure">An action that populates <see cref="SqlServerOptions"/>.</param>
        /// <returns>The builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseSqlServer(Action<SqlServerOptions> configure)
        {
            Argument.IsNotNull(configure);

            configure += x => x.Version = setup.Options.Version;

            return _AddSqlServerStorageCore(
                setup,
                services => services.Configure<SqlServerOptions, SqlServerOptionsValidator>(configure),
                outboxProbe: configure
            );
        }

        /// <summary>
        /// Configures the messaging outbox to use SQL Server, configuring <see cref="SqlServerOptions"/>
        /// with access to the resolved service provider (for example to resolve secrets or connection
        /// settings from DI).
        /// </summary>
        /// <remarks>
        /// This overload targets the raw ADO.NET storage path. The transactional outbox auto-wiring
        /// requires <c>UseEntityFramework&lt;TContext&gt;()</c>, whose configuration is inspectable at
        /// registration time; a service-provider-dependent configure cannot be probed then.
        /// </remarks>
        /// <param name="configure">A delegate that configures <see cref="SqlServerOptions"/> using the service provider.</param>
        /// <returns>The builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseSqlServer(Action<SqlServerOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            configure += (x, _) => x.Version = setup.Options.Version;

            return _AddSqlServerStorageCore(
                setup,
                services => services.Configure<SqlServerOptions, SqlServerOptionsValidator>(configure),
                outboxProbe: null
            );
        }

        /// <summary>
        /// Configures the messaging outbox to use SQL Server through the specified EF Core
        /// <typeparamref name="TContext"/>. The connection string is derived from the registered
        /// <c>DbContext</c> at startup. The transactional outbox is enabled by default.
        /// </summary>
        /// <typeparam name="TContext">The EF Core <c>DbContext</c> whose connection is used for message storage.</typeparam>
        /// <returns>The builder for chaining.</returns>
        public MessagingSetupBuilder UseEntityFramework<TContext>()
            where TContext : DbContext
        {
            return setup.UseEntityFramework<TContext>(_ => { });
        }

        /// <summary>
        /// Configures the messaging outbox to use SQL Server through the specified EF Core
        /// <typeparamref name="TContext"/>, with additional EF-specific option overrides.
        /// The transactional outbox is enabled by default; set
        /// <c>options.EnableTransactionalOutbox = false</c> to opt out.
        /// </summary>
        /// <typeparam name="TContext">The EF Core <c>DbContext</c> whose connection is used for message storage.</typeparam>
        /// <param name="configure">An action that populates <see cref="SqlServerEntityFrameworkMessagingOptions"/>.</param>
        /// <returns>The builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseEntityFramework<TContext>(
            Action<SqlServerEntityFrameworkMessagingOptions> configure
        )
            where TContext : DbContext
        {
            Argument.IsNotNull(configure);

            Action<SqlServerOptions> combined = x =>
            {
                configure(x);
                x.Version = setup.Options.Version;
                x.DbContextType = typeof(TContext);
            };

            return _AddSqlServerStorageCore(
                setup,
                services => services.Configure<SqlServerOptions, SqlServerOptionsValidator>(combined),
                outboxProbe: combined
            );
        }
    }

    private static MessagingSetupBuilder _AddSqlServerStorageCore(
        MessagingSetupBuilder setup,
        Action<IServiceCollection> configureOptions,
        Action<SqlServerOptions>? outboxProbe
    )
    {
        setup.RegisterExtension(new SqlServerMessagesOptionsExtension(configureOptions, outboxProbe));

        return setup;
    }

    private sealed class SqlServerMessagesOptionsExtension(
        Action<IServiceCollection> configureOptions,
        Action<SqlServerOptions>? outboxProbe
    ) : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageStorageMarkerService("SqlServer"));

            services.AddSingleton<IDataStorage, SqlServerDataStorage>();
            services.AddSingleton<IStorageInitializer, SqlServerStorageInitializer>();

            configureOptions(services);
            services.AddSingleton<IConfigureOptions<SqlServerOptions>, ConfigureSqlServerOptions>();

            _AddTransactionalOutbox(services);
        }

        // On-by-default transactional outbox: when the consumer chose the EF-context storage path
        // (UseEntityFramework<TContext>(), so DbContextType is non-null) and left EnableTransactionalOutbox at its
        // default (true), auto-wire commit coordination so a publish inside a coordinated transaction is atomic with
        // the DB write (outbox row written in that transaction, discarded on rollback) with zero consumer wiring.
        // AddCommitCoordination (called transitively by AddEntityFrameworkCommitCoordination) registers
        // ICurrentCommitCoordinator via an unconditional AddSingleton, so it wins over messaging's
        // TryAddSingleton<MessagingNullCommitCoordinator> fallback regardless of registration order. The raw-ADO
        // path (UseSqlServer(connString), DbContextType null) is intentionally untouched — it has no DbContext to
        // attach the interceptor to and stays opt-in. (SqlServer's out-of-band SqlClient diagnostic is NOT started
        // here; the EF path detects commits via the EF interceptor, so the process-wide diagnostic is never forced
        // on EF-storage consumers.)
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
            var probe = new SqlServerOptions();
            outboxProbe(probe);

            if (probe.DbContextType is null)
            {
                return; // raw-ADO path (UseSqlServer(connString)) — never auto-register.
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
