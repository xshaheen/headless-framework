// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.CommitCoordination.EntityFramework;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging.Storage.SqlServer;

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

            setup.RegisterExtension(new SqlServerMessagesOptionsExtension(configure));

            return setup;
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

            setup.RegisterExtension(
                new SqlServerMessagesOptionsExtension(x =>
                {
                    configure(x);
                    x.Version = setup.Options.Version;
                    x.DbContextType = typeof(TContext);
                })
            );

            return setup;
        }
    }

    private sealed class SqlServerMessagesOptionsExtension(Action<SqlServerOptions> configure)
        : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageStorageMarkerService("SqlServer"));

            services.AddSingleton<IDataStorage, SqlServerDataStorage>();
            services.AddSingleton<IStorageInitializer, SqlServerStorageInitializer>();

            services.Configure<SqlServerOptions, SqlServerOptionsValidator>(configure);
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
            // DbContextType / EnableTransactionalOutbox are set inside the captured configure action (only the EF
            // overload sets DbContextType), so materialize a throwaway options instance to read them without
            // depending on the DI-built options.
            var probe = new SqlServerOptions();
            configure(probe);

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
