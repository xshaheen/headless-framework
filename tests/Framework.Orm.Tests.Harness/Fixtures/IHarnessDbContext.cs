// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Framework.Orm.EntityFramework.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Tests.Entities;

namespace Tests.Fixtures;

/// <summary>
/// Interface for test DbContext implementations that provide message capture and test entity access.
/// </summary>
public interface IHarnessDbContext : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// DbSet for full audit test entities.
    /// </summary>
    DbSet<HarnessTestEntity> TestEntities { get; }

    /// <summary>
    /// DbSet for basic test entities without audit.
    /// </summary>
    DbSet<HarnessBasicEntity> BasicEntities { get; }

    /// <summary>
    /// Captured distributed messages emitted during SaveChanges for test verification.
    /// </summary>
    List<EmitterDistributedMessages> EmittedDistributedMessages { get; }

    /// <summary>
    /// Captured local messages emitted during SaveChanges for test verification.
    /// </summary>
    List<EmitterLocalMessages> EmittedLocalMessages { get; }

    /// <summary>
    /// Saves all changes made in this context to the database.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the given operation in a transaction with the specified isolation level.
    /// </summary>
    Task ExecuteTransactionAsync(
        Func<Task<bool>> operation,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Provides access to database-related information and operations for this context.
    /// </summary>
    DatabaseFacade Database { get; }
}
