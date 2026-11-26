// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Sql;

namespace Headless.Sequences;

/// <summary>
/// The one relational counter reader: every call is one plain <c>SELECT</c> on its own connection with no transaction
/// (<see cref="SqlAutonomousRead" />), over a table reference that never waits on a locked row.
/// </summary>
/// <remarks>
/// No statement writes, locks for update, or joins a transaction, so a read never blocks a gap-free unit holding a
/// counter's row and never takes SQLite's write lock. PostgreSQL and SQLite return a held row's last committed value;
/// SQL Server skips it.
/// </remarks>
#pragma warning disable CA2100 // SQL text comes from the dialect over validated schema and table names; values are parameters.
internal sealed class RelationalSequenceCounterReader : ISequenceCounterReader
{
    private static readonly SqlColumnType _TenantIdType = SqlColumnType.KeyText(SequenceFieldLimits.TenantIdMaxLength);
    private static readonly SqlColumnType _NameType = SqlColumnType.KeyText(SequenceFieldLimits.NameMaxLength);

    private static readonly SqlColumnType _PartitionType = SqlColumnType.KeyText(
        SequenceFieldLimits.PartitionMaxLength
    );

    private readonly ISqlDialect _dialect;
    private readonly RelationalSequencesOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly string _source;
    private readonly string _tenantId;
    private readonly string _name;
    private readonly string _partition;
    private readonly string _columns;
    private readonly string _order;
    private readonly Func<string, string> _foldAsciiCase;

    /// <summary>Creates a reader of the counter table <paramref name="options" /> names.</summary>
    /// <param name="dialect">The engine's SQL dialect.</param>
    /// <param name="options">The provider's options: connection string, schema, table, and command timeout.</param>
    /// <param name="timeProvider">The clock retries wait on.</param>
    /// <param name="foldAsciiCase">
    /// Renders a column with ASCII letters lowered and every other character unchanged, so a search ignores ASCII case
    /// alike on every engine. <see langword="null" /> uses <c>LOWER</c>, which folds only ASCII on PostgreSQL's
    /// <c>"C"</c> and SQLite's binary key columns.
    /// </param>
    public RelationalSequenceCounterReader(
        ISqlDialect dialect,
        RelationalSequencesOptions options,
        TimeProvider timeProvider,
        Func<string, string>? foldAsciiCase = null
    )
    {
        _foldAsciiCase = foldAsciiCase ?? (static column => $"LOWER({column})");
        _dialect = Argument.IsNotNull(dialect);
        _options = Argument.IsNotNull(options);
        _timeProvider = Argument.IsNotNull(timeProvider);
        _source = dialect.ReadWithoutWaiting(dialect.Qualify(options.Schema, options.TableName));
        _tenantId = SequencesColumns.TenantId(dialect);
        _name = SequencesColumns.Name(dialect);
        _partition = SequencesColumns.Partition(dialect);
        _columns =
            $"{_tenantId}, {_name}, {_partition}, {SequencesColumns.Value(dialect)}, {SequencesColumns.UpdatedAt(dialect)}";
        _order = $"{_tenantId}, {_name}, {_partition}";
    }

    public ValueTask<IReadOnlyList<SequenceCounterRecord>> ListAsync(
        SequenceCounterReadRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        var predicates = new List<string>();
        var bind = _Filter(request.Filter, predicates);

        if (request.After is { } after)
        {
            predicates.Add(
                _dialect.KeysetAfter([_tenantId, _name, _partition], ["AfterTenantId", "AfterName", "AfterPartition"])
            );
            bind += command =>
            {
                _dialect.AddParameter(command, "AfterTenantId", _TenantIdType, after.TenantId);
                _dialect.AddParameter(command, "AfterName", _NameType, after.Name);
                _dialect.AddParameter(command, "AfterPartition", _PartitionType, after.Partition);
            };
        }

        var sql =
            $"SELECT {_columns} FROM {_source} WHERE {_Where(predicates)} ORDER BY {_order} {_dialect.Limit("Limit")};";

        return _ReadRowsAsync("sequences.inspect.list", sql, bind, request.Limit, cancellationToken);
    }

