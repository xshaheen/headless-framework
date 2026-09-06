// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using InboxScopeDbContext = Tests.TransactionalInboxScopeConformanceTests.InboxScopeDbContext;
using InboxScopeEffect = Tests.TransactionalInboxScopeConformanceTests.InboxScopeEffect;

namespace Tests;

/// <summary>Verifies EF retries cannot replay a reserved inbox attempt against real relational storage.</summary>
public abstract class TransactionalInboxRetryConformanceTests : TestBase
{
    protected abstract void ConfigureContext(DbContextOptionsBuilder options);

    protected abstract void ConfigureStorage(MessagingSetupBuilder setup);

    protected abstract string CreateEffectsTableSql { get; }

    [Theory]
    [InlineData(FailurePoint.BeforeEntry)]
    [InlineData(FailurePoint.Handler)]
    [InlineData(FailurePoint.SaveChanges)]
    [InlineData(FailurePoint.BeforeCommit)]
    [InlineData(FailurePoint.AfterCommit)]
    [InlineData(FailurePoint.Rollback)]
    [InlineData(FailurePoint.Disposal)]
    public async Task should_retry_only_before_handler_entry_and_recover_with_a_new_fence(FailurePoint failurePoint)
    {
        var fault = new FaultState(failurePoint);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<InboxScopeDbContext>(options =>
        {
            ConfigureContext(options);
            options.ReplaceService<IExecutionStrategyFactory, OneShotRetryExecutionStrategyFactory>();
            options.AddInterceptors(
                new TransactionFaultInterceptor(fault),
                new SaveFaultInterceptor(fault),
                new DisposalFaultInterceptor(fault)
            );
        });
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            ConfigureStorage(setup);
            setup.Options.CommandTimeout = TimeSpan.FromSeconds(1);
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var storage = provider.GetRequiredService<IDataStorage>();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<InboxScopeDbContext>();
            await db.Database.ExecuteSqlRawAsync(CreateEffectsTableSql, AbortToken);
        }

