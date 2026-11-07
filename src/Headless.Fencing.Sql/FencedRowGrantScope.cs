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
/// The scope renders assignments only. The consumer's statement owns which rows it updates and must lock them the way
/// its engine needs; the fence the helper's own operations apply (no live attempt holds the row) is the consumer's
/// filter to write. Read each granted row's key, generation, and expiry back through <see cref="Returning" /> and build
/// a <see cref="FencedRow{TKey}" /> from each.
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