    public ValueTask<IReadOnlyList<SequenceCounterRecord>> ListPartitionsAsync(
        SequencePartitionReadRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        var before = request.Before is null ? string.Empty : $" AND {_partition} < @Before";
        var sql =
            $"SELECT {_columns} FROM {_source} WHERE {_tenantId} = @TenantId AND {_name} = @Name{before} "
            + $"ORDER BY {_partition} DESC {_dialect.Limit("Limit")};";

        return _ReadRowsAsync(
            "sequences.inspect.partitions",
            sql,
            command =>
            {
                _dialect.AddParameter(command, "TenantId", _TenantIdType, request.TenantId);
                _dialect.AddParameter(command, "Name", _NameType, request.Name);

                if (request.Before is not null)
                {
                    _dialect.AddParameter(command, "Before", _PartitionType, request.Before);
                }
            },
            request.Limit,
            cancellationToken
        );
    }

    public ValueTask<SequenceCounterCounts> CountAsync(
        SequenceCounterCountRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        var predicates = new List<string>();
        var bind = _Filter(request.Filter, predicates);
        var (mode, bindModes) = _ModeCase(request.Modes);
        bind += bindModes;

        // The derived table reads at most one row past the cap, in key order, and numbers the rows so the per-mode
        // counts stop at the cap too. The mode CASE sits inside it because SQL Server refuses a subquery inside an
        // aggregate.
        var sql = $"""
            SELECT COUNT(*),
                COUNT(CASE WHEN rn <= @Cap AND m = {_Mode(SequenceMode.Fast)} THEN 1 END),
                COUNT(CASE WHEN rn <= @Cap AND m = {_Mode(SequenceMode.GapFree)} THEN 1 END),
                COUNT(CASE WHEN rn <= @Cap AND m = {_Mode(SequenceMode.Reported)} THEN 1 END)
            FROM (
                SELECT ROW_NUMBER() OVER (ORDER BY {_order}) AS rn, {mode} AS m
                FROM {_source}
                WHERE {_Where(predicates)}
                ORDER BY {_order} {_dialect.Limit("CountLimit")}
            ) capped;
            """;

        return SqlAutonomousRead.RunAsync(
            "sequences.inspect.summary",
            () => _dialect.CreateConnection(_options.ConnectionString),
            async (connection, ct) =>
            {
                await using var command = _Command(connection, sql);
                bind?.Invoke(command);
                _dialect.AddParameter(command, "Cap", SqlColumnType.Int32, request.Cap);
                _dialect.AddParameter(command, "CountLimit", SqlColumnType.Int32, request.Cap + 1);

                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                await reader.ReadAsync(ct).ConfigureAwait(false);

                // COUNT is int on SQL Server and bigint elsewhere.
                var matched = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);

                return new SequenceCounterCounts(
                    Math.Min(matched, request.Cap),
                    Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
                    Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture),
                    Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
                    Capped: matched > request.Cap
                );
            },
            _timeProvider,
            cancellationToken
        );
    }

    /// <summary>Adds the filter's predicates and returns the binding of their parameters.</summary>
    private Action<DbCommand>? _Filter(SequenceCounterFilter filter, List<string> predicates)
    {
        Argument.IsNotNull(filter);
        Action<DbCommand>? bind = null;

        if (filter.TenantId is { } tenantId)
        {
            predicates.Add($"{_tenantId} = @TenantId");
            bind += command => _dialect.AddParameter(command, "TenantId", _TenantIdType, tenantId);
        }

        if (filter.Name is { } name)
        {
            predicates.Add($"{_name} = @Name");
            bind += command => _dialect.AddParameter(command, "Name", _NameType, name);
        }

        if (filter.Search is { } search)
        {
            // Key columns compare case-sensitively, so both the column and the term fold ASCII letters only: engines
            // disagree on folding other letters, which therefore match only as typed everywhere.
            var escape = $"ESCAPE '{SqlLikePattern.EscapeCharacter}'";
            predicates.Add(
                $"({_foldAsciiCase(_name)} LIKE @Search {escape} OR {_foldAsciiCase(_tenantId)} LIKE @Search {escape})"
            );
            bind += command =>
                _dialect.AddParameter(
                    command,
                    "Search",
                    SqlColumnType.Text(-1),
                    SqlLikePattern.Contains(_FoldAscii(search))
                );
        }

        if (filter.Selection is { } selection)
        {
            predicates.Add(_Selection(selection, ref bind));
        }

        return bind;
    }

    private string _Selection(SequenceCounterSelection selection, ref Action<DbCommand>? bind)
    {
        var terms = new List<string>();

        if (selection.Names.Count > 0)
        {
            var names = selection.Names;
            terms.Add(_dialect.InList(_name, "SelectedNames", _NameType));
            bind += command => _dialect.AddListParameter(command, "SelectedNames", _NameType, names);
        }

        if (selection.NamePartitions.Count > 0)
        {
            IReadOnlyList<SqlColumnType> types = [_NameType, _PartitionType];
            var rows = selection
                .NamePartitions.Select(static pair => (IReadOnlyList<object>)[pair.Name, pair.Partition])
                .ToList();
            terms.Add(_dialect.InTuples([_name, _partition], "SelectedPartitions", types));
            bind += command =>
            {
                foreach (var parameter in _dialect.CreateTupleListParameters("SelectedPartitions", types, rows))
                {
                    command.Parameters.Add(parameter);
                }
            };
        }

        if (selection.IncludeUnregisteredNames)
        {
            var unregistered = new List<string>();

            if (selection.RegisteredNames.Count > 0)
            {
                var registered = selection.RegisteredNames;
                unregistered.Add($"NOT ({_dialect.InList(_name, "RegisteredNames", _NameType)})");
                bind += command => _dialect.AddListParameter(command, "RegisteredNames", _NameType, registered);
            }

            if (selection.UnregisteredNamePartition is { } partition)
            {
                unregistered.Add($"{_partition} = @UnregisteredPartition");
                bind += command => _dialect.AddParameter(command, "UnregisteredPartition", _PartitionType, partition);
            }

            terms.Add(unregistered.Count == 0 ? "1 = 1" : "(" + string.Join(" AND ", unregistered) + ")");
        }

        return terms.Count == 0 ? "1 = 0" : "(" + string.Join(" OR ", terms) + ")";
    }

    private (string Case, Action<DbCommand>? Bind) _ModeCase(SequenceModeNames modes)
    {
        Argument.IsNotNull(modes);
        var branches = new List<string>();
        Action<DbCommand>? bind = null;

        foreach (
            var (mode, names, parameter) in new[]
            {
                (SequenceMode.Fast, modes.Fast, "FastNames"),
                (SequenceMode.GapFree, modes.GapFree, "GapFreeNames"),
                (SequenceMode.Reported, modes.Reported, "ReportedNames"),
            }
        )
        {
            if (names.Count == 0)
            {
                continue;
            }

            branches.Add($"WHEN {_dialect.InList(_name, parameter, _NameType)} THEN {_Mode(mode)}");
            bind += command => _dialect.AddListParameter(command, parameter, _NameType, names);
        }

        var fallback = _Mode(modes.DefaultMode);

        return (branches.Count == 0 ? fallback : $"CASE {string.Join(' ', branches)} ELSE {fallback} END", bind);
    }

    private ValueTask<IReadOnlyList<SequenceCounterRecord>> _ReadRowsAsync(
        string operation,
        string sql,
        Action<DbCommand>? bind,
        int limit,
        CancellationToken cancellationToken
    )
    {
        return SqlAutonomousRead.RunAsync<IReadOnlyList<SequenceCounterRecord>>(
            operation,
            () => _dialect.CreateConnection(_options.ConnectionString),
            async (connection, ct) =>
            {
                await using var command = _Command(connection, sql);
                bind?.Invoke(command);
                _dialect.AddParameter(command, "Limit", SqlColumnType.Int32, limit);

                var rows = new List<SequenceCounterRecord>();
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    rows.Add(
                        new SequenceCounterRecord(
                            new SequenceKey(reader.GetString(0), reader.GetString(1), reader.GetString(2)),
                            reader.GetInt64(3),
                            await reader.GetFieldValueAsync<DateTimeOffset>(4, ct).ConfigureAwait(false)
                        )
                    );
                }

                return rows;
            },
            _timeProvider,
            cancellationToken
        );
    }

    private DbCommand _Command(DbConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = _options.CommandTimeoutSeconds;

        return command;
    }

    private static string _Where(List<string> predicates)
    {
        return predicates.Count == 0 ? "1 = 1" : string.Join(" AND ", predicates);
    }

    private static string _Mode(SequenceMode mode)
    {
        return ((int)mode).ToString(CultureInfo.InvariantCulture);
    }

    private static string _FoldAscii(string value)
    {
        return string.Create(
            value.Length,
            value,
            static (span, source) =>
            {
                for (var i = 0; i < source.Length; i++)
                {
                    var c = source[i];
                    span[i] = c is >= 'A' and <= 'Z' ? (char)(c | 0x20) : c;
                }
            }
        );
    }
}
#pragma warning restore CA2100
