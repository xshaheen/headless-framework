// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// <c>RunAsync(db, …)</c> under the replay conformance suite, on whichever relational provider
/// <paramref name="useProvider" /> configures. The context's execution strategy replays
/// <see cref="ReplayableFaultException" /> once with no delay, and an interceptor raises the same fault before the
/// commit reaches the database when a scenario arms it. The probe table is the provider fixture's
/// <c>probe_rows</c>, so counting and resetting go through <paramref name="probes" />, and
/// <paramref name="breakConnection" /> ends the context's database session the provider's own way.
/// </summary>
public sealed class EntityFrameworkReplayFixture(
    IUnitOfWorkRunFixture probes,
    Action<DbContextOptionsBuilder> useProvider,
    Func<DbConnection, CancellationToken, Task> breakConnection
) : IUnitOfWorkReplayFixture
{
    public bool ReplaysBeforeCommit => true;

    public async Task<TResult> RunAsync<TResult>(
        Func<IUnitOfWorkReplayContext, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEntityFrameworkUnitOfWork();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();

        var commitFault = new CommitFaultInterceptor();
        var builder = new DbContextOptionsBuilder<ReplayProbeDbContext>();
        useProvider(builder);
        builder
            .ReplaceService<IExecutionStrategyFactory, ReplayableFaultExecutionStrategyFactory>()
            .AddInterceptors(commitFault);
        await using var db = new ReplayProbeDbContext(builder.Options);

        return await factory.RunAsync(
            db,
            (unitOfWork, ct) => operation(new Context(factory, db, unitOfWork, commitFault, breakConnection), ct),
            cancellationToken: cancellationToken
        );
    }

    public Task<int> CountProbeRowsAsync(CancellationToken cancellationToken)
    {
        return probes.CountProbeRowsAsync(cancellationToken);
    }

    public Task ResetAsync(CancellationToken cancellationToken)
    {
        return probes.ResetAsync(cancellationToken);
    }

    private sealed class Context(
        IUnitOfWorkFactory factory,
        ReplayProbeDbContext db,
        IUnitOfWork unitOfWork,
        CommitFaultInterceptor commitFault,
        Func<DbConnection, CancellationToken, Task> breakConnection
    ) : IUnitOfWorkReplayContext
    {
        public IUnitOfWork UnitOfWork => unitOfWork;

        public async Task InsertProbeRowAsync(string name, CancellationToken cancellationToken)
        {
            db.Probes.Add(new ReplayProbeRow { Name = name });
            await db.SaveChangesAsync(cancellationToken);
        }

        public Task ArmCommitFaultAsync(CancellationToken cancellationToken)
        {
            commitFault.Arm();

            return Task.CompletedTask;
        }

        public Task BreakConnectionAsync(CancellationToken cancellationToken)
        {
            return breakConnection(db.Database.GetDbConnection(), cancellationToken);
        }

        public Task RunJoinedAsync(
            Func<IUnitOfWork, CancellationToken, Task> operation,
            CancellationToken cancellationToken
        )
        {
            return factory.RunAsync(db, operation, cancellationToken);
        }
    }

    /// <summary>Raises <see cref="ReplayableFaultException" /> once, before the armed commit is sent.</summary>
    private sealed class CommitFaultInterceptor : DbTransactionInterceptor
    {
        private int _armed;

        public void Arm()
        {
            Volatile.Write(ref _armed, 1);
        }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1)
            {
                throw new ReplayableFaultException("Simulated commit fault.");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class ReplayableFaultExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception)
        {
            return exception is ReplayableFaultException;
        }
    }

    private sealed class ReplayableFaultExecutionStrategyFactory(ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create()
        {
            return new ReplayableFaultExecutionStrategy(dependencies);
        }
    }
}

/// <summary>Maps the provider fixture's <c>probe_rows</c> table in the connection's default schema.</summary>
public sealed class ReplayProbeDbContext(DbContextOptions<ReplayProbeDbContext> options) : DbContext(options)
{
    public DbSet<ReplayProbeRow> Probes => Set<ReplayProbeRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReplayProbeRow>(entity =>
        {
            entity.ToTable("probe_rows");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id");
            entity.Property(row => row.Name).HasColumnName("name");
        });
    }
}

public sealed class ReplayProbeRow
{
    public int Id { get; init; }

    public string? Name { get; init; }
}
