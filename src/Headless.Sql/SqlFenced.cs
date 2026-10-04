// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.Sql;

/// <summary>
/// The outcome of a fenced state transition: accepted with what it wrote, or rejected with the row as the fence found
/// it. There is no property that yields the accepted value: it is reached only through <see cref="Match{TResult}" />,
/// which also demands a rejection branch, so a caller cannot use a transition's result without deciding what a refusal
/// means.
/// </summary>
/// <typeparam name="TRow">The row the transition's locking read found; <see langword="null" /> when there was none.</typeparam>
/// <typeparam name="TAccepted">What an accepted transition reports it wrote.</typeparam>
[PublicAPI]
public sealed class SqlFenced<TRow, TAccepted>
    where TRow : class
{
    private readonly TAccepted _accepted;

    private SqlFenced(bool isAccepted, TRow? before, TAccepted accepted)
    {
        IsAccepted = isAccepted;
        Before = before;
        _accepted = accepted;
    }

    /// <summary>Gets whether the transition applied.</summary>
    public bool IsAccepted { get; }

    /// <summary>Gets the row as the transition's locking read found it, before any write.</summary>
    public TRow? Before { get; }

    internal static SqlFenced<TRow, TAccepted> Accepted(TRow? before, TAccepted accepted) =>
        new(isAccepted: true, before, accepted);

    internal static SqlFenced<TRow, TAccepted> Rejected(TRow? before) =>
        new(isAccepted: false, before, accepted: default!);

    /// <summary>Returns <paramref name="accepted" />'s result for an applied transition, else <paramref name="rejected" />'s.</summary>
    [MustUseReturnValue]
    public TResult Match<TResult>(Func<TAccepted, TRow?, TResult> accepted, Func<TRow?, TResult> rejected)
    {
        return IsAccepted ? accepted(_accepted, Before) : rejected(Before);
    }
}

/// <summary>Runs rendered fenced statements and reads them back in the shape the dialects agree on.</summary>
[PublicAPI]
public static class SqlFencedCommand
{
    /// <summary>
    /// Runs a batch of <see cref="SqlLockedRead" /> followed by <see cref="SqlFencedTransition" /> (or, with
    /// <paramref name="lockedRead" /> off, a lone <see cref="SqlInsertIfAbsent" />) and returns its outcome.
    /// </summary>
    /// <param name="command">The command, its text rendered and its parameters bound.</param>
    /// <param name="lockedRead">Whether the batch starts with a locking read whose row classifies a rejection.</param>
    /// <param name="readRow">Reads the locked row from ordinal 0.</param>
    /// <param name="readAccepted">Reads what the transition wrote, from ordinal 1 (ordinal 0 is the applied flag).</param>
    /// <param name="cancellationToken">Token used to cancel the command.</param>
    [MustUseReturnValue("A fenced transition can be refused; handle the rejection through Match.")]
    public static async Task<SqlFenced<TRow, TAccepted>> ExecuteAsync<TRow, TAccepted>(
        DbCommand command,
        bool lockedRead,
        Func<DbDataReader, CancellationToken, ValueTask<TRow>> readRow,
        Func<DbDataReader, CancellationToken, ValueTask<TAccepted>> readAccepted,
        CancellationToken cancellationToken
    )
        where TRow : class
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        TRow? before = null;

        if (lockedRead)
        {
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                before = await readRow(reader, cancellationToken).ConfigureAwait(false);
            }

            await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "A fenced statement returned no decision row; the dialect rendered it wrong."
            );
        }

        return reader.GetBoolean(0)
            ? SqlFenced<TRow, TAccepted>.Accepted(
                before,
                await readAccepted(reader, cancellationToken).ConfigureAwait(false)
            )
            : SqlFenced<TRow, TAccepted>.Rejected(before);
    }
}
