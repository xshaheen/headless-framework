// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Messaging.Internal;
using Headless.Sql;

namespace Headless.Messaging.Persistence;

/// <summary>
/// Renders the <c>WHERE</c> predicate of a <see cref="MessageListQuery"/> over the published or received table, and
/// binds its parameters. The list, the capped count, and an Operation's scope read share it, so a count and the rows
/// it counts never disagree about which rows a filter matches.
/// </summary>
internal static class RelationalMessageFilter
{
    private const int _TextMaxLength = 200;

    /// <summary>The predicate of a terminal row: succeeded or failed, with no retry pending.</summary>
    public static string Terminal(MessagingTables t) =>
        $"{t.StatusName} IN ('{nameof(StatusName.Succeeded)}','{nameof(StatusName.Failed)}') AND {t.NextRetryAt} IS NULL";

    /// <summary>Returns the predicate and the binder of <paramref name="query"/>'s filters.</summary>
    public static (string Where, Action<DbCommand> Bind) Build(
        MessagingTables t,
        ISqlDialect dialect,
        MessageListQuery query
    )
    {
        var received = query.MessageType == MessageType.Subscribe;
        var where = $"{t.IntentType} IN (0, 1)";

        if (query.Status is not null)
        {
            where += $" AND {t.StatusName} = @FilterStatus";
        }

        if (query.TerminalOnly)
        {
            where += $" AND {Terminal(t)}";
        }

        if (query.Lane is not null)
        {
            where += $" AND {t.IntentType} = @FilterIntentType";
        }

        if (query.Name is not null)
        {
            where += ExactText(t.Name, "FilterName");
        }

        if (query.MessageId is not null)
        {
            where += ExactText(t.MessageId, "FilterMessageId");
        }

        if (query.FailureFingerprint is not null)
        {
            where += ExactText(t.FailureFingerprint, "FilterFingerprint");
        }

        if (received && query.ConsumerIdentity is not null)
        {
            where += ExactText(t.ConsumerIdentity, "FilterConsumer");
        }

        if (received && query.TenantId is not null)
        {
            where += $" AND {t.TenantPresent} = {t.True}" + ExactText(t.TenantId, "FilterTenant");
        }

        if (query.IsHeld is { } held)
        {
            where +=
                received ? $" AND {t.IsHeld} = {(held ? t.True : t.False)}"
                : held ? " AND 1 = 0"
                : "";
        }

        void bind(DbCommand command)
        {
            if (query.Status is { } status)
            {
                dialect.AddParameter(command, "FilterStatus", SqlColumnType.Text(50), status.ToString("G"));
            }

            if (query.Lane is { } lane)
            {
                dialect.AddParameter(
                    command,
                    "FilterIntentType",
                    SqlColumnType.Int16,
                    MessageLaneCompatibility.ToPersistedValue(lane)
                );
            }

            BindExactText(dialect, command, "FilterName", query.Name, SqlColumnType.Text(_TextMaxLength));
            BindExactText(dialect, command, "FilterMessageId", query.MessageId, SqlColumnType.KeyText(_TextMaxLength));
            BindExactText(
                dialect,
                command,
                "FilterFingerprint",
                query.FailureFingerprint,
                SqlColumnType.Text(MessagingFailureFingerprints.MaxLength)
            );

            if (received)
            {
                BindExactText(
                    dialect,
                    command,
                    "FilterConsumer",
                    query.ConsumerIdentity,
                    SqlColumnType.KeyText(_TextMaxLength)
                );
                BindExactText(dialect, command, "FilterTenant", query.TenantId, SqlColumnType.KeyText(_TextMaxLength));
            }
        }

        return (where, bind);
    }

    /// <summary>
    /// The predicate, prefixed with <c> AND </c>, that <paramref name="column"/> equals the text bound by
    /// <see cref="BindExactText"/> under <paramref name="parameter"/>. The equality seeks; the <c>LIKE</c> of the
    /// escaped value keeps the match exact on an engine whose equality ignores trailing spaces.
    /// </summary>
    public static string ExactText(string column, string parameter) =>
        $" AND {column} = @{parameter} AND {column} LIKE @{parameter}Pattern ESCAPE '\\'";

    /// <summary>
    /// Binds the value and the escaped pattern of an <see cref="ExactText"/> predicate, or nothing when
    /// <paramref name="value"/> is <see langword="null"/>. The pattern is unbounded, so a value longer than its column,
    /// which a sized parameter would truncate, still matches nothing.
    /// </summary>
    public static void BindExactText(
        ISqlDialect dialect,
        DbCommand command,
        string parameter,
        string? value,
        SqlColumnType type
    )
    {
        if (value is null)
        {
            return;
        }

        dialect.AddParameter(command, parameter, type, value);
        dialect.AddParameter(command, parameter + "Pattern", SqlColumnType.Text(-1), SqlLikePattern.Escape(value));
    }

    /// <summary>
    /// The keyset predicate of a newest-first read: rows strictly before (<paramref name="atParameter"/>,
    /// <paramref name="idParameter"/>) in (<paramref name="atColumn"/> DESC, <paramref name="idColumn"/> DESC) order.
    /// </summary>
    public static string Before(string atColumn, string idColumn, string atParameter, string idParameter) =>
        $"({atColumn} < @{atParameter} OR ({atColumn} = @{atParameter} AND {idColumn} < @{idParameter}))";
}
