// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Domain;
using Headless.EntityFramework;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Runtime;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>Exercises the real consume executor and EF inbox transaction with each relational provider.</summary>
public abstract class TransactionalInboxScopeConformanceTests : TestBase
{
    protected abstract void ConfigureContext(DbContextOptionsBuilder options);

    protected abstract void ConfigureStorage(MessagingSetupBuilder setup);

    protected abstract string CreateEffectsTableSql { get; }

    protected abstract string ReplaceAttemptSql(string receivedTable);

    [Theory]
    [InlineData(false, false, true, null)]
    [InlineData(true, false, true, null)]
    [InlineData(false, true, true, null)]
    [InlineData(true, true, true, null)]
    [InlineData(false, false, true, "dispatch-tenant")]
    [InlineData(false, true, true, "dispatch-tenant")]
    [InlineData(false, false, false, "dispatch-tenant")]
    public async Task should_commit_or_rollback_handler_state_and_outbox_in_the_attempt_scope(
        bool explicitSave,
        bool rejectFence,
        bool propagateTenant,
        string? ambientTenant
    )
    {
        var state = new ExecutionState { ExplicitSave = explicitSave };
        var builder = Host.CreateApplicationBuilder();
        var services = builder.Services;
        services.AddLogging();
        services.AddSingleton(state);
        services.AddHeadlessDbContext<InboxScopeDbContext>(ConfigureContext);
        services.AddHeadlessTenantWriteGuard();
        if (propagateTenant)
        {
            builder.AddHeadlessTenancy(tenancy => tenancy.Messaging(messaging => messaging.PropagateTenant()));
        }
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
        var currentTenant = provider.GetRequiredService<ICurrentTenant>();
        using var callerTenantScope = currentTenant.Change(ambientTenant);
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
                [Headers.TenantId] = "envelope-tenant",
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
        state.HandlerContext!.Disposed.Should().BeTrue("the attempt scope must end after commit or rollback");
        var expectedTenant = propagateTenant ? "envelope-tenant" : ambientTenant;
        state.HandlerContext.TenantAtResolution.Should().Be(expectedTenant);
        state.HandlerTenant.Should().Be(expectedTenant);
        state.HandlerContext.TenantsAtSave.Should().NotBeEmpty().And.AllBe(expectedTenant);

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

    public sealed class InboxScopeEffect : IMultiTenant
    {
        public Guid Id { get; set; }

        public string? TenantId { get; set; }
    }

    public sealed class InboxScopeDbContext(
        HeadlessDbContextServices services,
        DbContextOptions<InboxScopeDbContext> options,
        ICurrentTenant currentTenant
    ) : HeadlessDbContext(services, options)
    {
        public DbSet<InboxScopeEffect> Effects => Set<InboxScopeEffect>();

        public override string? DefaultSchema => null;

        public string? TenantAtResolution { get; } = currentTenant.Id;

        public List<string?> TenantsAtSave { get; } = [];

        public bool Disposed { get; private set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<InboxScopeEffect>().ToTable("TenantInboxScopeEffects").HasKey(effect => effect.Id);
            base.OnModelCreating(modelBuilder);
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            TenantsAtSave.Add(TenantId);
            return base.SaveChangesAsync(cancellationToken);
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
        public string? HandlerTenant { get; set; }
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
            state.HandlerTenant = db.TenantId;
            state.HandlerHadTransaction = db.Database.CurrentTransaction is not null;
            await db.Effects.AddAsync(new InboxScopeEffect { Id = context.Message.Id }, cancellationToken);
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
                new QueueOptions { DeliveryMode = DeliveryMode.Durable },
                cancellationToken
            );
            if (state.BeforeHandlerReturns is { } beforeHandlerReturns)
            {
                await beforeHandlerReturns(cancellationToken);
            }
        }
    }
}
