// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.CommitCoordination.EntityFramework;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging.Storage.PostgreSql;

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
        /// Configures PostgreSQL as the message storage with custom options.
        /// </summary>
        /// <param name="configure">Action to configure PostgreSQL options.</param>
        /// <returns>The setup builder for chaining.</returns>
        public MessagingSetupBuilder UsePostgreSql(Action<PostgreSqlOptions> configure)
        {
            Argument.IsNotNull(configure);

            configure += x => x.Version = setup.Options.Version;

            setup.RegisterExtension(new PostgreSqlMessagesOptionsExtension(configure));

            return setup;
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

            setup.RegisterExtension(
                new PostgreSqlMessagesOptionsExtension(x =>
                {
                    configure(x);
                    x.Version = setup.Options.Version;
                    x.DbContextType = typeof(TContext);
                })
            );

            return setup;
        }
    }

    private sealed class PostgreSqlMessagesOptionsExtension(Action<PostgreSqlOptions> configure)
        : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageStorageMarkerService("PostgreSql"));
            services.Configure<PostgreSqlOptions, PostgreSqlOptionsValidator>(configure);
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
            // DbContextType / EnableTransactionalOutbox are set inside the captured configure action (only the EF
            // overload sets DbContextType), so materialize a throwaway options instance to read them without
            // depending on the DI-built options.
            var probe = new PostgreSqlOptions();
            configure(probe);

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
