// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Runtime;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>Exercises the real consume executor and EF inbox transaction with each relational provider.</summary>
public abstract class TransactionalInboxScopeConformanceTests : TestBase
{
    protected abstract void ConfigureContext(DbContextOptionsBuilder options);

    protected abstract void ConfigureStorage(MessagingSetupBuilder setup);

    protected abstract string CreateEffectsTableSql { get; }

    protected abstract string ReplaceAttemptSql(string receivedTable);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task should_commit_or_rollback_handler_state_and_outbox_in_the_attempt_scope(
        bool explicitSave,
        bool rejectFence
    )
    {
        var state = new ExecutionState { ExplicitSave = explicitSave };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(state);
        services.AddDbContext<InboxScopeDbContext>(ConfigureContext);
        services
            .AddHeadlessMessaging(setup =>
            {
                setup.UseInMemory();
                ConfigureStorage(setup);
                setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.Transactional;
                setup.Bus.ForMessage<InboxScopeMessage>(message =>
                    message
                        .Contract("tests.inbox-scope")
                        .Consumer<InboxScopeConsumer>(consumer =>
                            consumer.ConsumerIdentity("tests.inbox-scope.consumer").Group("tests.inbox-scope")
                        )
                );
                setup.Bus.ForMessage<InboxScopeOutput>(message => message.Contract("tests.inbox-scope.output"));
                setup.Queue.ForMessage<InboxScopeOutput>(message => message.Contract("tests.inbox-scope.output"));
            })
            .AddBusConsumeMiddleware<InboxScopeMiddleware>();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var storage = provider.GetRequiredService<IDataStorage>();
        var initializer = provider.GetRequiredService<IStorageInitializer>();
        await initializer.InitializeAsync(AbortToken);
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<InboxScopeDbContext>();
            await db.Database.ExecuteSqlRawAsync(CreateEffectsTableSql, AbortToken);
        }

