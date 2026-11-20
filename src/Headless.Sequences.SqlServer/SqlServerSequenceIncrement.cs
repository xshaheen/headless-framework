// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sequences.SqlServer;

/// <summary>The fast-path increment as one self-committing SQL Server batch.</summary>
internal static class SqlServerSequenceIncrement
{
    /// <summary>
    /// Wraps the rendered upsert (an existence check, then an update or an insert) in a transaction the batch opens and
    /// commits itself, so the whole increment is one round trip and its statements still agree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>XACT_ABORT ON</c> makes any error, and a client timeout or cancellation, roll the batch's transaction back
    /// instead of leaving it open on a pooled connection with the counter row still locked. READ COMMITTED matches the
    /// explicit transaction this replaces; a stricter server default would turn racing first calls on a new key into
    /// serialization failures.
    /// </para>
    /// <para>
    /// The decision row is read before <c>COMMIT</c> runs, so the store reads the batch's remaining results before it
    /// returns the value, which raises any error the commit hit. A batch that did not commit therefore never returns
    /// its value.
    /// Both settings are session state, and the pool resets them before the connection is handed out again. The
    /// gap-free path never uses this form: it runs inside the caller's transaction and must not change the caller's
    /// session settings.
    /// </para>
    /// </remarks>
    public static string Autocommit(string upsert)
    {
        return $"""
            SET XACT_ABORT ON;
            SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
            BEGIN TRANSACTION;
            {upsert}
            COMMIT TRANSACTION;
            """;
    }
}
