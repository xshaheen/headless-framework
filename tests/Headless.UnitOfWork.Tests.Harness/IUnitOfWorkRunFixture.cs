// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Tests;

/// <summary>
/// Provider fixture for the <c>RunAsync</c> conformance scenarios. Each leaf wraps its provider's
/// <c>IUnitOfWorkManager.RunAsync(resource, …)</c> helper and a durable probe table created at fixture
/// initialization — never inside the unit's transaction (transactional DDL would vanish on rollback, and SQL
/// Server temp tables are invisible to the verifying connection).
/// </summary>
public interface IUnitOfWorkRunFixture
{
    /// <summary>
    /// Runs <paramref name="operation" /> through the provider's <c>RunAsync</c> helper on a fresh service scope.
    /// The helper owns begin / operation / complete; operation exceptions propagate to the caller.
    /// </summary>
    Task RunAsync(Func<IUnitOfWorkRunContext, CancellationToken, Task> operation, CancellationToken cancellationToken);

    /// <summary>Counts probe rows using a connection independent of the unit's transaction.</summary>
    Task<int> CountProbeRowsAsync(CancellationToken cancellationToken);

    /// <summary>Creates the probe table when absent and deletes all probe rows between scenarios.</summary>
    Task ResetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Provider-agnostic surface handed to a conformance operation while it runs inside the helper's unit of work.
/// </summary>
public interface IUnitOfWorkRunContext
{
    /// <summary>The unit the helper began; the operation registers its work on it.</summary>
    IUnitOfWork UnitOfWork { get; }

    /// <summary>Inserts one probe row inside the unit's transaction.</summary>
    Task InsertProbeRowAsync(string name, CancellationToken cancellationToken);
}
