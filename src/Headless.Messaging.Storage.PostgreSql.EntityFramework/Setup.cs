// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using System.Runtime.ExceptionServices;
using Headless.Checks;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Messaging.Storage.PostgreSql;
using Headless.Messaging.Storage.PostgreSql.EntityFramework;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal;

namespace Headless.Messaging;

/// <summary>Configures PostgreSQL messaging storage from an EF Core DbContext.</summary>
[PublicAPI]
[SuppressMessage(
    "Naming",
    "CA1708:Identifiers should differ by more than case",
    Justification = "C# 14 extension member blocks emit compiler-generated marker members differing only by case."
)]
public static class SetupMessagingPostgreSqlEntityFramework
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>Uses the PostgreSQL connection configured for <typeparamref name="TContext"/>.</summary>
        public MessagingSetupBuilder UsePostgreSqlEntityFramework<TContext>()
            where TContext : DbContext
        {
            return setup.UsePostgreSqlEntityFramework<TContext>(_ => { });
        }

        /// <summary>Uses the PostgreSQL connection configured for <typeparamref name="TContext"/>.</summary>
        /// <param name="configure">Configures the EF-backed messaging storage path.</param>
        public MessagingSetupBuilder UsePostgreSqlEntityFramework<TContext>(
            Action<PostgreSqlEntityFrameworkMessagingOptions> configure
        )
            where TContext : DbContext
        {
            Argument.IsNotNull(configure);

            var options = new PostgreSqlEntityFrameworkMessagingOptions();
            configure(options);
            setup.RegisterExtension(
                new PostgreSqlEntityFrameworkOptionsExtension<TContext>(options, setup.Options.Version)
            );

            return setup;
        }
    }

    extension(OutboxStorageBuilder outbox)
    {
        /// <summary>
        /// Stores this additional outbox's published rows in the PostgreSQL database configured for
        /// <typeparamref name="TContext"/>, so a unit of work begun on that context publishes atomically through
        /// <c>unit.Outbox</c>.
        /// </summary>
        /// <typeparam name="TContext">The context whose database holds the outbox.</typeparam>
        /// <returns>The messaging setup builder, for chaining.</returns>
        /// <exception cref="InvalidOperationException">This outbox already has a storage.</exception>
        public MessagingSetupBuilder UsePostgreSqlEntityFramework<TContext>()
            where TContext : DbContext
        {
            var setup = SetupMessagingPostgreSql.AddPostgreSqlOutboxCore(
                outbox,
                $"AddOutbox().UsePostgreSqlEntityFramework<{typeof(TContext).Name}>()",
                options =>
                    options.Configure<IServiceScopeFactory>(
                        (storageOptions, scopeFactory) =>
                            new ConfigurePostgreSqlOptions<TContext>(scopeFactory).Configure(storageOptions)
                    )
            );

            // The outbox exists to be joined by units begun on this context.
            setup.Services.AddEntityFrameworkUnitOfWork();

            return setup;
        }
    }

    private sealed class PostgreSqlEntityFrameworkOptionsExtension<TContext>(
        PostgreSqlEntityFrameworkMessagingOptions options,
        string version
    ) : IMessagesOptionsExtension
        where TContext : DbContext
    {
        public void AddServices(IServiceCollection services)
        {
            new SetupMessagingPostgreSql.PostgreSqlMessagesOptionsExtension(
                storageServices =>
                    storageServices.Configure<PostgreSqlOptions, PostgreSqlOptionsValidator>(storageOptions =>
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

            services.AddSingleton<IConfigureOptions<PostgreSqlOptions>, ConfigurePostgreSqlOptions<TContext>>();

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

    private sealed class ConfigurePostgreSqlOptions<TContext>(IServiceScopeFactory serviceScopeFactory)
        : IConfigureOptions<PostgreSqlOptions>
        where TContext : DbContext
    {
        public void Configure(PostgreSqlOptions options)
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
            if (!dbContext.Database.IsNpgsql())
            {
                throw new InvalidOperationException(
                    $"Messaging UsePostgreSqlEntityFramework<{typeof(TContext).Name}> requires a PostgreSQL DbContext."
                );
            }

            // A data source EF holds (one passed to UseNpgsql, one registered in DI, or one EF builds for
            // ConfigureDataSource or a plugin such as NetTopologySuite) carries configuration the connection string
            // does not: its password provider, its certificates, and the password itself, which the context's
            // connection string omits. Messaging connects through it whenever there is one.
            if (_GetDataSource(dbContext) is { } dataSource)
            {
                options.DataSource = dataSource;
            }
            else
            {
                options.ConnectionString = dbContext.Database.GetConnectionString();
            }

            if (options.DataSource is null && string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                throw new InvalidOperationException(
                    $"Failed to resolve a data source or connection string from DbContext '{typeof(TContext).FullName}'."
                );
            }
        }

        // The data source belongs to EF (or to the application that registered it) and outlives the messaging
        // storage, which never disposes a data source it was handed.
#pragma warning disable EF1001 // EF Core exposes no public accessor for the data source a context connects through; INpgsqlRelationalConnection is the provider's own handle on it.
        private static NpgsqlDataSource? _GetDataSource(DbContext context) =>
            context.GetService<IRelationalConnection>()
                is INpgsqlRelationalConnection { DataSource: NpgsqlDataSource dataSource }
                ? dataSource
                : null;
#pragma warning restore EF1001
    }
}
