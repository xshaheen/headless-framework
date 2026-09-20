// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Runtime.ExceptionServices;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Runtime;
using Headless.Messaging.Storage.SqlServer;
using Headless.Messaging.Storage.SqlServer.EntityFramework;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging;

/// <summary>Configures SQL Server messaging storage from an EF Core DbContext.</summary>
[PublicAPI]
public static class SetupSqlServerEntityFrameworkMessaging
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>Uses the SQL Server connection configured for <typeparamref name="TContext"/>.</summary>
        public MessagingSetupBuilder UseEntityFramework<TContext>()
            where TContext : DbContext
        {
            return setup.UseEntityFramework<TContext>(_ => { });
        }

        /// <summary>Uses the SQL Server connection configured for <typeparamref name="TContext"/>.</summary>
        /// <param name="configure">Configures the EF-backed messaging storage path.</param>
        public MessagingSetupBuilder UseEntityFramework<TContext>(
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

    private sealed class SqlServerEntityFrameworkOptionsExtension<TContext>(
        SqlServerEntityFrameworkMessagingOptions options,
        string version
    ) : IMessagesOptionsExtension
        where TContext : DbContext
    {
        public void AddServices(IServiceCollection services)
        {
            new SetupSqlServerMessaging.SqlServerMessagesOptionsExtension(storageServices =>
                storageServices.Configure<SqlServerOptions, SqlServerOptionsValidator>(storageOptions =>
                {
                    storageOptions.OwnerColumnMaxLength = options.OwnerColumnMaxLength;
                    storageOptions.Version = version;
                })
            ).AddServices(services);

            services.AddSingleton<IConfigureOptions<SqlServerOptions>, ConfigureSqlServerOptions<TContext>>();

            if (options.EnableTransactionalOutbox)
            {
                services.AddEntityFrameworkUnitOfWork();
                _PromoteStorageCapability(services, "SqlServer");
                services.AddScoped<IInboxTransactionRunner>(
                    serviceProvider => new SqlServerInboxTransactionRunner<TContext>(
                        serviceProvider.GetRequiredService<TContext>(),
                        serviceProvider.GetRequiredService<IUnitOfWorkFactory>(),
                        serviceProvider.GetRequiredService<IDeliveryCoordinationResolver>(),
                        serviceProvider.GetRequiredService<SqlServerDataStorage>(),
                        serviceProvider
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger<SqlServerInboxTransactionRunner<TContext>>()
                    )
                );
            }
        }
    }

    private static void _PromoteStorageCapability(IServiceCollection services, string provider)
    {
        var descriptor = services.LastOrDefault(candidate =>
            candidate.ServiceType == typeof(MessagingProviderCapabilities)
            && candidate.ImplementationInstance
                is MessagingProviderCapabilities
                {
                    Role: MessagingProviderRole.Storage,
                    Provider: var registeredProvider,
                }
            && string.Equals(registeredProvider, provider, StringComparison.Ordinal)
        );
        var current =
            descriptor?.ImplementationInstance as MessagingProviderCapabilities
            ?? throw new InvalidOperationException(
                $"The {provider} storage capability must be registered before enabling its EF inbox transaction runner."
            );

        services.Remove(descriptor!);
        services.AddMessagingProviderCapabilities(
            MessagingProviderCapabilities.Storage(
                current.Provider,
                current.Lanes.ToArray(),
                current.SupportsDelayedScheduling,
                MessagingInboxCapabilityTier.Transactional
            )
        );
    }

    private sealed class SqlServerInboxTransactionRunner<TContext>(
        TContext context,
        IUnitOfWorkFactory unitOfWorkFactory,
        IDeliveryCoordinationResolver coordinationResolver,
        SqlServerDataStorage storage,
        ILogger logger
    ) : IInboxTransactionRunner
        where TContext : DbContext
    {
        public async Task ExecuteAsync(
            MediumMessage message,
            Func<IUnitOfWork, CancellationToken, Task> handler,
            CancellationToken cancellationToken
        )
        {
            if (context.Database.CurrentTransaction is not null || context.UnitOfWork() is not null)
            {
                throw new InvalidOperationException(
                    "Transactional inbox execution cannot enter an already-active or nested transaction boundary."
                );
            }

            var attemptError = await context
                .Database.CreateExecutionStrategy()
                .ExecuteAsync(
                    async ct =>
                    {
                        var handlerEntered = false;
                        try
                        {
                            await using var transaction = await context
                                .Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
                                .ConfigureAwait(false);
                            var dbTransaction = transaction.GetDbTransaction();
                            // Observed mode: the runner commits; the unit is handed to the handler so the
                            // consumer's context and a callback publish enlist in this transaction.
                            await using var unitOfWork = unitOfWorkFactory.Enlist(context, transaction);
                            var coordination = coordinationResolver.Resolve(unitOfWork);
                            if (coordination.Status is not DeliveryCoordinationStatus.Compatible)
                            {
                                throw new InvalidOperationException(
                                    $"The EF inbox transaction is incompatible with messaging storage: {coordination.Mismatch}."
                                );
                            }

                            handlerEntered = true;
                            await handler(unitOfWork, ct).ConfigureAwait(false);
                            await context.SaveChangesAsync(ct).ConfigureAwait(false);
                            var completed = await ((ITransactionalInboxStorage)storage)
                                .CompleteReceivedInboxAsync(message, dbTransaction, ct)
                                .ConfigureAwait(false);
                            if (!completed)
                            {
                                throw new StaleInboxAttemptException(message.StorageId);
                            }

                            try
                            {
                                await transaction.CommitAsync(ct).ConfigureAwait(false);
                            }
                            catch (Exception commitException)
                            {
                                if (
                                    await ((ITransactionalInboxStorage)storage)
                                        .ProbeReceivedInboxCommitAsync(message, CancellationToken.None)
                                        .ConfigureAwait(false) is InboxCommitProbe.Committed
                                )
                                {
                                    await _CompleteAfterDurableCommitAsync(unitOfWork, message).ConfigureAwait(false);
                                    return null;
                                }

                                try
                                {
                                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                                    await unitOfWork.RollbackAsync().ConfigureAwait(false);
                                }
                                catch (Exception rollbackException)
                                {
                                    if (
                                        await ((ITransactionalInboxStorage)storage)
                                            .ProbeReceivedInboxCommitAsync(message, CancellationToken.None)
                                            .ConfigureAwait(false) is InboxCommitProbe.Committed
                                    )
                                    {
                                        await _CompleteAfterDurableCommitAsync(unitOfWork, message)
                                            .ConfigureAwait(false);
                                        return null;
                                    }

                                    return ExceptionDispatchInfo.Capture(
                                        new IndeterminateInboxCommitException(
                                            message.StorageId,
                                            commitException,
                                            rollbackException
                                        )
                                    );
                                }

                                return ExceptionDispatchInfo.Capture(
                                    new UncommittedInboxCommitException(message.StorageId, commitException)
                                );
                            }

                            // Outside the commit's catch: a drain fault here is not a commit fault and must not
                            // be probed and "completed" a second time.
                            await _CompleteAfterDurableCommitAsync(unitOfWork, message).ConfigureAwait(false);
                            return null;
                        }
                        catch (Exception exception) when (handlerEntered)
                        {
                            // Persisted inbox recovery owns every retry after entry, including failures
                            // raised while disposing the unit of work or transaction.
                            return ExceptionDispatchInfo.Capture(exception);
                        }
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
            attemptError?.Throw();
        }

        // The row is durable once the commit (or its probe) says so; the unit's drain only accelerates the
        // dispatch of the enlisted callbacks. A drain fault must not turn a committed attempt into a retry that
        // re-invokes the consumer, so it is logged and the attempt reports success — the same policy as the
        // unit-of-work runners; the relay recovers any enlisted rows.
        private async Task _CompleteAfterDurableCommitAsync(IUnitOfWork unitOfWork, MediumMessage message)
        {
            try
            {
                await unitOfWork.CompleteAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (unitOfWork.State == UnitOfWorkState.Completed)
            {
                logger.InboxPostCommitDrainFaulted(ex, message.StorageId);
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
            options.ConnectionString = dbContext.Database.GetConnectionString();

            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                throw new InvalidOperationException(
                    $"DbContext '{typeof(TContext).FullName}' returned a null or empty connection string."
                );
            }
        }
    }
}