        var id = Guid.NewGuid();
        var origin = new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = id.ToString(),
                [Headers.MessageName] = "tests.inbox-retry",
                [Headers.Group] = "tests.inbox-retry",
            },
            id
        );
        ValueTask<InboxAdmissionResult> admit() =>
            storage.AdmitReceivedMessageAsync(
                "tests.inbox-retry",
                "tests.inbox-retry",
                "tests.inbox-retry.consumer",
                "v1",
                new MediumMessage
                {
                    StorageId = Guid.Empty,
                    Origin = origin,
                    Content = string.Empty,
                    Lane = MessageLane.Bus,
                },
                cancellationToken: AbortToken
            );
        var admission = await admit();
        admission.Disposition.Should().Be(InboxAdmissionDisposition.Winner);
        var message = admission.Message;
        var originalInlineAttempts = message.InlineAttempts++;
        (
            await storage.LeaseReceiveAndReserveAttemptAsync(
                message,
                TimeSpan.FromMinutes(1),
                originalInlineAttempts,
                AbortToken
            )
        )
            .Should()
            .BeTrue();

        var handlerEntries = 0;
        Exception? error;
        fault.Armed = true;
        await using (var attemptScope = provider.CreateAsyncScope())
        {
            var db = attemptScope.ServiceProvider.GetRequiredService<InboxScopeDbContext>();
            error = await Record.ExceptionAsync(() =>
                attemptScope
                    .ServiceProvider.GetRequiredService<IInboxTransactionRunner>()
                    .ExecuteAsync(
                        message,
                        async ct =>
                        {
                            handlerEntries++;
                            await db.Effects.AddAsync(new InboxScopeEffect { Id = id }, ct);
                            fault.ThrowOnce(FailurePoint.Handler);
                            if (failurePoint is FailurePoint.Disposal)
                            {
                                throw new InvalidOperationException("Trigger transaction disposal without committing.");
                            }
                        },
                        AbortToken
                    )
            );
        }
        fault.Armed = false;

        handlerEntries.Should().Be(1, "only persisted Messaging recovery may enter another handler attempt");
        fault.Injected.Should().BeTrue();
        fault.TransactionStarts.Should().Be(failurePoint is FailurePoint.BeforeEntry ? 2 : 1);
        var committed = failurePoint is FailurePoint.BeforeEntry or FailurePoint.AfterCommit;
        if (committed)
        {
            error.Should().BeNull("a pre-entry retry is safe and a durable commit is resolved by its probe");
        }
        else
        {
            error.Should().NotBeNull();
        }

        await using (var verificationScope = provider.CreateAsyncScope())
        {
            var db = verificationScope.ServiceProvider.GetRequiredService<InboxScopeDbContext>();
            (await db.Effects.AnyAsync(effect => effect.Id == id, AbortToken)).Should().Be(committed);
        }
        (await admit())
            .Disposition.Should()
            .Be(committed ? InboxAdmissionDisposition.SucceededDuplicate : InboxAdmissionDisposition.InFlightDuplicate);
        if (committed)
        {
            return;
        }

        // The failed attempt returns to Messaging, which persists retry state before a later pickup.
        (
            await storage.ChangeReceiveStateAsync(
                message,
                StatusName.Failed,
                nextRetryAt: DateTimeOffset.UtcNow.AddMinutes(-1),
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();
        var recovered = (await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken)).Single(
            candidate => candidate.StorageId == message.StorageId
        );
        recovered.InboxAttemptFence!.AttemptId.Should().NotBe(message.InboxAttemptFence!.AttemptId);
        await using (var staleScope = provider.CreateAsyncScope())
        {
            var staleError = await Record.ExceptionAsync(() =>
                staleScope
                    .ServiceProvider.GetRequiredService<IInboxTransactionRunner>()
                    .ExecuteAsync(message, _ => Task.CompletedTask, AbortToken)
            );
            staleError.Should().BeOfType<StaleInboxAttemptException>();
        }
        originalInlineAttempts = recovered.InlineAttempts++;
        (await storage.ReserveReceiveAttemptAsync(recovered, originalInlineAttempts, AbortToken)).Should().BeTrue();
        await using (var recoveryScope = provider.CreateAsyncScope())
        {
            var db = recoveryScope.ServiceProvider.GetRequiredService<InboxScopeDbContext>();
            await recoveryScope
                .ServiceProvider.GetRequiredService<IInboxTransactionRunner>()
                .ExecuteAsync(
                    recovered,
                    async ct =>
                    {
                        handlerEntries++;
                        await db.Effects.AddAsync(new InboxScopeEffect { Id = id }, ct);
                    },
                    AbortToken
                );
        }
        handlerEntries.Should().Be(2);
        (await admit()).Disposition.Should().Be(InboxAdmissionDisposition.SucceededDuplicate);
        await using var finalScope = provider.CreateAsyncScope();
        (
            await finalScope
                .ServiceProvider.GetRequiredService<InboxScopeDbContext>()
                .Effects.CountAsync(effect => effect.Id == id, AbortToken)
        )
            .Should()
            .Be(1);
    }

    public enum FailurePoint
    {
        BeforeEntry,
        Handler,
        SaveChanges,
        BeforeCommit,
        AfterCommit,
        Rollback,
        Disposal,
    }

    public sealed class RetryableInboxException() : Exception("Injected transient inbox failure.");

    private sealed class FaultState(FailurePoint failurePoint)
    {
        public bool Armed { get; set; }
        public bool Injected { get; private set; }
        public int TransactionStarts { get; set; }

        public void ThrowOnce(FailurePoint point)
        {
            if (Armed && !Injected && failurePoint == point)
            {
                Injected = true;
                throw new RetryableInboxException();
            }
        }

        public void FailCommitForRollback()
        {
            if (Armed && !Injected && failurePoint is FailurePoint.Rollback)
            {
                throw new InvalidOperationException("Trigger the rollback path before commit.");
            }
        }
    }

    private sealed class TransactionFaultInterceptor(FaultState fault) : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default
        )
        {
            if (fault.Armed)
            {
                fault.TransactionStarts++;
                fault.ThrowOnce(FailurePoint.BeforeEntry);
            }
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            fault.ThrowOnce(FailurePoint.BeforeCommit);
            fault.FailCommitForRollback();
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            fault.ThrowOnce(FailurePoint.AfterCommit);
            return Task.CompletedTask;
        }

        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            fault.ThrowOnce(FailurePoint.Rollback);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class SaveFaultInterceptor(FaultState fault) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            fault.ThrowOnce(FailurePoint.SaveChanges);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class DisposalFaultInterceptor(FaultState fault) : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionClosingAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result
        )
        {
            // EF closes the connection while disposing the failed transaction, outside the handler body.
            fault.ThrowOnce(FailurePoint.Disposal);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class OneShotRetryExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is RetryableInboxException;
    }

    private sealed class OneShotRetryExecutionStrategyFactory(ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new OneShotRetryExecutionStrategy(dependencies);
    }
}
