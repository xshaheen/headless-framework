// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.Jobs.Infrastructure;

/// <summary>
/// Owns the provider-neutral EF transaction lifecycle for native claim strategies. Provider packages retain
/// responsibility for SQL generation and command execution; this scope guarantees consistent commit and disposal
/// semantics around those operations.
/// </summary>
internal sealed class JobsClaimTransaction<TDbContext> : IAsyncDisposable
    where TDbContext : DbContext
{
    private JobsClaimTransaction(TDbContext dbContext, IDbContextTransaction transaction)
    {
        DbContext = dbContext;
        Transaction = transaction;
    }

    public TDbContext DbContext { get; }

    public IDbContextTransaction Transaction { get; }

    public static async Task<JobsClaimTransaction<TDbContext>> CreateAsync(
        IDbContextFactory<TDbContext> dbContextFactory,
        CancellationToken cancellationToken
    )
    {
        var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            return new JobsClaimTransaction<TDbContext>(dbContext, transaction);
        }
        catch
        {
            await dbContext.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        // Once commit starts, cancellation cannot safely distinguish a rollback from a server-side commit.
        cancellationToken.ThrowIfCancellationRequested();
        return Transaction.CommitAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Transaction.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await DbContext.DisposeAsync().ConfigureAwait(false);
        }
    }
}
