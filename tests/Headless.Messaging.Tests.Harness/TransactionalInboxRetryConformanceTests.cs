// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Tests.Guarantees;
using Tests.Toxiproxy;

namespace Tests;

/// <summary>Verifies EF retries cannot replay a reserved inbox attempt against real relational storage.</summary>
public abstract class TransactionalInboxRetryConformanceTests : TestBase
{
    protected abstract void ConfigureContext(DbContextOptionsBuilder options);

    protected abstract void ConfigureStorage(MessagingSetupBuilder setup);

    protected abstract string CreateEffectsTableSql { get; }

    /// <summary>
    /// Gets the host port the database container publishes, so a test can put a Toxiproxy route in front of it, or
    /// <see langword="null"/> when the leaf cannot route its database.
    /// </summary>
    protected virtual int? DatabaseHostPort => null;

    /// <summary>
    /// Configures the context like <see cref="ConfigureContext"/>, but connecting through <paramref name="host"/> and
    /// <paramref name="port"/>. The EF-registered storage reads its connection from the context, so it follows.
    /// </summary>
    protected virtual void ConfigureContextThrough(DbContextOptionsBuilder options, string host, int port) =>
        throw new NotSupportedException("This leaf cannot route its database through a proxy.");

    [Theory]
    [InlineData(FailurePoint.BeforeEntry)]
    [InlineData(FailurePoint.Handler)]
    [InlineData(FailurePoint.SaveChanges)]
    [InlineData(FailurePoint.BeforeCommit)]
    [InlineData(FailurePoint.AfterCommit)]
    [InlineData(FailurePoint.Rollback)]
    [InlineData(FailurePoint.Disposal)]
    [Guarantee("MSG-CONSUME-08")]
    public async Task should_retry_only_before_handler_entry_and_recover_with_a_new_fence(FailurePoint failurePoint)
    {
        var fault = new FaultState(failurePoint);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<InboxRetryDbContext>(options =>
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
        await provider.ApplyMessagingSchemaAsync(AbortToken);
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<InboxRetryDbContext>();
            await db.Database.ExecuteSqlRawAsync(CreateEffectsTableSql, AbortToken);
        }

        var id = Guid.NewGuid();
        var origin = new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = id.ToString(),
                [Headers.MessageName] = "tests.inbox-retry",
                [Headers.ConsumerIdentity] = "tests.inbox-retry.consumer",
            },
            id
        );
        ValueTask<InboxAdmissionResult> admit() =>
            storage.AdmitReceivedMessageAsync(
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
            var db = attemptScope.ServiceProvider.GetRequiredService<InboxRetryDbContext>();
            error = await Record.ExceptionAsync(() =>
                attemptScope
                    .ServiceProvider.GetRequiredService<IInboxTransactionRunner>()
                    .ExecuteAsync(
                        message,
                        async (_, ct) =>
                        {
                            handlerEntries++;
                            await db.Effects.AddAsync(new InboxRetryEffect { Id = id }, ct);
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
            var db = verificationScope.ServiceProvider.GetRequiredService<InboxRetryDbContext>();
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
                retryDelay: RetryDelay.Exactly(TimeSpan.Zero),
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();
        var recovered = (await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, null, AbortToken)).Single(
            candidate => candidate.StorageId == message.StorageId
        );
        recovered.InboxAttemptFence!.AttemptId.Should().NotBe(message.InboxAttemptFence!.AttemptId);
        await using (var staleScope = provider.CreateAsyncScope())
        {
            var staleError = await Record.ExceptionAsync(() =>
                staleScope
                    .ServiceProvider.GetRequiredService<IInboxTransactionRunner>()
                    .ExecuteAsync(message, (_, _) => Task.CompletedTask, AbortToken)
            );
            staleError.Should().BeOfType<StaleInboxAttemptException>();
        }

        originalInlineAttempts = recovered.InlineAttempts++;
        (await storage.ReserveReceiveAttemptAsync(recovered, originalInlineAttempts, AbortToken)).Should().BeTrue();
        await using (var recoveryScope = provider.CreateAsyncScope())
        {
            var db = recoveryScope.ServiceProvider.GetRequiredService<InboxRetryDbContext>();
            await recoveryScope
                .ServiceProvider.GetRequiredService<IInboxTransactionRunner>()
                .ExecuteAsync(
                    recovered,
                    async (_, ct) =>
                    {
                        handlerEntries++;
                        await db.Effects.AddAsync(new InboxRetryEffect { Id = id }, ct);
                    },
                    AbortToken
                );
        }

        handlerEntries.Should().Be(2);
        (await admit()).Disposition.Should().Be(InboxAdmissionDisposition.SucceededDuplicate);
        await using var finalScope = provider.CreateAsyncScope();
        (
            await finalScope
                .ServiceProvider.GetRequiredService<InboxRetryDbContext>()
                .Effects.CountAsync(effect => effect.Id == id, AbortToken)
        )
            .Should()
            .Be(1);
    }

    [Fact]
    [Guarantee("MSG-CONSUME-08")]
    public async Task should_report_success_when_the_drain_faults_after_a_durable_commit()
    {
        // A post-commit drain fault (an OnCompleted callback throwing once the unit is Completed) must not turn a
        // durably committed attempt into a failed one: the retry machinery would re-invoke the consumer and then
        // record the message as failed against a row that already succeeded.
        var fault = new FaultState(FailurePoint.BeforeEntry);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<InboxRetryDbContext>(options =>
        {
            ConfigureContext(options);
            options.AddInterceptors(new TransactionFaultInterceptor(fault));
        });
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            ConfigureStorage(setup);
            setup.Options.CommandTimeout = TimeSpan.FromSeconds(1);
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var storage = provider.GetRequiredService<IDataStorage>();
        await provider.ApplyMessagingSchemaAsync(AbortToken);
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<InboxRetryDbContext>();
            await db.Database.ExecuteSqlRawAsync(CreateEffectsTableSql, AbortToken);
        }

        var id = Guid.NewGuid();
        var origin = new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = id.ToString(),
                [Headers.MessageName] = "tests.inbox-drain",
                [Headers.ConsumerIdentity] = "tests.inbox-drain.consumer",
            },
            id
        );
        ValueTask<InboxAdmissionResult> admit() =>
            storage.AdmitReceivedMessageAsync(
                "tests.inbox-drain",
                "tests.inbox-drain.consumer",
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
        var drainAttempts = 0;
        Exception? error;
        await using (var attemptScope = provider.CreateAsyncScope())
        {
            var db = attemptScope.ServiceProvider.GetRequiredService<InboxRetryDbContext>();
            error = await Record.ExceptionAsync(() =>
                attemptScope
                    .ServiceProvider.GetRequiredService<IInboxTransactionRunner>()
                    .ExecuteAsync(
                        message,
                        async (unitOfWork, ct) =>
                        {
                            handlerEntries++;
                            unitOfWork
                                .Should()
                                .NotBeNull("the runner enlists its transaction for the handler's duration");
                            unitOfWork.OnCompleted(() =>
                            {
                                drainAttempts++;
                                throw new InvalidOperationException("drain down");
                            });
                            await db.Effects.AddAsync(new InboxRetryEffect { Id = id }, ct);
                        },
                        AbortToken
                    )
            );
        }

        error.Should().BeNull("the row is durable; the drain fault is logged, never reported as an attempt failure");
        handlerEntries.Should().Be(1);
        drainAttempts.Should().Be(1, "the drain ran exactly once — no second CompleteAsync after the fault");
        await using (var verificationScope = provider.CreateAsyncScope())
        {
            var db = verificationScope.ServiceProvider.GetRequiredService<InboxRetryDbContext>();
            (await db.Effects.AnyAsync(effect => effect.Id == id, AbortToken)).Should().BeTrue();
        }

        (await admit()).Disposition.Should().Be(InboxAdmissionDisposition.SucceededDuplicate);
    }

    [Fact]
    [Guarantee("MSG-INBOX-06")]
    public async Task should_resolve_a_commit_whose_reply_is_lost_as_committed_without_running_the_handler_again()
    {
        if (DatabaseHostPort is not { } databaseHostPort)
        {
            Assert.Skip("This leaf cannot route its database through a proxy.");
            return;
        }

        // given: the context, and the storage that reads its connection from it, reach the database through a route
        // that can drop the database's replies
        await using var route = await ToxiproxyDatabaseRoute.StartAsync(databaseHostPort, AbortToken);
        var replyCut = new CommitReplyCut(route.Proxy);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<InboxRetryDbContext>(options =>
        {
            ConfigureContextThrough(options, route.Proxy.Host, route.Proxy.Port);
            options.AddInterceptors(replyCut);
        });
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            ConfigureStorage(setup);
            setup.Options.CommandTimeout = TimeSpan.FromSeconds(5);
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var storage = provider.GetRequiredService<IDataStorage>();
        await provider.ApplyMessagingSchemaAsync(AbortToken);
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<InboxRetryDbContext>();
            await db.Database.ExecuteSqlRawAsync(CreateEffectsTableSql, AbortToken);
        }

        var id = Guid.NewGuid();
        var origin = new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = id.ToString(),
                [Headers.MessageName] = "tests.inbox-lost-reply",
                [Headers.ConsumerIdentity] = "tests.inbox-lost-reply.consumer",
            },
            id
        );
        ValueTask<InboxAdmissionResult> admit() =>
            storage.AdmitReceivedMessageAsync(
                "tests.inbox-lost-reply",
                "tests.inbox-lost-reply.consumer",
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

        // when: the attempt's commit reaches the database, but the database's reply to it never arrives
        var handlerEntries = 0;
        Exception? error;
        replyCut.Armed = true;
        await using (var attemptScope = provider.CreateAsyncScope())
        {
            var db = attemptScope.ServiceProvider.GetRequiredService<InboxRetryDbContext>();
            error = await Record.ExceptionAsync(() =>
                attemptScope
                    .ServiceProvider.GetRequiredService<IInboxTransactionRunner>()
                    .ExecuteAsync(
                        message,
                        async (_, ct) =>
                        {
                            handlerEntries++;
                            await db.Effects.AddAsync(new InboxRetryEffect { Id = id }, ct);
                        },
                        AbortToken
                    )
            );
        }

        // then: the client saw its commit fail, and the attempt still resolved as the success it was
        replyCut
            .CommitFailures.Should()
            .ContainSingle("the route must cut the commit's reply, or this test proves nothing");
        error.Should().BeNull("the commit probe finds the durable commit, so the attempt reports success");
        handlerEntries.Should().Be(1);
        await using (var verificationScope = provider.CreateAsyncScope())
        {
            (
                await verificationScope
                    .ServiceProvider.GetRequiredService<InboxRetryDbContext>()
                    .Effects.CountAsync(effect => effect.Id == id, AbortToken)
            )
                .Should()
                .Be(1, "the handler's effect committed once and was not written again");
        }

        // and: a redelivery of the message is a duplicate of the succeeded attempt, not a second run
        (await admit())
            .Disposition.Should()
            .Be(InboxAdmissionDisposition.SucceededDuplicate);
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

    public sealed class InboxRetryEffect
    {
        public Guid Id { get; set; }
    }

    // Retry fault injection needs a plain context, independent of the tenant-scope suite's service graph.
    public sealed class InboxRetryDbContext(DbContextOptions<InboxRetryDbContext> options) : DbContext(options)
    {
        public DbSet<InboxRetryEffect> Effects => Set<InboxRetryEffect>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<InboxRetryEffect>().ToTable("InboxScopeEffects").HasKey(effect => effect.Id);
    }

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

    /// <summary>
    /// Drops the database's reply to the next commit. Requests still reach the database, so the commit lands; the reply
    /// is discarded and the route closes the connection a second later, which is what the client sees fail.
    /// </summary>
    private sealed class CommitReplyCut(ToxiproxyProxy proxy) : DbTransactionInterceptor
    {
        private const string _ToxicName = "lost-commit-reply";

        public bool Armed { get; set; }

        public List<Exception> CommitFailures { get; } = [];

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            if (Armed)
            {
                await proxy.AddToxicAsync(
                    _ToxicName,
                    "timeout",
                    new Dictionary<string, object>(StringComparer.Ordinal) { ["timeout"] = 1000 },
                    cancellationToken
                );
            }

            return result;
        }

        public override async Task TransactionFailedAsync(
            DbTransaction transaction,
            TransactionErrorEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            if (Armed && string.Equals(eventData.Action, "Commit", StringComparison.Ordinal))
            {
                Armed = false;
                CommitFailures.Add(eventData.Exception);
                // Only the commit's reply is lost: the runner's probe that follows must reach the database.
                await proxy.RemoveToxicAsync(_ToxicName, CancellationToken.None);
            }
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
