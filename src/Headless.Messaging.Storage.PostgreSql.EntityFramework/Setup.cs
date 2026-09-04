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
using Headless.Messaging.Storage.PostgreSql;
using Headless.Messaging.Storage.PostgreSql.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging;

/// <summary>Configures PostgreSQL messaging storage from an EF Core DbContext.</summary>
[PublicAPI]
public static class SetupPostgreSqlEntityFrameworkMessaging
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>Uses the PostgreSQL connection configured for <typeparamref name="TContext"/>.</summary>
        public MessagingSetupBuilder UseEntityFramework<TContext>()
            where TContext : DbContext
        {
            return setup.UseEntityFramework<TContext>(_ => { });
        }

        /// <summary>Uses the PostgreSQL connection configured for <typeparamref name="TContext"/>.</summary>
        /// <param name="configure">Configures the EF-backed messaging storage path.</param>
        public MessagingSetupBuilder UseEntityFramework<TContext>(
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

    private sealed class PostgreSqlEntityFrameworkOptionsExtension<TContext>(
        PostgreSqlEntityFrameworkMessagingOptions options,
        string version
    ) : IMessagesOptionsExtension
        where TContext : DbContext
    {
        public void AddServices(IServiceCollection services)
        {
            new SetupPostgreSqlMessaging.PostgreSqlMessagesOptionsExtension(storageServices =>
                storageServices.Configure<PostgreSqlOptions, PostgreSqlOptionsValidator>(storageOptions =>
                {
                    storageOptions.Schema = options.Schema;
                    storageOptions.OwnerColumnMaxLength = options.OwnerColumnMaxLength;
                    storageOptions.Version = version;
                })
            ).AddServices(services);

            services.AddSingleton<IConfigureOptions<PostgreSqlOptions>, ConfigurePostgreSqlOptions<TContext>>();

            if (options.EnableTransactionalOutbox)
            {
                services.AddCommitCoordinationWithStartupGate(typeof(TContext));
                _PromoteStorageCapability(services, "PostgreSql");
                services.AddScoped<IInboxTransactionRunner>(
                    serviceProvider => new PostgreSqlInboxTransactionRunner<TContext>(
                        serviceProvider.GetRequiredService<TContext>(),
                        serviceProvider,
                        serviceProvider.GetRequiredService<ICurrentCommitCoordinator>(),
                        serviceProvider.GetRequiredService<IDeliveryCoordinationResolver>(),
                        serviceProvider.GetRequiredService<PostgreSqlDataStorage>(),
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

    private sealed class PostgreSqlInboxTransactionRunner<TContext>(
        TContext context,
        IServiceProvider services,
        ICurrentCommitCoordinator currentCoordinator,
        IDeliveryCoordinationResolver coordinationResolver,
        PostgreSqlDataStorage storage,
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
            var providerOptions = dbContext.GetService<IDbContextOptions>();
            var extension = providerOptions.Extensions.First(x => x.Info.IsDatabaseProvider);

#pragma warning disable REFL003, REFL017 // Provider options expose connection state through provider-specific members.
            options.DataSource =
                extension.GetType().GetProperty(nameof(options.DataSource))?.GetValue(extension) as NpgsqlDataSource;
            if (options.DataSource is null)
            {
                options.ConnectionString =
                    extension.GetType().GetProperty(nameof(options.ConnectionString))?.GetValue(extension) as string;
            }
#pragma warning restore REFL003, REFL017

            if (options.DataSource is null && string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                throw new InvalidOperationException(
                    $"Failed to resolve a DataSource or ConnectionString from '{extension.GetType().FullName}' for DbContext '{typeof(TContext).FullName}'."
                );
            }
        }
    }
}
