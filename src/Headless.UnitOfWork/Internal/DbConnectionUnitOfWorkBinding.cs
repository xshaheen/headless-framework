// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.UnitOfWork.Internal;

/// <summary>
/// The <see cref="DbConnection" /> → <see cref="IUnitOfWork" /> binding shared by every provider that begins
/// on, enlists, or wraps a connection — the raw-ADO providers directly, and the EF provider for the connection
/// underneath its context, so a block run on that connection joins the context's unit.
/// </summary>
internal static class DbConnectionUnitOfWorkBinding
{
    public const string AlreadyBoundMessage =
        "This connection already carries an active unit of work. Run the block with RunAsync(connection, …) to join it, or pass that unit to the code that needs it (read it with connection.UnitOfWork()), instead of beginning a second one on the same connection.";

    private static readonly UnitOfWorkBinding<DbConnection> _Binding = new();

    public static void Bind(DbConnection connection, IUnitOfWork unit) => _Binding.Bind(connection, unit);

    public static bool TryGet(DbConnection connection, out IUnitOfWork unit) => _Binding.TryGet(connection, out unit);

    /// <summary>Throws the catalogued refusal when <paramref name="connection" /> already carries a live unit.</summary>
    public static void ThrowIfBound(DbConnection connection)
    {
        if (TryGet(connection, out _))
        {
            throw new InvalidOperationException(AlreadyBoundMessage);
        }
    }
}
