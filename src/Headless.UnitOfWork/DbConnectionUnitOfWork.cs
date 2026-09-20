// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.UnitOfWork.Internal;

namespace Headless.UnitOfWork;

/// <summary>
/// The <see cref="DbConnection" /> → <see cref="IUnitOfWork" /> binding that the raw-ADO <c>BeginAsync</c>,
/// <c>Enlist</c>, and <c>RunAsync</c> record, and that the EF provider records for the connection beneath its
/// context. With no ambient unit of work, the connection is how code that was handed only the connection — a
/// Dapper repository, a raw-SQL helper — reaches the unit that owns its transaction.
/// </summary>
[PublicAPI]
public static class DbConnectionUnitOfWork
{
    extension(DbConnection connection)
    {
        /// <summary>
        /// Gets the unit of work bound to this connection while it is still <see cref="UnitOfWorkState.Active" />,
        /// or <see langword="null" /> when none is: no unit was begun on the connection, or the bound unit reached
        /// a terminal state and was evicted.
        /// </summary>
        /// <remarks>
        /// <c>RunAsync(connection, …)</c> reads this first and joins the bound unit; a callee that wants the
        /// handle itself reads it here rather than beginning a second unit on the same connection.
        /// </remarks>
        /// <returns>The active bound unit, or <see langword="null" />.</returns>
        /// <exception cref="ArgumentNullException">The connection is <see langword="null" />.</exception>
        public IUnitOfWork? UnitOfWork()
        {
            Argument.IsNotNull(connection);

            return DbConnectionUnitOfWorkBinding.TryGet(connection, out var unit) ? unit : null;
        }
    }
}
