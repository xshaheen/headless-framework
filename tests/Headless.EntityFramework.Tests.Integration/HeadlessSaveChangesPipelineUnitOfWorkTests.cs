// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Abstractions;
using Headless.Domain;
using Headless.EntityFramework;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Tests.Fixture;

namespace Tests;

/// <summary>
/// Proves the save pipeline's unit-of-work contract (R11/KD13) against PostgreSQL: a pipeline-owned save enlists
/// its own transaction and drains after the commit; a save inside a caller-owned transaction requires the unit
/// that owns it; a unit bound to a factory-created context is adopted into that context's scope for the save;
/// and a participant that prevents retry routes the fault out of the execution strategy's replay.
/// </summary>
[Collection<HeadlessDbContextTestFixture>]
public sealed class HeadlessSaveChangesPipelineUnitOfWorkTests(HeadlessDbContextTestFixture fixture) : TestBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_enlist_the_pipeline_owned_transaction_and_drain_after_the_commit(bool sync)
    {
        // given — no transaction on the context: the pipeline opens one and enlists it in the scope's unit of work.
        await using var provider = await _BuildProviderAsync();
        var evidence = provider.GetRequiredService<PipelineEvidence>();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PipelineTestDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        db.Probes.Add(_ProbeWithEvents("owned"));

        // when
        await _SaveAsync(db, sync, AbortToken);

        // then — the handler and the outbox dispatcher saw the same observed unit over the pipeline's transaction,
        // the after-commit registration drained once, and the unit ended with the save.
        evidence.HandlerCalls.Should().Be(1);
        evidence.HandlerCurrent.Should().NotBeNull();
        var unitOfWork = evidence.HandlerCurrent!;
        unitOfWork.Resource.Should().NotBeNull();
        unitOfWork.Resource!.IsOwned.Should().BeFalse("the pipeline commits; the unit only observes");
        evidence.HandlerTransaction.Should().BeSameAs(evidence.ContextTransaction);
        evidence.OutboxCurrent.Should().BeSameAs(unitOfWork);
        evidence.Completed.Should().Be(1);
        evidence.Failed.Should().BeEmpty();
        unitOfWork.State.Should().Be(UnitOfWorkState.Completed);
        manager.Current.Should().BeNull("the pipeline's unit ends with the save");
        (await _CountProbesAsync(provider)).Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_roll_the_pipeline_owned_unit_back_when_the_outbox_dispatch_faults(bool sync)
    {
        // given — the outbox dispatcher faults after the handler registered its callbacks.
        await using var provider = await _BuildProviderAsync();
        var evidence = provider.GetRequiredService<PipelineEvidence>();
        evidence.OutboxFault = static () => new InvalidOperationException("outbox down");
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PipelineTestDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        db.Probes.Add(_ProbeWithEvents("faulted"));

        // when
        var act = async () => await _SaveAsync(db, sync, AbortToken);

        // then — the original fault surfaces, the unit rolled back (not abandoned), nothing drained, no row.
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("outbox down");
        evidence.Completed.Should().Be(0);
        evidence.Failed.Should().Equal(UnitOfWorkFailureReason.RolledBack);
        manager.Current.Should().BeNull();
        (await _CountProbesAsync(provider)).Should().Be(0);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task should_save_inside_the_caller_owned_unit_and_register_nothing(bool sync, bool complete)
    {
        // given — the developer began the unit on the context; its transaction is the caller-owned one.
        await using var provider = await _BuildProviderAsync();
        var evidence = provider.GetRequiredService<PipelineEvidence>();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PipelineTestDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        await using (var unitOfWork = await manager.BeginAsync(db, cancellationToken: AbortToken))
        {
            db.Probes.Add(_ProbeWithEvents("caller-owned"));

            // when — the save runs inside the developer's unit.
            await _SaveAsync(db, sync, AbortToken);

            // then — handler and outbox dispatcher saw that unit; nothing drained before the developer decides.
            evidence.HandlerCurrent.Should().BeSameAs(unitOfWork);
            evidence.OutboxCurrent.Should().BeSameAs(unitOfWork);
            evidence.Completed.Should().Be(0, "nothing drains before CompleteAsync");
            (await _CountProbesAsync(provider)).Should().Be(0, "not committed yet");

            if (complete)
            {
                await unitOfWork.CompleteAsync(AbortToken);
            }
        }

        if (complete)
        {
            evidence.Completed.Should().Be(1);
            evidence.Failed.Should().BeEmpty();
            (await _CountProbesAsync(provider)).Should().Be(1);
        }
        else
        {
            evidence.Completed.Should().Be(0);
            evidence.Failed.Should().Equal(UnitOfWorkFailureReason.Abandoned);
            (await _CountProbesAsync(provider)).Should().Be(0, "dispose without CompleteAsync rolls back");
        }

        manager.Current.Should().BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_throw_before_any_dispatch_when_a_caller_owned_transaction_has_no_unit_of_work(bool sync)
    {
        // given — a plain caller-owned transaction, no unit of work, and an integration event on the entity.
        await using var provider = await _BuildProviderAsync();
        var evidence = provider.GetRequiredService<PipelineEvidence>();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PipelineTestDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(AbortToken);
        db.Probes.Add(_ProbeWithEvents("un-enlisted"));

        // when
        var act = async () => await _SaveAsync(db, sync, AbortToken);

        // then — fails with the remedy before the domain-event drain and before the outbox dispatch.
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*IUnitOfWorkManager.BeginAsync(db)*");
        evidence.HandlerCalls.Should().Be(0, "the guard runs before any domain-event dispatch");
        evidence.OutboxCalls.Should().Be(0, "the guard runs before any outbox dispatch");
        await transaction.RollbackAsync(AbortToken);
        (await _CountProbesAsync(provider)).Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_throw_when_the_scope_holds_only_a_resource_less_unit_of_work(bool sync)
    {
        // given — a resource-less root coordinates nothing transactional: a caller-owned transaction with
        // integration events under it would write the outbox autonomously, so it is refused the same way.
        await using var provider = await _BuildProviderAsync();
        var evidence = provider.GetRequiredService<PipelineEvidence>();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PipelineTestDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        await using var root = await manager.BeginAsync(cancellationToken: AbortToken);
        await using var transaction = await db.Database.BeginTransactionAsync(AbortToken);
        db.Probes.Add(_ProbeWithEvents("resource-less"));

        // when
        var act = async () => await _SaveAsync(db, sync, AbortToken);

        // then — refused without side effects; the root itself is untouched and still completes.
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*IUnitOfWorkManager.BeginAsync(db)*");
        evidence.HandlerCalls.Should().Be(0);
        evidence.OutboxCalls.Should().Be(0);
        root.State.Should().Be(UnitOfWorkState.Active);
        await transaction.RollbackAsync(AbortToken);
        await root.CompleteAsync(AbortToken);
        (await _CountProbesAsync(provider)).Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_save_plainly_under_a_caller_owned_transaction_without_integration_events(bool sync)
    {
        // given — ordinary EF usage: a caller-owned transaction and a save that only raises domain events needs
        // no unit of work; the guard is keyed on integration events.
        await using var provider = await _BuildProviderAsync();
        var evidence = provider.GetRequiredService<PipelineEvidence>();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PipelineTestDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(AbortToken);
        var probe = new PipelineProbe { Name = "plain" };
        probe.Emit(new ProbeSaved(probe));
        db.Probes.Add(probe);

        // when
        await _SaveAsync(db, sync, AbortToken);
        await transaction.CommitAsync(AbortToken);

        // then
        evidence.HandlerCalls.Should().Be(1);
        evidence.HandlerCurrent.Should().BeNull("no unit of work is involved in a plain save");
        evidence.OutboxCalls.Should().Be(0);
        (await _CountProbesAsync(provider)).Should().Be(1);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task should_surface_the_fault_without_replay_when_a_participant_prevents_retry(bool sync, bool prevent)
    {
        // given — a retrying strategy that replays the marker exception once, and a handler that throws it on
        // its first call after (optionally) marking the unit non-replayable.
        await using var provider = await _BuildProviderAsync(configureOptions: static options =>
            options.ReplaceService<IExecutionStrategyFactory, RetryOnceStrategyFactory>()
        );
        var evidence = provider.GetRequiredService<PipelineEvidence>();
        evidence.OnHandled = (unitOfWork, _, _) =>
        {
            if (prevent)
            {
                unitOfWork!.PreventRetry();
            }

            return evidence.HandlerCalls == 1 ? throw new TransientProbeException() : Task.CompletedTask;
        };
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PipelineTestDbContext>();
        db.Probes.Add(_ProbeWithEvents("retry"));

        // when
        var act = async () => await _SaveAsync(db, sync, AbortToken);

        // then
        if (prevent)
        {
            await act.Should().ThrowAsync<TransientProbeException>("the strategy must not replay a prevented unit");
            evidence.HandlerCalls.Should().Be(1);
            evidence.Failed.Should().Equal(UnitOfWorkFailureReason.RolledBack);
            (await _CountProbesAsync(provider)).Should().Be(0);
        }
        else
        {
            await act.Should().NotThrowAsync();
            evidence.HandlerCalls.Should().Be(2, "the transient failure replayed the block with a fresh unit");
            evidence.Failed.Should().Equal(UnitOfWorkFailureReason.RolledBack);
            evidence.Completed.Should().Be(1);
            (await _CountProbesAsync(provider)).Should().Be(1);
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task should_adopt_the_unit_bound_to_a_factory_created_context_for_the_save(bool sync, bool complete)
    {
        // given — the request scope's manager begins the unit on a context that owns its own (factory) scope; the
        // handler resolved in the factory scope must still see that unit as Current.
        await using var provider = await _BuildProviderAsync();
        var evidence = provider.GetRequiredService<PipelineEvidence>();
        var factory = provider.GetRequiredService<IDbContextFactory<PipelineTestDbContext>>();
        await using var requestScope = provider.CreateAsyncScope();
        var requestManager = requestScope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        await using var db = await factory.CreateDbContextAsync(AbortToken);
        var factoryManager = (
            (IHeadlessDbContextScopeOwner)db
        ).OwnedScope!.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        await using var unitOfWork = await requestManager.BeginAsync(db, cancellationToken: AbortToken);
        db.Probes.Add(_ProbeWithEvents("factory"));

        // when
        await _SaveAsync(db, sync, AbortToken);

        // then — adopted for the save only; the request scope still owns the unit.
        evidence.HandlerCurrent.Should().BeSameAs(unitOfWork, "the factory scope's manager adopted the unit");
        evidence.OutboxCurrent.Should().BeSameAs(unitOfWork);
        factoryManager.Current.Should().BeNull("adoption is scoped to the save");
        requestManager.Current.Should().BeSameAs(unitOfWork);

        if (complete)
        {
            await unitOfWork.CompleteAsync(AbortToken);
            evidence.Completed.Should().Be(1);
            (await _CountProbesAsync(provider)).Should().Be(1);
        }
        else
        {
            await unitOfWork.RollbackAsync();
            evidence.Failed.Should().Equal(UnitOfWorkFailureReason.RolledBack);
            evidence.Completed.Should().Be(0);
            (await _CountProbesAsync(provider)).Should().Be(0);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task should_reenter_the_pipeline_when_a_handler_saves_the_same_context(bool sync, bool viaFactory)
    {
        // given — the handler adds a second row and saves the same context from inside the drain. The nested save
        // uses the same sync/async mode as the outer one: the handler shape (a Task-returning hook) allows either —
        // a sync inner SaveChanges is just a non-async lambda returning an already-completed Task<int>.
        await using var provider = await _BuildProviderAsync();
        var evidence = provider.GetRequiredService<PipelineEvidence>();
        evidence.OnHandled = async (_, db, ct) =>
        {
            if (evidence.HandlerCalls == 1)
            {
                db.Probes.Add(new PipelineProbe { Name = "nested" });
                await _SaveAsync(db, sync, ct);
            }
        };
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        await using var db = viaFactory
            ? await provider
                .GetRequiredService<IDbContextFactory<PipelineTestDbContext>>()
                .CreateDbContextAsync(AbortToken)
            : scope.ServiceProvider.GetRequiredService<PipelineTestDbContext>();
        var unitOfWork = viaFactory ? await manager.BeginAsync(db, cancellationToken: AbortToken) : null;
        db.Probes.Add(_ProbeWithEvents("outer"));

        // when
        await _SaveAsync(db, sync, AbortToken);

        if (unitOfWork is not null)
        {
            await unitOfWork.CompleteAsync(AbortToken);
        }

        // then — the nested save re-entered the pipeline on the same unit without error; both rows are durable.
        // (The re-entered save re-collects the outer entity's still-pending domain event — its events clear only
        // when the outer save completes — so the handler runs again; that is the pipeline's replay-safe contract,
        // which is why the hook guards on the first call.)
        evidence.HandlerCalls.Should().BeGreaterThanOrEqualTo(1);
        (await _CountProbesAsync(provider)).Should().Be(2);
    }

    #region Setup

    private async Task<ServiceProvider> _BuildProviderAsync(
        Action<IServiceCollection>? configureServices = null,
        Action<DbContextOptionsBuilder>? configureOptions = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(fixture.Clock);
        services.AddSingleton<ICurrentTenant>(fixture.CurrentTenant);
        services.AddSingleton<ICurrentUser>(fixture.CurrentUser);
        services.AddSingleton<IGuidGenerator>(new SequentialGuidGenerator(SequentialGuidType.Version7));
        services.AddSingleton<PipelineEvidence>();
        services.AddScoped<IHeadlessOutboxDispatcher, EvidenceOutboxDispatcher>();
        services.AddScoped<IDomainEventHandler<ProbeSaved>, ProbeSavedHandler>();
        services
            .AddHeadlessDbContext<PipelineTestDbContext>(options =>
            {
                options.UseNpgsql(fixture.SqlConnectionString);
                configureOptions?.Invoke(options);
            })
            .AddDomainEvents();
        configureServices?.Invoke(services);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // The fixture database is shared by the collection: recreate only this suite's table so a stale model
        // from a reused container never hides a failure.
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PipelineTestDbContext>();
        await db.Database.ExecuteSqlRawAsync("""DROP TABLE IF EXISTS "PipelineProbes";""", AbortToken);
        await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(AbortToken);

        return provider;
    }

    // Dispatches through either DbContext.SaveChanges() or SaveChangesAsync() so every scenario can prove the sync
    // and async pipeline twins behave identically without duplicating assertions.
    private static Task<int> _SaveAsync(DbContext db, bool sync, CancellationToken ct)
    {
        if (sync)
        {
#pragma warning disable MA0045 // Test intentionally exercises the synchronous SaveChanges path.
            return Task.FromResult(db.SaveChanges());
#pragma warning restore MA0045
        }

        return db.SaveChangesAsync(ct);
    }

    private static PipelineProbe _ProbeWithEvents(string name)
    {
        var probe = new PipelineProbe { Name = name };
        probe.Emit(new ProbeSaved(probe));
        probe.EmitIntegration(new ProbeShipped(name));

        return probe;
    }

    private static async Task<int> _CountProbesAsync(ServiceProvider provider)
    {
        // A fresh scope and context: only committed rows are visible.
        await using var scope = provider.CreateAsyncScope();

        return await scope
            .ServiceProvider.GetRequiredService<PipelineTestDbContext>()
            .Probes.AsNoTracking()
            .CountAsync(AbortToken);
    }

    #endregion

    #region Test model

    private sealed record ProbeSaved(PipelineProbe Probe);

    private sealed record ProbeShipped(string Name);

    private sealed class TransientProbeException() : Exception("Simulated transient failure.");

    private sealed class RetryOnceStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is TransientProbeException;
    }

    private sealed class RetryOnceStrategyFactory(ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new RetryOnceStrategy(dependencies);
    }

    /// <summary>Everything the participants observed, shared across scopes.</summary>
    private sealed class PipelineEvidence
    {
        public int HandlerCalls { get; set; }

        public int OutboxCalls { get; set; }

        public int Completed { get; set; }

        public List<UnitOfWorkFailureReason> Failed { get; } = [];

        public IUnitOfWork? HandlerCurrent { get; set; }

        public IUnitOfWork? OutboxCurrent { get; set; }

        public DbTransaction? HandlerTransaction { get; set; }

        public DbTransaction? ContextTransaction { get; set; }

        public Func<IUnitOfWork?, PipelineTestDbContext, CancellationToken, Task>? OnHandled { get; set; }

        public Func<Exception>? OutboxFault { get; set; }
    }

    /// <summary>
    /// Registers after-commit and failure callbacks on whatever unit is current in the handler's scope — the
    /// shape a real participant (outbox writer, job writer) has.
    /// </summary>
    private sealed class ProbeSavedHandler(
        IUnitOfWorkManager manager,
        PipelineTestDbContext db,
        PipelineEvidence evidence
    ) : IDomainEventHandler<ProbeSaved>
    {
        public async ValueTask HandleAsync(
            EventContext<ProbeSaved> context,
            CancellationToken cancellationToken = default
        )
        {
            evidence.HandlerCalls++;
            var current = manager.Current;
            evidence.HandlerCurrent = current;
            evidence.HandlerTransaction = (current?.Resource as IRelationalUnitOfWorkResource)?.Transaction;
            evidence.ContextTransaction = db.Database.CurrentTransaction?.GetDbTransaction();

            current?.OnCompleted(() =>
            {
                evidence.Completed++;
                return ValueTask.CompletedTask;
            });
            current?.OnFailed(failure =>
            {
                evidence.Failed.Add(failure.Reason);
                return ValueTask.CompletedTask;
            });

            if (evidence.OnHandled is { } hook)
            {
                await hook(current, db, cancellationToken);
            }
        }
    }

    private sealed class EvidenceOutboxDispatcher(IUnitOfWorkManager manager, PipelineEvidence evidence)
        : IHeadlessOutboxDispatcher
    {
        public Task DispatchAsync(
            IReadOnlyList<EventContext<object>> integrationEvents,
            CancellationToken cancellationToken = default
        )
        {
            evidence.OutboxCalls++;
            evidence.OutboxCurrent = manager.Current;

            return evidence.OutboxFault is { } fault ? Task.FromException(fault()) : Task.CompletedTask;
        }

        public void Dispatch(IReadOnlyList<EventContext<object>> integrationEvents)
        {
            DispatchAsync(integrationEvents, CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    public sealed class PipelineProbe : AggregateRoot, IEntity<Guid>
    {
        public Guid Id { get; private init; }

        public required string Name { get; init; }

        public void Emit(object domainEvent) => AddDomainEvent(domainEvent);

        public void EmitIntegration(object integrationEvent) => AddIntegrationEvent(integrationEvent);

        public override IReadOnlyList<object> GetKeys() => [Id];
    }

    public sealed class PipelineTestDbContext(
        HeadlessDbContextServices services,
        DbContextOptions<PipelineTestDbContext> options
    ) : HeadlessDbContext(services, options)
    {
        public DbSet<PipelineProbe> Probes => Set<PipelineProbe>();

        public override string DefaultSchema => "";

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<PipelineProbe>().ToTable("PipelineProbes");
        }
    }

    #endregion
}
