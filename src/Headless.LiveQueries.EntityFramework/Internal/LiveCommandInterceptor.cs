// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// Sorts every command a tracked context sends: a query's tables are recorded on the live query that runs it, and a
/// write's tables are handed to the tracker once the command has run.
/// </summary>
/// <remarks>
/// With row matching on, a query whose SQL carries the mark of a shape is recorded with that shape's row filter,
/// bound to the values this execution sends.
/// </remarks>
internal sealed class LiveCommandInterceptor(LiveTableCatalog catalog, LiveWriteTracker tracker, bool matchRows)
    : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result
    )
    {
        _Executing(command, eventData);

        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        _Executing(command, eventData);

        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result
    )
    {
        _Executing(command, eventData);

        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        _Executing(command, eventData);

        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result
    )
    {
        _Executing(command, eventData);

        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default
    )
    {
        _Executing(command, eventData);

        return ValueTask.FromResult(result);
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result
    )
    {
        _Executed(command, eventData);

        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default
    )
    {
        _Executed(command, eventData);

        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        _Executed(command, eventData);

        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        _Executed(command, eventData);

        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        _Executed(command, eventData);

        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default
    )
    {
        _Executed(command, eventData);

        return ValueTask.FromResult(result);
    }

    private void _Executing(DbCommand command, CommandEventData eventData)
    {
        if (eventData.Context is not { } context)
        {
            return;
        }

        if (matchRows && eventData.CommandSource is CommandSource.SaveChanges)
        {
            tracker.SaveCommandStarting(context);
        }

        // Nothing else to do before a command runs unless a live query sends it: a write is recorded after it ran.
        if (LiveQueryRecorder.Current is not { } recorder)
        {
            return;
        }

        switch (_Classify(eventData.CommandSource))
        {
            case CommandKind.Read:
                // Not contained: a query whose tables are unknown would stay stale without any sign, so the run
                // fails instead.
                var read = catalog.Scan(context, command.CommandText);

                if (_BindFilter(context, command, read) is { } filter)
                {
                    recorder.AddFiltered(read[0], filter.Matches);
                }
                else
                {
                    recorder.Add(read);
                }

                break;
            case CommandKind.Write:
                var keys = _ScanContained(context, command);

                // Raw SQL that names no mapped table is not known to write one: a session setting, for example.
                if (keys.Length > 0 || eventData.CommandSource is not CommandSource.ExecuteSqlRaw)
                {
                    throw LiveReadOnlyRule.Violated(_Name(eventData.CommandSource), keys);
                }

                break;
        }
    }

    private void _Executed(DbCommand command, CommandExecutedEventData eventData)
    {
        if (eventData.Context is not { } context)
        {
            return;
        }

        var kind = _Classify(eventData.CommandSource);

        if (kind is not (CommandKind.Write or CommandKind.Save))
        {
            return;
        }

        var keys = _ScanContained(context, command, written: true);

        if (kind is CommandKind.Save)
        {
            // The save interceptor reads the change tracker; the command text also names tables an interceptor
            // added after that reading.
            tracker.SaveCommandSucceeded(context, keys);
        }
        else
        {
            tracker.Written(context, keys);
        }
    }

    // The shape was taken from the query before EF Core expanded it. The SQL is what runs, so the filter is used
    // only when the SQL names the shape's table and no other: whatever EF Core added that reads a second table
    // shows here.
    private LiveRowFilter? _BindFilter(DbContext context, DbCommand command, string[] read)
    {
        return
            matchRows
            && LiveQueryShapes.Find(context.Model, command.CommandText) is { } shape
            && read is [var key]
            && string.Equals(key, shape.Key, StringComparison.Ordinal)
            ? shape.Bind(command, context.GetService<ISqlGenerationHelper>())
            : null;
    }

    // For a command that ran, the tables it can change: a delete also changes the tables whose rows the database
    // removes or clears with the deleted ones.
    private string[] _ScanContained(DbContext context, DbCommand command, bool written = false)
    {
        try
        {
            return written
                ? catalog.ScanWrite(context, command.CommandText)
                : catalog.Scan(context, command.CommandText);
        }
#pragma warning disable CA1031 // The fix would make it worse: tracking must not fail the application's write.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            tracker.Failed(exception);

            return [];
        }
    }

    private static CommandKind _Classify(CommandSource source)
    {
        return source switch
        {
            CommandSource.Migrations or CommandSource.Scaffolding or CommandSource.ValueGenerator => CommandKind.None,
            CommandSource.SaveChanges => CommandKind.Save,
            CommandSource.ExecuteUpdate or CommandSource.ExecuteDelete or CommandSource.ExecuteSqlRaw =>
                CommandKind.Write,
            _ => CommandKind.Read,
        };
    }

    private static string _Name(CommandSource source)
    {
        return source switch
        {
            // EF Core reports a set-based delete under the same source as a set-based update.
            CommandSource.ExecuteUpdate => "ExecuteUpdate or ExecuteDelete",
            CommandSource.ExecuteDelete => "ExecuteDelete",
            _ => "ExecuteSql",
        };
    }

    private enum CommandKind
    {
        None,
        Read,
        Write,
        Save,
    }
}
