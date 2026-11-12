// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tests;

/// <summary>
/// Records the text of every reader command EF runs on a host built with it, so a test can assert how many queries a
/// dashboard read makes and what they hold.
/// </summary>
internal sealed class DashboardSqlCapture : DbCommandInterceptor
{
    private readonly ConcurrentQueue<string> _statements = new();

    public string[] Statements => [.. _statements];

    public void Clear()
    {
        while (_statements.TryDequeue(out _)) { }
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result
    )
    {
        _statements.Enqueue(command.CommandText);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        _statements.Enqueue(command.CommandText);
        return ValueTask.FromResult(result);
    }
}
