// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Sql;

namespace Headless.Fencing;

/// <summary>
/// The grant assignments of a <see cref="FencedRowTable{TKey}" /> for a statement the consumer writes, such as a claim
/// of a job and its whole subtree: every row the statement updates is granted under its own new generation, with
/// parameters named under <see cref="Prefix" /> so two scopes can share one statement.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Assignments" /> renders assignments only: that statement owns which rows it updates and must lock them the
/// way its engine needs, and the fence the helper's own operations apply (no live attempt holds the row) is the
/// consumer's filter to write. <see cref="RenderClaim" /> renders a whole claim instead, the fence and the locking
/// included, for a consumer that claims its own rows with columns of its own set and read back. Either way, read each
/// granted row's key, generation, and expiry back through <see cref="Returning" /> and build a
/// <see cref="FencedRow{TKey}" /> from each.
/// </para>
/// <para>
/// Each row's generation is drawn from that row (or the table's sequence), never copied from another row, so one
/// child's token cannot fence a sibling. On SQLite a sequence is read once per statement, so its rows share one value;
/// each is still above that row's earlier generations.
/// </para>
/// </remarks>
/// <typeparam name="TKey">The row's key type.</typeparam>
[PublicAPI]
public sealed class FencedRowGrantScope<TKey>
    where TKey : notnull
{
    private readonly FencedRowTable<TKey> _table;

    internal FencedRowGrantScope(FencedRowTable<TKey> table, string prefix)
    {
        _table = table;
        Prefix = prefix;
        Returning = [.. table.KeyColumns, table.Generation, table.ExpiresAt];
    }

    /// <summary>Gets the prefix of the scope's parameter names: <c>{Prefix}Duration</c> and <c>{Prefix}Holder</c>.</summary>
    public string Prefix { get; }

    /// <summary>
    /// Gets the quoted columns to read back from each granted row, in order: the key columns, the generation, and the
    /// expiry.
    /// </summary>
    public IReadOnlyList<string> Returning { get; }

    /// <summary>
    /// Renders the grant's assignments for a <c>SET</c> clause, over unqualified columns and
    /// <see cref="SqlDialectTokens.Now" />, which the consumer's statement resolves to the database clock.
    /// </summary>
    /// <param name="deadlineExpression">
    /// A trusted SQL expression for the expiry, such as the root row's stored deadline so a subtree expires with its
    /// root, or <see langword="null" /> for the clock plus the bound duration.
    /// </param>
    /// <returns>The assignments, without the <c>SET</c> keyword.</returns>
    public string Assignments(string? deadlineExpression = null)
    {
        if (deadlineExpression is not null)
        {
            Argument.IsNotNullOrWhiteSpace(deadlineExpression);
        }

        var deadline = deadlineExpression ?? _table.Dialect.ShiftByDuration(SqlDialectTokens.Now, Prefix + "Duration");

        return _table.GrantAssignments(deadline, Prefix + "Holder");
    }

    /// <summary>
    /// Renders a claim of up to a batch of rows <paramref name="filter" /> selects that the lease lets a new attempt
    /// take, granting each under its own new generation with the consumer's assignments in the same statement, as
    /// <see cref="FencedRows.ClaimAsync{TKey}" /> does for the lease alone.
    /// </summary>
    /// <remarks>
    /// PostgreSQL and SQL Server skip rows another transaction has locked; SQLite serializes claimers on its write lock.
    /// Bind the scope's parameters with <see cref="Bind" />, and the batch size and every parameter the consumer's
    /// fragments name on the same command.
    /// </remarks>
    /// <param name="filter">A trusted SQL predicate selecting the candidate rows, over unqualified columns.</param>
    /// <param name="orderBy">The quoted columns that order candidates; the first rows in this order are claimed.</param>
    /// <param name="set">The consumer's trusted assignments applied beside the grant, or <see langword="null" />.</param>
    /// <param name="returning">
    /// The quoted columns to read back after <see cref="Returning" />'s, so the first of them sits at the ordinal that
    /// follows the expiry; empty for the grant's columns alone.
    /// </param>
    /// <param name="batchSizeParameter">The name, without its prefix, of the parameter that bounds the batch.</param>
    /// <param name="takeover">
    /// Whether a grant may take a row whose attempt expired without ending, as <see cref="LeaseGrantOptions.Takeover" />
    /// says.
    /// </param>
    /// <param name="deadlineExpression">
    /// A trusted SQL expression for the expiry, or <see langword="null" /> for the clock plus the bound duration.
    /// </param>
    /// <returns>The claim statement, rendered for the table's dialect.</returns>
    /// <exception cref="ArgumentException">A fragment or name is empty or white space, or <paramref name="takeover" /> is not defined.</exception>
    public string RenderClaim(
        string filter,
        IReadOnlyList<string> orderBy,
        string? set,
        IReadOnlyList<string> returning,
        string batchSizeParameter,
        LeaseTakeover takeover = LeaseTakeover.Allowed,
        string? deadlineExpression = null
    )
    {
        Argument.IsNotNullOrWhiteSpace(filter);
        Argument.IsNotNullOrEmpty(orderBy);

        foreach (var order in orderBy)
        {
            Argument.IsNotNullOrWhiteSpace(order, paramName: nameof(orderBy));
        }

        if (set is not null)
        {
            Argument.IsNotNullOrWhiteSpace(set);
        }

        Argument.IsNotNull(returning);

        foreach (var column in returning)
        {
            Argument.IsNotNullOrWhiteSpace(column, paramName: nameof(returning));
        }

        Argument.IsNotNullOrWhiteSpace(batchSizeParameter);
        LeaseRequestResolver.ValidateGrantOptions(new LeaseGrantOptions { Takeover = takeover });

        var fence = takeover == LeaseTakeover.AfterSweep ? _table.AfterSweepFence : _table.GrantableFence;
        var grant = Assignments(deadlineExpression);

        return _table.Dialect.Render(
            new SqlClaimNext(
                _table.Table,
                _table.KeyColumns,
                $"({filter}) AND {fence}",
                orderBy,
                set is null ? grant : grant + ",\n    " + set,
                [.. Returning, .. returning],
                batchSizeParameter
            )
        );
    }

    /// <summary>Binds the scope's parameters.</summary>
    /// <param name="command">The consumer's command.</param>
    /// <param name="duration">
    /// How long each row is held when <see cref="Assignments" /> is rendered without a deadline expression; bound
    /// either way, since a rendered deadline may still read it.
    /// </param>
    /// <param name="holder">The holder to record on each row, or <see langword="null" />.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration" /> is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="holder" /> is blank or longer than its limit.</exception>
    public void Bind(DbCommand command, TimeSpan duration, string? holder)
    {
        Argument.IsNotNull(command);
        Argument.IsPositive(duration);
        LeaseRequestResolver.ValidateGrantOptions(new LeaseGrantOptions { Holder = holder });

        _table.Dialect.AddDuration(command, Prefix + "Duration", duration);
        _table.Dialect.AddParameter(
            command,
            Prefix + "Holder",
            SqlColumnType.Text(FencingFieldLimits.HolderMaxLength),
            holder
        );
    }
}
