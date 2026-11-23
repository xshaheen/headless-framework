// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Runtime.ExceptionServices;
using Headless.Checks;

namespace Headless;

/// <summary>
/// Runs a write that must commit only while a group of rows still has the version a caller read, and settles the
/// failures a relational database reports around it: a concurrency conflict is retried or rejected, and a commit whose
/// reply was lost counts as success only when storage confirms it.
/// </summary>
/// <remarks>
/// <para>
/// Each attempt opens its own transaction, checks the group's version inside it, writes, computes the version the write
/// leaves, and calls <see cref="RelationalVersionedWriteAttempt.BeginCommit" /> just before it commits. On a failure the
/// group is read again outside any transaction:
/// </para>
/// <list type="bullet">
/// <item>A concurrency conflict (<see cref="RelationalFaults.IsConcurrencyConflict" />), even one reported by the commit,
/// means the database rolled the attempt back. A changed version throws <see cref="DBConcurrencyException" />; an
/// unchanged one runs a fresh attempt, up to <see cref="MaxAttempts" />, then throws <see cref="DBConcurrencyException" />
/// as well, so the caller re-reads instead of receiving a server error.</item>
/// <item>Any other failure after <see cref="RelationalVersionedWriteAttempt.BeginCommit" /> leaves the commit's outcome
/// unknown. It is never replayed: when the group now has the version the attempt meant to leave, that version is
/// returned; otherwise the original failure is rethrown.</item>
/// <item>Any other failure before the commit, a failure to read the group again, and cancellation rethrow the original
/// failure.</item>
/// </list>
/// <para>
/// The version must change whenever a row of the group is inserted, updated, or deleted, and new rows must carry fresh
/// identities, so another writer cannot reproduce the version an attempt meant to leave. Under an EF Core execution
/// strategy, run the strategy inside <c>attempt</c> and return its failure as a value rather than throwing it there, so
/// the strategy never replays an uncertain commit.
/// </para>
/// </remarks>
[PublicAPI]
public static class RelationalVersionedWrite
{
    /// <summary>The number of attempts a write gets while conflicts leave the group's version unchanged.</summary>
    public const int MaxAttempts = 3;

    /// <summary>Runs <paramref name="attempt" /> until it commits, the group changes, or the attempts run out.</summary>
    /// <param name="attempt">
    /// One transaction: check the version, write, compute the resulting version, call
    /// <see cref="RelationalVersionedWriteAttempt.BeginCommit" />, commit, and return the resulting version. Throws
    /// <see cref="DBConcurrencyException" /> when the version inside the transaction is not <paramref name="expectedVersion" />.
    /// </param>
    /// <param name="readVersion">Reads the group outside any transaction and returns its current version.</param>
    /// <param name="expectedVersion">The version the caller read.</param>
    /// <param name="conflictMessage">The message of the <see cref="DBConcurrencyException" /> thrown on a conflict.</param>
    /// <param name="cancellationToken">The abort token, passed to both delegates.</param>
    /// <returns>The group's version after the write.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null" />.</exception>
    /// <exception cref="DBConcurrencyException">
    /// The group no longer has <paramref name="expectedVersion" />, or conflicts outlasted <see cref="MaxAttempts" />.
    /// </exception>
    public static async Task<string> ExecuteAsync(
        Func<RelationalVersionedWriteAttempt, CancellationToken, Task<string>> attempt,
        Func<CancellationToken, Task<string>> readVersion,
        string expectedVersion,
        string conflictMessage,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(attempt);
        Argument.IsNotNull(readVersion);
        Argument.IsNotNull(expectedVersion);
        Argument.IsNotNull(conflictMessage);

        for (var number = 1; ; number++)
        {
            // Fresh per attempt: a commit flag left over from a rolled-back attempt would turn the next attempt's
            // early failure into an apparent lost reply.
            var state = new RelationalVersionedWriteAttempt();

            try
            {
                return await attempt(state, cancellationToken).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Only conflicts and post-commit failures are caught; both are settled or rethrown below.
            catch (Exception exception)
                when (!cancellationToken.IsCancellationRequested
                    && (state.CommitStarted || RelationalFaults.IsConcurrencyConflict(exception))
                )
#pragma warning restore CA1031
            {
                var conflict = RelationalFaults.IsConcurrencyConflict(exception);
                string currentVersion;

                try
                {
                    currentVersion = await readVersion(cancellationToken).ConfigureAwait(false);
                }
#pragma warning disable CA1031 // A failed re-read must surface the write's own failure, not the read's.
                catch (Exception)
#pragma warning restore CA1031
                {
                    ExceptionDispatchInfo.Capture(exception).Throw();
                    throw;
                }

                if (conflict)
                {
                    if (
                        string.Equals(currentVersion, expectedVersion, StringComparison.Ordinal)
                        && number < MaxAttempts
                    )
                    {
                        continue;
                    }

                    throw new DBConcurrencyException(conflictMessage, exception);
                }

                if (string.Equals(currentVersion, state.ResultVersion, StringComparison.Ordinal))
                {
                    return currentVersion;
                }

                throw;
            }
        }
    }
}
