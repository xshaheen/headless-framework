// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
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
/// A pipeline-owned save commits its observed unit's transaction itself, so a commit fault reaches the caller and the
/// unit's participants classified as an owned unit's <c>CompleteAsync</c> would classify it: in-doubt when the commit
/// may have reached the database, faulted when it certainly did not.
/// </summary>
public sealed class HeadlessSaveChangesPipelineCommitFaultTests : TestBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_surface_an_in_doubt_commit_as_unit_of_work_in_doubt(bool sync)
    {
        // given — the commit loses its answer to a transport timeout
        var (provider, connection) = await _CreateProviderAsync(() => new TimeoutException("The commit timed out."));
        await using var _ = provider;
        await using var __ = connection;
        var evidence = provider.GetRequiredService<CommitFaultEvidence>();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommitFaultDbContext>();
        db.Rows.Add(_RowWithEvent());

        // when
        var act = async () => await _SaveAsync(db, sync, AbortToken);

        // then — the caller and the unit's participants both learn that the outcome is unknown
        var thrown = await act.Should().ThrowAsync<UnitOfWorkInDoubtException>();
        thrown.Which.InnerException.Should().BeOfType<TimeoutException>();
        evidence.FailureReasons.Should().Equal(UnitOfWorkFailureReason.InDoubt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_surface_a_definite_commit_failure_as_its_own_exception(bool sync)
    {
        // given — the commit is refused before anything reaches the database
        var (provider, connection) = await _CreateProviderAsync(() => new InvalidOperationException("Refused."));
        await using var _ = provider;
        await using var __ = connection;
        var evidence = provider.GetRequiredService<CommitFaultEvidence>();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommitFaultDbContext>();
        db.Rows.Add(_RowWithEvent());

        // when
        var act = async () => await _SaveAsync(db, sync, AbortToken);

        // then
        await act.Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage("Refused.");
        evidence.FailureReasons.Should().Equal(UnitOfWorkFailureReason.Faulted);
    }

    #region Setup

    private static async Task<(ServiceProvider Provider, SqliteConnection Connection)> _CreateProviderAsync(
        Func<Exception> commitFault
    )
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(AbortToken);

        var evidence = new CommitFaultEvidence();
        var interceptor = new CommitFaultInterceptor(commitFault);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(evidence);
        services.AddScoped<IDomainEventHandler<RowSaved>, RowSavedHandler>();
        services
            .AddHeadlessDbContext<CommitFaultDbContext>(options =>
                options.UseSqlite(connection).AddInterceptors(interceptor)
            )
            .AddDomainEvents();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using var scope = provider.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<CommitFaultDbContext>()
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(AbortToken);
        interceptor.Armed = true;

        return (provider, connection);
    }

    private static Task<int> _SaveAsync(DbContext db, bool sync, CancellationToken cancellationToken)
    {
        return sync ? Task.FromResult(db.SaveChanges()) : db.SaveChangesAsync(cancellationToken);
    }

    private static CommitFaultRow _RowWithEvent()
    {
        var row = new CommitFaultRow();
        row.Emit(new RowSaved());

        return row;
    }

    #endregion

    #region Test model

    private sealed record RowSaved;

    private sealed class CommitFaultEvidence
    {
        public List<UnitOfWorkFailureReason> FailureReasons { get; } = [];
    }

    /// <summary>Faults every commit once armed, before it reaches the database.</summary>
    private sealed class CommitFaultInterceptor(Func<Exception> commitFault) : DbTransactionInterceptor
    {
        public bool Armed { get; set; }

        public override InterceptionResult TransactionCommitting(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result
        )
        {
            return Armed ? throw commitFault() : result;
        }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            return Armed ? throw commitFault() : ValueTask.FromResult(result);
        }
    }

    private sealed class RowSavedHandler(CommitFaultDbContext db, CommitFaultEvidence evidence)
        : IDomainEventHandler<RowSaved>
    {
        public ValueTask HandleAsync(EventContext<RowSaved> context, CancellationToken cancellationToken = default)
        {
            db.UnitOfWork()
                ?.OnFailed(failure =>
                {
                    evidence.FailureReasons.Add(failure.Reason);

                    return ValueTask.CompletedTask;
                });

            return ValueTask.CompletedTask;
        }
    }

    public sealed class CommitFaultRow : AggregateRoot, IEntity<Guid>
    {
        public Guid Id { get; private init; }

        public void Emit(object domainEvent) => AddDomainEvent(domainEvent);

        public override IReadOnlyList<object> GetKeys() => [Id];
    }

    public sealed class CommitFaultDbContext(DbContextOptions<CommitFaultDbContext> options)
        : HeadlessDbContext(options)
    {
        public DbSet<CommitFaultRow> Rows => Set<CommitFaultRow>();

        public override string DefaultSchema => "";
    }

    #endregion
}
