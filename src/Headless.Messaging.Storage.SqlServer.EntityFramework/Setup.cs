// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using System.Runtime.ExceptionServices;
using Headless.Checks;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Messaging.Storage.SqlServer;
using Headless.Messaging.Storage.SqlServer.EntityFramework;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging;

/// <summary>Configures SQL Server messaging storage from an EF Core DbContext.</summary>
[PublicAPI]
[SuppressMessage(
    "Naming",
    "CA1708:Identifiers should differ by more than case",
    Justification = "C# 14 extension member blocks emit compiler-generated marker members differing only by case."
)]
public static class SetupMessagingSqlServerEntityFramework
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>Uses the SQL Server connection configured for <typeparamref name="TContext"/>.</summary>
        public MessagingSetupBuilder UseSqlServerEntityFramework<TContext>()
            where TContext : DbContext
        {
            return setup.UseSqlServerEntityFramework<TContext>(_ => { });
        }

        /// <summary>Uses the SQL Server connection configured for <typeparamref name="TContext"/>.</summary>
        /// <param name="configure">Configures the EF-backed messaging storage path.</param>
        public MessagingSetupBuilder UseSqlServerEntityFramework<TContext>(
            Action<SqlServerEntityFrameworkMessagingOptions> configure
        )
            where TContext : DbContext
        {
            Argument.IsNotNull(configure);

            var options = new SqlServerEntityFrameworkMessagingOptions();
            configure(options);
            setup.RegisterExtension(
                new SqlServerEntityFrameworkOptionsExtension<TContext>(options, setup.Options.Version)
            );

            return setup;
        }
    }

    extension(OutboxStorageBuilder outbox)
    {
        /// <summary>
        /// Stores this additional outbox's published rows in the SQL Server database configured for
        /// <typeparamref name="TContext"/>, so a unit of work begun on that context publishes atomically through
        /// <c>unit.Outbox</c>.
        /// </summary>
        /// <typeparam name="TContext">The context whose database holds the outbox.</typeparam>
        /// <returns>The messaging setup builder, for chaining.</returns>
        /// <exception cref="InvalidOperationException">This outbox already has a storage.</exception>
        public MessagingSetupBuilder UseSqlServerEntityFramework<TContext>()
            where TContext : DbContext
        {
            var setup = SetupMessagingSqlServer.AddSqlServerOutboxCore(
                outbox,
                $"AddOutbox().UseSqlServerEntityFramework<{typeof(TContext).Name}>()",
                options =>
                    options.Configure<IServiceScopeFactory>(
                        (storageOptions, scopeFactory) =>
                            new ConfigureSqlServerOptions<TContext>(scopeFactory).Configure(storageOptions)
                    )
            );

            // The outbox exists to be joined by units begun on this context.
            setup.Services.AddEntityFrameworkUnitOfWork();

            return setup;
        }
    }

    private sealed class SqlServerEntityFrameworkOptionsExtension<TContext>(
        SqlServerEntityFrameworkMessagingOptions options,
        string version
    ) : IMessagesOptionsExtension
        where TContext : DbContext
    {
        public void AddServices(IServiceCollection services)
        {
            new SetupMessagingSqlServer.SqlServerMessagesOptionsExtension(
                storageServices =>
                    storageServices.Configure<SqlServerOptions, SqlServerOptionsValidator>(storageOptions =>
                    {
                        storageOptions.OwnerColumnMaxLength = options.OwnerColumnMaxLength;
                        storageOptions.Version = version;
                    }),
                // Known at registration, so composing the capability never resolves the storage options, which
                // would build the application's DbContext.
                isTransactionalInboxEnabled: _ => options.EnableTransactionalInbox,
                inboxRunnerFactory: options.EnableTransactionalInbox
                    ? serviceProvider => new EntityFrameworkInboxTransactionRunner<TContext>(
                        serviceProvider.GetRequiredService<TContext>(),
                        serviceProvider.GetRequiredService<IUnitOfWorkFactory>(),
                        serviceProvider.GetRequiredService<IDeliveryCoordinationResolver>(),
                        serviceProvider.GetRequiredService<RelationalDataStorage>(),
                        serviceProvider
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger<EntityFrameworkInboxTransactionRunner<TContext>>()
                    )
                    : null
            ).AddServices(services);

            services.AddSingleton<IConfigureOptions<SqlServerOptions>, ConfigureSqlServerOptions<TContext>>();

            if (options.EnableTransactionalInbox)
            {
                services.AddEntityFrameworkUnitOfWork();
            }
        }
    }

    private sealed class EntityFrameworkInboxTransactionRunner<TContext>(
        TContext context,
        IUnitOfWorkFactory unitOfWorkFactory,
        IDeliveryCoordinationResolver coordinationResolver,
        RelationalDataStorage storage,
        ILogger logger
    ) : InboxTransactionRunner(coordinationResolver, storage, logger)
        where TContext : DbContext
    {
        protected override void EnsureNoAmbientTransaction()
        {
            if (context.Database.CurrentTransaction is not null || context.UnitOfWork() is not null)
            {
                throw new InvalidOperationException(
                    "Transactional inbox execution cannot enter an already-active or nested transaction boundary."
                );
            }
        }

        // EF's execution strategy replays a transient fault; the shared runner reports every fault after handler
        // entry as a result instead, so only an attempt that never reached the handler is replayed.
        protected override Task<ExceptionDispatchInfo?> ExecuteWithPreEntryRetryAsync(
            Func<CancellationToken, Task<ExceptionDispatchInfo?>> attempt,
            CancellationToken cancellationToken
        )
        {
            return context.Database.CreateExecutionStrategy().ExecuteAsync(attempt, cancellationToken);
        }

        protected override async ValueTask<InboxAttemptTransaction> BeginAttemptAsync(
            CancellationToken cancellationToken
        )
        {
            var transaction = await context
                .Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                // Observed mode: the runner commits; the unit is handed to the handler so the consumer's context and
                // a callback publish enlist in this transaction.
                return new Attempt(context, transaction, unitOfWorkFactory.Enlist(context, transaction));
            }
            catch
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        private sealed class Attempt(TContext context, IDbContextTransaction transaction, IUnitOfWork unitOfWork)
            : InboxAttemptTransaction
        {
            public override IUnitOfWork UnitOfWork => unitOfWork;

            public override DbTransaction Transaction => transaction.GetDbTransaction();

            public override async ValueTask FlushAsync(CancellationToken cancellationToken)
            {
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            public override Task CommitAsync(CancellationToken cancellationToken) =>
                transaction.CommitAsync(cancellationToken);

            public override Task RollbackAsync(CancellationToken cancellationToken) =>
                transaction.RollbackAsync(cancellationToken);

            public override async ValueTask DisposeAsync()
            {
                try
                {
                    await unitOfWork.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    await transaction.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    private sealed class ConfigureSqlServerOptions<TContext>(IServiceScopeFactory serviceScopeFactory)
        : IConfigureOptions<SqlServerOptions>
        where TContext : DbContext
    {
        public void Configure(SqlServerOptions options)
        {
            if (
                RuntimeTypeInspection.DeclaresFieldOfType<IBus>(typeof(TContext))
                || RuntimeTypeInspection.DeclaresFieldOfType<IQueue>(typeof(TContext))
            )
            {
                throw new InvalidOperationException(
                    "The DbContext must not capture IBus or IQueue. Inject the storage extension directly to avoid a circular dependency."
                );
            }

            using var scope = serviceScopeFactory.CreateScope();
            using var dbContext = scope.ServiceProvider.GetRequiredService<TContext>();
            // The connection can hold credentials a connection string cannot carry; the copy refuses one that
            // authenticates with them rather than failing at the first login. GetConnectionString() is the string
            // the context was configured from, which keeps the password a pooled, already-opened connection lost.
            options.ConnectionString = dbContext.Database.GetDbConnection() is SqlConnection connection
                ? connection.GetReusableConnectionString(dbContext.Database.GetConnectionString())
                : throw new InvalidOperationException(
                    $"Messaging UseSqlServerEntityFramework<{typeof(TContext).Name}> requires a SQL Server DbContext."
                );

            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                throw new InvalidOperationException(
                    $"DbContext '{typeof(TContext).FullName}' returned a null or empty connection string."
                );
            }
        }
    }
}
