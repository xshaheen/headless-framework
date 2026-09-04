// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Runtime.ExceptionServices;
using Headless.Checks;
using Headless.CommitCoordination;
using Headless.CommitCoordination.EntityFramework;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Runtime;
using Headless.Messaging.Storage.SqlServer;
using Headless.Messaging.Storage.SqlServer.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
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
                    storageOptions.Schema = options.Schema;
                    storageOptions.OwnerColumnMaxLength = options.OwnerColumnMaxLength;
                    storageOptions.Version = version;
                })
            ).AddServices(services);

            services.AddSingleton<IConfigureOptions<SqlServerOptions>, ConfigureSqlServerOptions<TContext>>();

            if (options.EnableTransactionalOutbox)
            {
                services.AddCommitCoordinationWithStartupGate(typeof(TContext));
                _PromoteStorageCapability(services, "SqlServer");
                services.AddScoped<IInboxTransactionRunner>(
                    serviceProvider => new SqlServerInboxTransactionRunner<TContext>(
                        serviceProvider.GetRequiredService<TContext>(),
                        serviceProvider,
                        serviceProvider.GetRequiredService<ICurrentCommitCoordinator>(),
                        serviceProvider.GetRequiredService<IDeliveryCoordinationResolver>(),
                        serviceProvider.GetRequiredService<SqlServerDataStorage>(),
                        serviceProvider.GetRequiredService<EntityFrameworkCommitSignalSource>()
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
        IServiceProvider services,
        ICurrentCommitCoordinator currentCoordinator,
        IDeliveryCoordinationResolver coordinationResolver,
        SqlServerDataStorage storage,
        EntityFrameworkCommitSignalSource signalSource
    ) : IInboxTransactionRunner
        where TContext : DbContext
    {
        public async Task ExecuteAsync(
            MediumMessage message,
            Func<CancellationToken, Task> handler,
            CancellationToken cancellationToken
        )
        {
            if (currentCoordinator.Current is not null || context.Database.CurrentTransaction is not null)
            {
                throw new InvalidOperationException(
                    "Transactional inbox execution cannot enter an already-active or nested transaction boundary."
                );
            }

            var commitError = await context
                .Database.CreateExecutionStrategy()
                .ExecuteAsync(
                    async ct =>
                    {
                        await using var transaction = await context
                            .Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
                            .ConfigureAwait(false);
                        var dbTransaction = transaction.GetDbTransaction();
                        await using var coordinationScope = context.Database.EnlistCommitCoordination(
                            transaction,
                            services,
                            ct
                        );
                        var coordinator =
                            currentCoordinator.Current
                            ?? throw new InvalidOperationException(
                                "The EF inbox transaction did not establish commit coordination before handler entry."
                            );
                        var coordination = coordinationResolver.Resolve(coordinator);
                        if (coordination.Status is not DeliveryCoordinationStatus.Compatible)
                        {
                            throw new InvalidOperationException(
                                $"The EF inbox transaction is incompatible with messaging storage: {coordination.Mismatch}."
                            );
                        }

                        await handler(ct).ConfigureAwait(false);
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
                            await signalSource.SignalCommittedAsync(dbTransaction).ConfigureAwait(false);
                            return null;
                        }
                        catch (Exception commitException)
                        {
                            if (
                                await ((ITransactionalInboxStorage)storage)
                                    .ProbeReceivedInboxCommitAsync(message, CancellationToken.None)
                                    .ConfigureAwait(false) is InboxCommitProbe.Committed
                            )
                            {
                                await signalSource.SignalCommittedAsync(dbTransaction).ConfigureAwait(false);
                                return null;
                            }

                            try
                            {
                                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                                await signalSource.SignalRolledBackAsync(dbTransaction).ConfigureAwait(false);
                            }
                            catch (Exception rollbackException)
                            {
                                if (
                                    await ((ITransactionalInboxStorage)storage)
                                        .ProbeReceivedInboxCommitAsync(message, CancellationToken.None)
                                        .ConfigureAwait(false) is InboxCommitProbe.Committed
                                )
                                {
                                    await signalSource.SignalCommittedAsync(dbTransaction).ConfigureAwait(false);
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
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
            commitError?.Throw();
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
