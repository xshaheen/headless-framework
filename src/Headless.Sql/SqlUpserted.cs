// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.Sql;

/// <summary>
/// The outcome of a <see cref="SqlUpsert" />: inserted or updated with what it wrote, or refused by the guard. What it
/// wrote is reached only through <see cref="Match{TResult}" />, which also demands a refusal branch.
/// </summary>
/// <typeparam name="TWritten">What an applied upsert reports it wrote.</typeparam>
[PublicAPI]
public sealed class SqlUpserted<TWritten>
{
    private readonly TWritten _written;

    private SqlUpserted(SqlUpsertOutcome outcome, TWritten written)
    {
        Outcome = outcome;
        _written = written;
    }

    /// <summary>Gets what the statement did.</summary>
    public SqlUpsertOutcome Outcome { get; }

    internal static SqlUpserted<TWritten> Applied(SqlUpsertOutcome outcome, TWritten written) => new(outcome, written);

    internal static SqlUpserted<TWritten> Refused() => new(SqlUpsertOutcome.Refused, default!);

    /// <summary>
    /// Returns <paramref name="applied" />'s result for an insert or an update, else <paramref name="refused" />'s.
    /// </summary>
    [MustUseReturnValue]
    public TResult Match<TResult>(Func<TWritten, SqlUpsertOutcome, TResult> applied, Func<TResult> refused)
    {
        return Outcome == SqlUpsertOutcome.Refused ? refused() : applied(_written, Outcome);
    }
}

/// <summary>Runs a rendered <see cref="SqlUpsert" /> and reads its decision row.</summary>
[PublicAPI]
public static class SqlUpsertCommand
{
    /// <summary>Runs the upsert and returns its outcome.</summary>
    /// <param name="command">The command, its text rendered and its parameters bound.</param>
    /// <param name="readWritten">Reads what the upsert wrote, from ordinal 1 (ordinal 0 is the outcome).</param>
    /// <param name="cancellationToken">Token used to cancel the command.</param>
    [MustUseReturnValue("An upsert with a guard can be refused; handle the refusal through Match.")]
    public static async Task<SqlUpserted<TWritten>> ExecuteAsync<TWritten>(
        DbCommand command,
        Func<DbDataReader, CancellationToken, ValueTask<TWritten>> readWritten,
        CancellationToken cancellationToken
    )
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        // The decision row is the first row of any result set: an engine whose upsert cannot report insert or update
        // from one statement (SQLite) runs one statement per branch, and only the branch that applied returns a row.
        while (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "An upsert returned no decision row; the dialect rendered it wrong."
                );
            }
        }

        var outcome = (SqlUpsertOutcome)reader.GetInt16(0);

        return outcome == SqlUpsertOutcome.Refused
            ? SqlUpserted<TWritten>.Refused()
            : SqlUpserted<TWritten>.Applied(
                outcome,
                await readWritten(reader, cancellationToken).ConfigureAwait(false)
            );
    }
}