        var descriptor = provider
            .GetRequiredService<MethodMatcherCache>()
            .GetCandidatesMethodsOfGroupNameGrouped()
            .Values.SelectMany(descriptors => descriptors)
            .Single();
        var origin = new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = state.Id.ToString(),
                [Headers.MessageName] = descriptor.MessageName,
                [Headers.Group] = descriptor.GroupName,
            },
            new InboxScopeMessage(state.Id)
        );
        ValueTask<InboxAdmissionResult> admit() =>
            storage.AdmitReceivedMessageAsync(
                descriptor.MessageName,
                descriptor.GroupName,
                descriptor.ConsumerIdentity!,
                descriptor.MessageContractVersion!,
                new MediumMessage
                {
                    StorageId = Guid.Empty,
                    Origin = origin,
                    Content = string.Empty,
                    Lane = MessageLane.Bus,
                },
                cancellationToken: AbortToken
            );

        var admitted = await admit();
        admitted.Disposition.Should().Be(InboxAdmissionDisposition.Winner);
        if (rejectFence)
        {
            state.BeforeHandlerReturns = async cancellationToken =>
            {
                // A separate committed connection replaces the persisted fence after application work.
                // Completing with the original attempt must now fail the real provider's CAS.
                await using var competingScope = provider.CreateAsyncScope();
                var db = competingScope.ServiceProvider.GetRequiredService<InboxScopeDbContext>();
                await db.Database.OpenConnectionAsync(cancellationToken);
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = ReplaceAttemptSql(initializer.GetReceivedTableName());
                var id = command.CreateParameter();
                id.ParameterName = "@id";
                id.Value = admitted.Message.StorageId;
                command.Parameters.Add(id);
                var attempt = command.CreateParameter();
                attempt.ParameterName = "@attempt";
                attempt.Value = Guid.NewGuid();
                command.Parameters.Add(attempt);
                (await command.ExecuteNonQueryAsync(cancellationToken)).Should().Be(1);
            };
        }

        await using var dispatchScope = provider.CreateAsyncScope();
        var result = await provider
            .GetRequiredService<ISubscribeExecutor>()
            .ExecuteAsync(admitted.Message, dispatchScope.ServiceProvider, descriptor, AbortToken);

        result.Succeeded.Should().Be(!rejectFence);
        if (rejectFence)
        {
            result.Exception.Should().BeOfType<StaleInboxAttemptException>();
        }

        state.HandlerEntries.Should().Be(1);
        state.HandlerContext.Should().BeSameAs(state.MiddlewareContext);
        state.HandlerHadTransaction.Should().BeTrue("the configured context must own the runner's transaction");
        state.MiddlewareHadTransaction.Should().BeTrue();
        state.ContextDisposedBeforeHandlerReturned.Should().BeFalse();
        state.HandlerContext!.Disposed.Should().BeTrue("the attempt scope must end after commit or rollback");

        await using var verificationScope = provider.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<InboxScopeDbContext>();
        (await verificationDb.Effects.AnyAsync(effect => effect.Id == state.Id, AbortToken)).Should().Be(!rejectFence);
        var monitoring = storage.GetMonitoringApi();
        foreach (var lane in new[] { MessageLane.Bus, MessageLane.Queue })
        {
            var outgoing = await monitoring.GetMessagesAsync(
                new MessageQuery
                {
                    MessageType = MessageType.Publish,
                    Name = "tests.inbox-scope.output",
                    Content = state.Id.ToString(),
                    Lane = lane,
                    PageSize = 10,
                },
                AbortToken
            );
            outgoing.Items.Should().HaveCount(rejectFence ? 0 : 1);
        }

        if (!rejectFence)
        {
            (await admit()).Disposition.Should().Be(InboxAdmissionDisposition.SucceededDuplicate);
        }
        else
        {
            (await admit()).Disposition.Should().Be(InboxAdmissionDisposition.InFlightDuplicate);
        }
    }

    public sealed record InboxScopeMessage(Guid Id);

    public sealed record InboxScopeOutput(Guid Id);

    public sealed class InboxScopeEffect
    {
        public Guid Id { get; set; }
    }

    public sealed class InboxScopeDbContext(DbContextOptions<InboxScopeDbContext> options) : DbContext(options)
    {
        public DbSet<InboxScopeEffect> Effects => Set<InboxScopeEffect>();

        public bool Disposed { get; private set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<InboxScopeEffect>().ToTable("InboxScopeEffects").HasKey(effect => effect.Id);
        }

        public override async ValueTask DisposeAsync()
        {
            Disposed = true;
            await base.DisposeAsync();
        }
    }

    public sealed class ExecutionState
    {
        public Guid Id { get; } = Guid.NewGuid();
        public bool ExplicitSave { get; init; }
        public InboxScopeDbContext? HandlerContext { get; set; }
        public InboxScopeDbContext? MiddlewareContext { get; set; }
        public bool HandlerHadTransaction { get; set; }
        public bool MiddlewareHadTransaction { get; set; }
        public bool ContextDisposedBeforeHandlerReturned { get; set; }
        public int HandlerEntries { get; set; }
        public Func<CancellationToken, Task>? BeforeHandlerReturns { get; set; }
    }

    public sealed class InboxScopeMiddleware(InboxScopeDbContext db, ExecutionState state)
        : IConsumeMiddleware<ConsumeContext>
    {
        public async ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next)
        {
            state.MiddlewareContext = db;
            state.MiddlewareHadTransaction = db.Database.CurrentTransaction is not null;
            await next();
            state.ContextDisposedBeforeHandlerReturned = db.Disposed;
        }
    }

    public sealed class InboxScopeConsumer(InboxScopeDbContext db, ExecutionState state, IBus bus, IQueue queue)
        : IConsume<InboxScopeMessage>
    {
        public async ValueTask ConsumeAsync(
            ConsumeContext<InboxScopeMessage> context,
            CancellationToken cancellationToken
        )
        {
            state.HandlerEntries++;
            state.HandlerContext = db;
            state.HandlerHadTransaction = db.Database.CurrentTransaction is not null;
            db.Effects.Add(new InboxScopeEffect { Id = context.Message.Id });
            if (state.ExplicitSave)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            var output = new InboxScopeOutput(context.Message.Id);
            await bus.PublishAsync(
                output,
                new PublishOptions { DeliveryMode = DeliveryMode.Durable },
                cancellationToken
            );
            await queue.EnqueueAsync(
                output,
                new EnqueueOptions { DeliveryMode = DeliveryMode.Durable },
                cancellationToken
            );
            if (state.BeforeHandlerReturns is { } beforeHandlerReturns)
            {
                await beforeHandlerReturns(cancellationToken);
            }
        }
    }
}
