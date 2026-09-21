// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.UnitOfWork.Internal;

/// <summary>
/// The begin and enlist choreography every raw-ADO provider shares: refuse a connection that already carries a
/// live unit, open the unit through the provider's own resource, then record it in the connection binding so a
/// later <c>RunAsync(connection, …)</c> joins it. The providers supply only how their transaction begins.
/// </summary>
internal static class BoundConnectionUnitOfWork
{
    /// <summary>
    /// Begins an owned unit on <paramref name="connection" />. The refusal runs inside the factory's begin so it
    /// precedes any connection or transaction effect, and the binding is recorded once the unit exists.
    /// </summary>
    public static async ValueTask<IUnitOfWork> BeginAsync(
        IUnitOfWorkFactory factory,
        DbConnection connection,
        Func<CancellationToken, ValueTask<IUnitOfWorkResource>> beginOwned,
        CancellationToken cancellationToken
    )
    {
        var unit = await factory
            .BeginAsync(
                ct =>
                {
                    DbConnectionUnitOfWorkBinding.ThrowIfBound(connection);

                    return beginOwned(ct);
                },
                options: null,
                cancellationToken
            )
            .ConfigureAwait(false);

        DbConnectionUnitOfWorkBinding.Bind(connection, unit);

        return unit;
    }

    /// <summary>Enlists an observed <paramref name="resource" /> on <paramref name="connection" /> and records the binding.</summary>
    public static IUnitOfWork Enlist(IUnitOfWorkFactory factory, DbConnection connection, IUnitOfWorkResource resource)
    {
        DbConnectionUnitOfWorkBinding.ThrowIfBound(connection);

        var unit = factory.Enlist(resource);
        DbConnectionUnitOfWorkBinding.Bind(connection, unit);

        return unit;
    }
}
