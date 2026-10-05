// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.EntityFramework;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tests.Helpers;

namespace Tests;

/// <summary>Exercises the real consume executor and EF inbox transaction with each relational provider.</summary>
public abstract class TransactionalInboxScopeConformanceTests : TestBase
{
    protected abstract void ConfigureContext(DbContextOptionsBuilder options);

    protected abstract void ConfigureStorage(MessagingSetupBuilder setup);

    protected abstract string CreateEffectsTableSql { get; }

    protected abstract string ReplaceAttemptSql(string receivedTable);

    [Theory]
    [InlineData(false, false, true, null, false, MessageLane.Bus)]
    [InlineData(true, false, true, null, false, MessageLane.Bus)]
    [InlineData(false, true, true, null, false, MessageLane.Bus)]
    [InlineData(true, true, true, null, false, MessageLane.Bus)]
    [InlineData(false, false, true, "dispatch-tenant", false, MessageLane.Bus)]
    [InlineData(false, true, true, "dispatch-tenant", false, MessageLane.Bus)]
    [InlineData(false, false, false, "dispatch-tenant", false, MessageLane.Bus)]
    [InlineData(true, false, true, null, true, MessageLane.Bus)]
    [InlineData(false, true, true, null, true, MessageLane.Bus)]
    [InlineData(false, false, true, "dispatch-tenant", true, MessageLane.Bus)]
    [InlineData(true, true, true, "dispatch-tenant", true, MessageLane.Bus)]
    [InlineData(false, false, true, "dispatch-tenant", false, MessageLane.Queue)]
    [InlineData(true, true, true, null, false, MessageLane.Queue)]
    [InlineData(false, false, false, "dispatch-tenant", false, MessageLane.Queue)]
    [InlineData(false, false, true, "dispatch-tenant", true, MessageLane.Queue)]
    public async Task should_commit_or_rollback_handler_state_and_outbox_in_the_attempt_scope(
        bool explicitSave,
        bool rejectFence,
        bool propagateTenant,
        string? ambientTenant,
        bool pooled,
        MessageLane lane
    )
    {
        var state = new ExecutionState { ExplicitSave = explicitSave };
        var builder = Host.CreateApplicationBuilder();
        var services = builder.Services;
        services.AddLogging();
        services.AddSingleton(state);

        if (pooled)
        {
            services.AddHeadlessDbContextPool<InboxScopeDbContext>(ConfigureContext);
        }
        else
        {
            services.AddHeadlessDbContext<InboxScopeDbContext>(ConfigureContext);
        }

        builder.AddHeadlessTenancy(tenancy => tenancy.EntityFramework(ef => ef.GuardTenantWrites()));
        if (propagateTenant)
        {
            builder.AddHeadlessTenancy(tenancy => tenancy.Messaging(messaging => messaging.PropagateTenant()));
        }

        var messagingBuilder = services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            ConfigureStorage(setup);
            setup.Options.MinimumInboxGuarantee = InboxGuarantee.Transactional;
        });
        messagingBuilder.AddBusConsumeMiddleware<InboxScopeMiddleware>();
        messagingBuilder.AddQueueConsumeMiddleware<InboxScopeMiddleware>();
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<InboxScopeMessage>("tests.inbox-scope");
            messaging.Message<InboxScopeOutput>("tests.inbox-scope.output");
            if (lane is MessageLane.Queue)
            {
                messaging.AddModule<InboxScopeQueueModule>();
            }
            else
            {
                messaging.AddModule<InboxScopeModule>();
            }
        });

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var currentTenant = provider.GetRequiredService<ICurrentTenant>();
        using var callerTenantScope = currentTenant.Change(ambientTenant);
        var storage = provider.GetRequiredService<IDataStorage>();
        var tableNames = provider.GetRequiredService<IStorageTableNames>();
        await provider.ApplyMessagingSchemaAsync(AbortToken);
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<InboxScopeDbContext>();
            await db.Database.ExecuteSqlRawAsync(CreateEffectsTableSql, AbortToken);
        }

        var descriptor = provider
            .GetRequiredService<MethodMatcherCache>()
            .GetCandidatesBySubscriptionName()
            .Values.SelectMany(descriptors => descriptors)
            .Single();
        var origin = new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = state.Id.ToString(),
                [Headers.MessageName] = descriptor.MessageName,
                [Headers.ConsumerIdentity] = descriptor.ConsumerIdentity,
                [Headers.TenantId] = "envelope-tenant",
            },
            new InboxScopeMessage(state.Id)
        );
        ValueTask<InboxAdmissionResult> admit() =>
            storage.AdmitReceivedMessageAsync(
                descriptor.MessageName,
                descriptor.ConsumerIdentity!,
                descriptor.MessageContractVersion!,
                new MediumMessage
                {
                    StorageId = Guid.Empty,
                    Origin = origin,
                    Content = string.Empty,
                    Lane = lane,
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
                command.CommandText = ReplaceAttemptSql(tableNames.GetReceivedTableName());
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

        result.Succeeded.Should().Be(!rejectFence, "{0}", result.Exception);
        currentTenant
            .Id.Should()
            .Be(ambientTenant, "the attempt must restore the caller's tenant on success or failure");
        if (rejectFence)
        {
            result.Exception.Should().BeOfType<StaleInboxAttemptException>();
        }

        state.HandlerEntries.Should().Be(1);
        state.HandlerContext.Should().BeSameAs(state.MiddlewareContext);
        state.HandlerHadTransaction.Should().BeTrue("the configured context must own the runner's transaction");
        state.MiddlewareHadTransaction.Should().BeTrue();
        state.ContextDisposedBeforeHandlerReturned.Should().BeFalse();
        state
            .HandlerContext!.DisposeCount.Should()
            .BeGreaterThan(state.DisposeCountAtHandlerEntry, "the attempt scope must end after commit or rollback");
        var expectedTenant = propagateTenant ? "envelope-tenant" : ambientTenant;
        if (!pooled)
        {
            // A pooled instance is constructed once, not per resolution, so only a per-scope context proves the
            // attempt's tenant is in place before the scope resolves it.
            state.HandlerContext.TenantAtResolution.Should().Be(expectedTenant);
        }

        state.HandlerTenant.Should().Be(expectedTenant);
        state.TenantsAtSave.Should().NotBeEmpty().And.AllBe(expectedTenant);

        await using var verificationScope = provider.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<InboxScopeDbContext>();
        var effect = await verificationDb
            .Effects.IgnoreQueryFilters()
            .SingleOrDefaultAsync(e => e.Id == state.Id, AbortToken);
        if (rejectFence)
        {
            effect.Should().BeNull();
        }
        else
        {
            effect.Should().NotBeNull();
            effect!.TenantId.Should().Be(expectedTenant);
        }

        var monitoring = storage.GetMonitoringApi();
        foreach (var outgoingLane in new[] { MessageLane.Bus, MessageLane.Queue })
        {
            var outgoing = await monitoring.GetMessagesAsync(
                new MessageQuery
                {
                    MessageType = MessageType.Publish,
                    Name = "tests.inbox-scope.output",
                    Content = state.Id.ToString(),
                    Lane = outgoingLane,
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

    public sealed class InboxScopeEffect : IMultiTenant
    {
        public Guid Id { get; set; }

        public string? TenantId { get; set; }
    }

    // Pool-safe: it keeps per-attempt observations in the singleton ExecutionState and counts disposals instead of
    // flagging them, because a pooled instance serves several scopes in one test.
    public sealed class InboxScopeDbContext(
        DbContextOptions<InboxScopeDbContext> options,
        ICurrentTenant currentTenant,
        ExecutionState state
    ) : HeadlessDbContext(options)
    {
        public DbSet<InboxScopeEffect> Effects => Set<InboxScopeEffect>();

        public override string? DefaultSchema => null;

        public string? TenantAtResolution { get; } = currentTenant.Id;

        public int DisposeCount { get; private set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<InboxScopeEffect>().ToTable("TenantInboxScopeEffects").HasKey(effect => effect.Id);
            base.OnModelCreating(modelBuilder);
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            state.TenantsAtSave.Add(TenantId);
            return base.SaveChangesAsync(cancellationToken);
        }

        public override async ValueTask DisposeAsync()
        {
            DisposeCount++;
            await base.DisposeAsync();
        }
    }

    public sealed class ExecutionState
    {
        public Guid Id { get; } = Guid.NewGuid();
        public bool ExplicitSave { get; init; }
        public string? HandlerTenant { get; set; }
        public InboxScopeDbContext? HandlerContext { get; set; }
        public InboxScopeDbContext? MiddlewareContext { get; set; }
        public bool HandlerHadTransaction { get; set; }
        public bool MiddlewareHadTransaction { get; set; }
        public bool ContextDisposedBeforeHandlerReturned { get; set; }
        public int DisposeCountAtHandlerEntry { get; set; }
        public List<string?> TenantsAtSave { get; } = [];
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
            var disposeCount = db.DisposeCount;
            await next();
            state.ContextDisposedBeforeHandlerReturned = db.DisposeCount != disposeCount;
        }
    }

    public sealed class InboxScopeModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog) =>
            catalog.AddBusConsumer<InboxScopeConsumer, InboxScopeMessage>(
                "tests.inbox-scope.consumer",
                everyInstance: false,
                TestConsumerDispatch.FromServices<InboxScopeConsumer, InboxScopeMessage>()
            );
    }

    /// <summary>The same consumer declared on the Queue lane, so the theory proves the attempt scope on both lanes.</summary>
    public sealed class InboxScopeQueueModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog) =>
            catalog.AddQueueConsumer<InboxScopeConsumer, InboxScopeMessage>(
                "tests.inbox-scope.queue-consumer",
                TestConsumerDispatch.FromServices<InboxScopeConsumer, InboxScopeMessage>()
            );
    }

    public sealed class InboxScopeConsumer(InboxScopeDbContext db, ExecutionState state) : IConsume<InboxScopeMessage>
    {
        public async ValueTask ConsumeAsync(
            ConsumeContext<InboxScopeMessage> context,
            CancellationToken cancellationToken
        )
        {
            state.HandlerEntries++;
            state.HandlerContext = db;
            state.DisposeCountAtHandlerEntry = db.DisposeCount;
            state.HandlerTenant = db.TenantId;
            state.HandlerHadTransaction = db.Database.CurrentTransaction is not null;
            await db.Effects.AddAsync(new InboxScopeEffect { Id = context.Message.Id }, cancellationToken);
            if (state.ExplicitSave)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            // The runner enlisted this attempt in a unit and handed it over on the context; publishing through
            // it is what makes the rows share the attempt's fate. An injected IBus/IQueue would write standalone
            // rows that survive the fence rejection this test forces.
            var unitOfWork =
                context.UnitOfWork
                ?? throw new InvalidOperationException(
                    "The transactional inbox guarantee must hand the consumer its unit."
                );
            var output = new InboxScopeOutput(context.Message.Id);
            await unitOfWork.Outbox.PublishAsync(output, cancellationToken);
            await unitOfWork.Outbox.EnqueueAsync(output, cancellationToken);
            if (state.BeforeHandlerReturns is { } beforeHandlerReturns)
            {
                await beforeHandlerReturns(cancellationToken);
            }
        }
    }
}
