// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data.Common;
using System.Reflection;

namespace Headless.UnitOfWork.Internal;

/// <summary>
/// The SQLite half of <see cref="RelationalTransientFaults" />. <c>Microsoft.Data.Sqlite</c> reports a busy database
/// as <c>SqliteException</c> with <see cref="DbException.IsTransient" /> false, so the result code is read from its
/// <c>SqliteErrorCode</c> property, by reflection because this package references no driver.
/// </summary>
/// <remarks>
/// <c>SQLITE_BUSY</c> (5) means another connection held the database write lock for longer than the driver's busy
/// wait, or wrote after this transaction's read snapshot (<c>SQLITE_BUSY_SNAPSHOT</c>, which carries the same primary
/// code); <c>SQLITE_LOCKED</c> (6) is the same conflict inside one shared cache. A fresh transaction can clear all of
/// them, as a lock timeout on another engine.
/// </remarks>
internal static class SqliteTransientFaults
{
    private const string _SqliteExceptionTypeName = "Microsoft.Data.Sqlite.SqliteException";
    private const int _Busy = 5;
    private const int _Locked = 6;

    private static readonly ConcurrentDictionary<Type, PropertyInfo?> _ErrorCodeProperties = new();

    public static bool IsTransient(DbException exception)
    {
        var type = exception.GetType();

        if (!string.Equals(type.FullName, _SqliteExceptionTypeName, StringComparison.Ordinal))
        {
            return false;
        }

        var property = _ErrorCodeProperties.GetOrAdd(
            type,
            static t => t.GetProperty("SqliteErrorCode", BindingFlags.Public | BindingFlags.Instance)
        );

        return property?.GetValue(exception) is _Busy or _Locked;
    }
}
