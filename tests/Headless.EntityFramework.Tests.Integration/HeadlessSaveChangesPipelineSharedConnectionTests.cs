// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.EntityFramework;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Two <see cref="HeadlessDbContext" /> types over one SQLite connection: a sibling context saved through the
/// Headless pipeline, with no explicit <c>UnitOfWork()</c> or <c>RunAsync</c> call, joins the unit the other
/// context carries; and a sibling that writes rows into the pipeline's own observed unit ends that save's replay,
/// because the replay does not re-run the handler that saved through the sibling.
/// </summary>
public sealed class HeadlessSaveChangesPipelineSharedConnectionTests : TestBase
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task should_join_the_owner_unit_when_a_sibling_saves_plainly_over_its_connection(
        bool sync,
        bool complete
    )
    {
        // given — the unit is begun on the owner; the sibling shares its connection and never touches the unit.
        var (provider, connection) = await _CreateProviderAsync();
        await using var _ = provider;
        await using var __ = connection;
        await using var scope = provider.CreateAsyncScope();
        var owner = scope.ServiceProvider.GetRequiredService<OwnerDbContext>();
        var sibling = scope.ServiceProvider.GetRequiredService<SiblingDbContext>();
        var factory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();

        await using (var unitOfWork = await factory.BeginAsync(owner, AbortToken))
        {
            owner.Owners.Add(new OwnerRow { Name = "owner" });
            await _SaveAsync(owner, sync, AbortToken);

            // when — a plain save: the pipeline itself must adopt the unit's transaction.
            sibling.Siblings.Add(new SiblingRow { Name = "sibling" });
            await _SaveAsync(sibling, sync, AbortToken);

            // then — the sibling adopted the unit's transaction. SQLite runs every statement on a connection inside
            // its open transaction whether or not the command carries it, so the row counts below cannot tell an
            // adopted save from an unadopted one; providers that bind commands to a transaction (SQL Server) can.
            // An owned unit stays replayable.
            sibling.Database.CurrentTransaction.Should().NotBeNull("the pipeline adopts the unit's transaction");
            sibling
                .Database.CurrentTransaction!.GetDbTransaction()
                .Should()
                .BeSameAs(((IRelationalUnitOfWorkResource)unitOfWork.Resource!).Transaction);
            unitOfWork.IsRetryPrevented.Should().BeFalse("the owner's block re-runs the sibling save on replay");

            if (complete)
            {
                await unitOfWork.CompleteAsync(AbortToken);
            }
            else
            {
                await unitOfWork.RollbackAsync();
            }
        }

        var expected = complete ? 1 : 0;
        (await _CountAsync<OwnerDbContext, OwnerRow>(provider)).Should().Be(expected);
        (await _CountAsync<SiblingDbContext, SiblingRow>(provider))
            .Should()
            .Be(expected, "the sibling's row commits or rolls back with the owner's unit");
    }

    [Theory]
    [InlineData(true, HandlerWrite.None)]
    [InlineData(true, HandlerWrite.SiblingSave)]
    [InlineData(true, HandlerWrite.RawSql)]
    [InlineData(false, HandlerWrite.None)]
    [InlineData(false, HandlerWrite.SiblingSave)]
    [InlineData(false, HandlerWrite.RawSql)]
    public async Task should_not_replay_the_owner_save_after_a_handler_returned(bool sync, HandlerWrite write)
    {
        // given — the owner's pipeline save enlists its own observed unit; the domain-event handler writes through
        // the shared connection (or not at all), then the owner's base save faults once with a retryable exception.
        var (provider, connection) = await _CreateProviderAsync(retryOnce: true);
        await using var _ = provider;
        await using var __ = connection;
        var evidence = provider.GetRequiredService<SharedConnectionEvidence>();
        evidence.Sync = sync;
        evidence.Write = write;
        await using var scope = provider.CreateAsyncScope();
        var owner = scope.ServiceProvider.GetRequiredService<OwnerDbContext>();
        var emitter = new EmitterRow { Name = "owner" };
        emitter.Emit(new OwnerSaved());
        owner.Emitters.Add(emitter);

        // when
        var act = async () => await _SaveAsync(owner, sync, AbortToken);

        // then — a replay would re-run only the owner's save, resuming after the handler, and commit the aggregate
        // without whatever the handler wrote outside the tracker; the pipeline cannot tell whether it wrote anything,
        // so the fault surfaces and nothing commits.
        await act.Should().ThrowAsync<TransientProbeException>("a replay would not re-run the returned handler");
        evidence.FaultsThrown.Should().Be(1);
        evidence.HandlerCalls.Should().Be(1);
        evidence.RetryPreventedAfterHandler.Should().BeTrue();
        (await _CountAsync<OwnerDbContext, EmitterRow>(provider)).Should().Be(0);
        (await _CountAsync<SiblingDbContext, SiblingRow>(provider)).Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_prevent_retry_only_when_a_sibling_writes_rows_into_an_observed_unit(bool sync)
    {
        // given — an observed unit enlisted over the owner's own transaction, as the pipeline and inbox runner do.
        var (provider, connection) = await _CreateProviderAsync();
        await using var _ = provider;
        await using var __ = connection;
        await using var scope = provider.CreateAsyncScope();
        var owner = scope.ServiceProvider.GetRequiredService<OwnerDbContext>();
        var sibling = scope.ServiceProvider.GetRequiredService<SiblingDbContext>();
        var factory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();
        await using var transaction = await owner.Database.BeginTransactionAsync(AbortToken);
        var unitOfWork = factory.Enlist(owner, transaction);

        // when/then — the unit's own context saving rows keeps it replayable: the owning code replays that tracker.
        owner.Owners.Add(new OwnerRow { Name = "owner" });
        await _SaveAsync(owner, sync, AbortToken);
        unitOfWork.IsRetryPrevented.Should().BeFalse();

        // A sibling save that writes nothing has nothing a replay could lose.
        await _SaveAsync(sibling, sync, AbortToken);
        unitOfWork.IsRetryPrevented.Should().BeFalse();

        // A sibling save that writes rows ends replay.
        sibling.Siblings.Add(new SiblingRow { Name = "sibling" });
        await _SaveAsync(sibling, sync, AbortToken);
        unitOfWork.IsRetryPrevented.Should().BeTrue();

        await transaction.RollbackAsync(AbortToken);
        await unitOfWork.RollbackAsync();
        await unitOfWork.DisposeAsync();
    }

    #region Setup

    private static async Task<(ServiceProvider Provider, SqliteConnection Connection)> _CreateProviderAsync(
        bool retryOnce = false
    )
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(AbortToken);

        var evidence = new SharedConnectionEvidence();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(evidence);
        services.AddScoped<IDomainEventHandler<OwnerSaved>, OwnerSavedHandler>();
        services
            .AddHeadlessDbContext<OwnerDbContext>(options =>
            {
                options.UseSqlite(connection).AddInterceptors(new ArmedFaultInterceptor(evidence));

                if (retryOnce)
                {
                    options.ReplaceService<IExecutionStrategyFactory, RetryOnceStrategyFactory>();
                }
            })
            .AddDomainEvents();
        services.AddHeadlessDbContext<SiblingDbContext>(options => options.UseSqlite(connection));

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using var scope = provider.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<OwnerDbContext>()
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(AbortToken);
        await scope
            .ServiceProvider.GetRequiredService<SiblingDbContext>()
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(AbortToken);

        return (provider, connection);
    }

    // Dispatches through either DbContext.SaveChanges() or SaveChangesAsync() so every scenario proves both
    // pipeline twins.
    private static Task<int> _SaveAsync(DbContext db, bool sync, CancellationToken ct)
    {
        if (sync)
        {
            return Task.FromResult(db.SaveChanges());
        }

        return db.SaveChangesAsync(ct);
    }

    private static async Task<int> _CountAsync<TContext, TRow>(ServiceProvider provider)
        where TContext : DbContext
        where TRow : class
    {
        // A fresh scope and context: only committed rows are visible once the unit has ended.
        await using var scope = provider.CreateAsyncScope();

        return await scope
            .ServiceProvider.GetRequiredService<TContext>()
            .Set<TRow>()
            .AsNoTracking()
            .CountAsync(AbortToken);
    }

    #endregion

    #region Test model

    private sealed record OwnerSaved;

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

    private sealed class SharedConnectionEvidence
    {
        public bool Sync { get; set; }

        public HandlerWrite Write { get; set; }

        public int HandlerCalls { get; set; }

        public bool? RetryPreventedAfterHandler { get; set; }

        // Armed by the handler so the fault hits the owner's base save after the drain, not a handler's save.
        public bool FaultArmed { get; set; }

        public int FaultsThrown { get; set; }

        public IUnitOfWork? Unit { get; set; }
    }

    /// <summary>Faults the owner's next base save once, after the domain-event drain has completed.</summary>
    private sealed class ArmedFaultInterceptor(SharedConnectionEvidence evidence) : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result
        )
        {
            _ThrowIfArmed();

            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            _ThrowIfArmed();

            return ValueTask.FromResult(result);
        }

        private void _ThrowIfArmed()
        {
            if (!evidence.FaultArmed)
            {
                return;
            }

            evidence.FaultArmed = false;
            evidence.FaultsThrown++;
            evidence.RetryPreventedAfterHandler = evidence.Unit?.IsRetryPrevented;

            throw new TransientProbeException();
        }
    }

    private sealed class OwnerSavedHandler(SiblingDbContext sibling, SharedConnectionEvidence evidence)
        : IDomainEventHandler<OwnerSaved>
    {
        public async ValueTask HandleAsync(
            EventContext<OwnerSaved> context,
            CancellationToken cancellationToken = default
        )
        {
            evidence.HandlerCalls++;

            switch (evidence.Write)
            {
                case HandlerWrite.SiblingSave:
                    sibling.Siblings.Add(new SiblingRow { Name = "from-handler" });
                    await _SaveAsync(sibling, evidence.Sync, cancellationToken);

                    break;
                case HandlerWrite.RawSql:
                    // A command on the shared connection, outside any change tracker: nothing restores it on replay.
                    await sibling.Database.ExecuteSqlAsync(
                        $"INSERT INTO \"Siblings\" (\"Id\", \"Name\") VALUES ({Guid.NewGuid()}, 'raw')",
                        cancellationToken
                    );

                    break;
            }

            // Read after the handler returns, as the owner's pipeline sees the unit when its base save faults.
            evidence.FaultArmed = true;
            evidence.Unit = sibling.UnitOfWork();
        }
    }

    public enum HandlerWrite
    {
        None,
        SiblingSave,
        RawSql,
    }

    // A plain entity: an aggregate root's save dispatches its lifecycle events, and a caller-owned save that
    // dispatched anything ends replay on its own, which would mask the rule these tests isolate.
    public sealed class OwnerRow : IEntity<Guid>
    {
        public Guid Id { get; private init; }

        public required string Name { get; init; }

        public IReadOnlyList<object> GetKeys() => [Id];
    }

    public sealed class EmitterRow : AggregateRoot, IEntity<Guid>
    {
        public Guid Id { get; private init; }

        public required string Name { get; init; }

        public void Emit(object domainEvent) => AddDomainEvent(domainEvent);

        public override IReadOnlyList<object> GetKeys() => [Id];
    }

    public sealed class SiblingRow : IEntity<Guid>
    {
        public Guid Id { get; private init; }

        public required string Name { get; init; }

        public IReadOnlyList<object> GetKeys() => [Id];
    }

    public sealed class OwnerDbContext(DbContextOptions<OwnerDbContext> options) : HeadlessDbContext(options)
    {
        public DbSet<OwnerRow> Owners => Set<OwnerRow>();

        public DbSet<EmitterRow> Emitters => Set<EmitterRow>();

        public override string DefaultSchema => "";
    }

    public sealed class SiblingDbContext(DbContextOptions<SiblingDbContext> options) : HeadlessDbContext(options)
    {
        public DbSet<SiblingRow> Siblings => Set<SiblingRow>();

        public override string DefaultSchema => "";
    }

    #endregion
}
