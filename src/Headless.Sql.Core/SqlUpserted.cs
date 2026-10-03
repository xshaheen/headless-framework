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
