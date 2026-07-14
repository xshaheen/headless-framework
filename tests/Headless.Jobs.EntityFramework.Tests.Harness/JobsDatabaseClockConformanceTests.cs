// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Provider-neutral guard that the Jobs lease paths let the DATABASE own ownership time. It drives the real
/// production methods (<c>BasePersistenceProvider</c> + the EF claim strategy) against a real container, captures
/// the SQL they emit through an interceptor on the real Jobs DbContext, and asserts three things about every
/// statement that touches <c>LockedUntil</c>:
/// <list type="number">
/// <item>
/// <b>The negative invariant (primary).</b> No lease predicate and no lease-deadline write may bind a
/// <em>timestamp parameter</em>. A duration parameter (the lease seconds) is fine and expected — an absolute
/// instant is not, because that is the app clock re-entering ownership math.
/// </item>
/// <item>
/// <b>The positive invariant.</b> The clock in those fragments is the provider's own server-clock function,
/// sourced from <see cref="IJobsCoordinationFixture.EfTranslatedDatabaseClockSql" /> rather than hard-coded.
/// </item>
/// <item>
/// <b>The transaction-scope invariant.</b> A statement that WRITES a lease deadline must run in autocommit. On
/// PostgreSQL the EF-translated clock is <c>now()</c> — transaction-start time — so wrapping a deadline write in
/// an explicit transaction would anchor the deadline to transaction-open and silently SHORTEN the lease by the
/// transaction's duration, letting a second node reclaim a row its owner still believes it holds. The production
/// code upholds this by construction; this suite is what enforces it.
/// </item>
/// </list>
/// A skewed-<c>TimeProvider</c> test cannot replace this: inside an <c>ExecuteUpdate</c> expression tree a bare
/// <c>DateTime.UtcNow</c> is provider-translated, but a regression to a client-evaluated clock ignores
/// <c>TimeProvider</c> entirely and therefore dodges an injected skew. The SQL on the wire is the only decisive
/// evidence.
/// </summary>
public abstract class JobsDatabaseClockConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    private static readonly TimeSpan _LeaseDuration = TimeSpan.FromMinutes(5);

    public virtual async Task claim_and_acquire_lease_sql_is_owned_by_the_database_clock()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var capture = new LeaseSqlCapture();
        using var host = fixture.BuildInterceptedHost("clock-claim", capture, _LeaseDuration);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            // Scheduled claim (CAS claim-tree update) — the primary time-job acquire.
            var scheduled = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "clock-scheduled",
                ExecutionTime = DateTime.UtcNow,
            };
            await persistence.AddTimeJobsAsync([scheduled], ct);
            var candidates = await persistence.GetEarliestTimeJobsAsync(ct);
            var claimed = await persistence.QueueTimeJobsAsync(candidates, ct).ToArrayAsync(ct);
            claimed.Should().ContainSingle().Which.Id.Should().Be(scheduled.Id);

            // Fallback claim (timed-out sweep) — same lease stamp, different eligibility predicate.
            var timedOut = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "clock-timed-out",
                ExecutionTime = DateTime.UtcNow.AddMinutes(-5),
            };
            await persistence.AddTimeJobsAsync([timedOut], ct);
            var reclaimed = await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);
            reclaimed.Should().ContainSingle().Which.Id.Should().Be(timedOut.Id);

            // Immediate acquire (LockedUntil + Status=InProgress in one statement).
            var immediate = new TimeJobEntity { Id = Guid.NewGuid(), Function = "clock-immediate" };
            await persistence.AddTimeJobsAsync([immediate], ct);
            var acquired = await persistence.AcquireImmediateTimeJobsAsync([immediate.Id], ct);
            acquired.Should().ContainSingle();

            // Cron occurrence claim — create-then-claim, plus the direct claim of a known occurrence.
            var cronId = Guid.NewGuid();
            await fixture.SeedCronJobAsync(cronId, "clock-cron", "* * * * *", NodeDeathPolicy.Retry, ct);
            var occurrences = await persistence
                .QueueCronJobOccurrencesAsync(
                    (
                        DateTime.UtcNow.AddMinutes(1),
                        [
                            new JobManagerDispatchContext(cronId)
                            {
                                FunctionName = "clock-cron",
                                Expression = "* * * * *",
                            },
                        ]
                    ),
                    ct
                )
                .ToArrayAsync(ct);
            occurrences.Should().ContainSingle();

            _AssertDatabaseOwnsTheLeaseClock(capture, expectDeadlineWrites: true, expectPredicates: true);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task lease_renewal_sql_is_owned_by_the_database_clock()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var capture = new LeaseSqlCapture();
        using var host = fixture.BuildInterceptedHost("clock-renew", capture, _LeaseDuration);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            var timeJob = new TimeJobEntity { Id = Guid.NewGuid(), Function = "clock-renew-time" };
            await persistence.AddTimeJobsAsync([timeJob], ct);
            (await persistence.AcquireImmediateTimeJobsAsync([timeJob.Id], ct)).Should().ContainSingle();

            var cronId = Guid.NewGuid();
            var occurrenceId = Guid.NewGuid();
            await fixture.SeedCronJobAsync(cronId, "clock-renew-cron", "* * * * *", NodeDeathPolicy.Retry, ct);
            await fixture.SeedCronOccurrenceAsync(
                occurrenceId,
                cronId,
                (int)JobStatus.Idle,
                ownerId: null,
                NodeDeathPolicy.Retry,
                lockedUntil: null,
                DateTime.UtcNow,
                ct
            );
            (await persistence.AcquireImmediateCronOccurrencesAsync([occurrenceId], ct)).Should().ContainSingle();

            // The renewals only match because the acquires above really ran and really own the rows — an affected
            // count of 1 is what proves the captured SQL is the live lease path, not a shape we merely rehearsed.
            capture.Clear();
            (await persistence.RenewTimeJobLeaseAsync(timeJob.Id, ct)).Should().Be(1);
            (await persistence.RenewCronJobOccurrenceLeaseAsync(occurrenceId, ct)).Should().Be(1);

            _AssertDatabaseOwnsTheLeaseClock(capture, expectDeadlineWrites: true, expectPredicates: false);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task reclaim_and_release_sweep_sql_is_owned_by_the_database_clock()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var capture = new LeaseSqlCapture();
        using var host = fixture.BuildInterceptedHost("clock-sweep", capture, _LeaseDuration);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            var timeJob = new TimeJobEntity { Id = Guid.NewGuid(), Function = "clock-sweep-time" };
            await persistence.AddTimeJobsAsync([timeJob], ct);
            (await persistence.AcquireImmediateTimeJobsAsync([timeJob.Id], ct)).Should().ContainSingle();

            capture.Clear();

            // The sweeps below are the statements that DO run inside an explicit transaction. They may read the
            // lease and release it (LockedUntil = NULL); they may never write a deadline. That is the invariant the
            // shared assertion enforces — a deadline write here would be anchored to transaction-open.
            await persistence.ReclaimStalledTimeJobsAsync(ct);
            await persistence.ReclaimStalledCronJobOccurrencesAsync(ct);
            await persistence.ReleaseDeadNodeTimeJobResourcesAsync("dead-node@1", ct);
            await persistence.ReleaseDeadNodeOccurrenceResourcesAsync("dead-node@1", ct);
            await persistence.ReleaseAcquiredTimeJobsAsync([timeJob.Id], ct);
            await persistence.ReleaseAcquiredCronJobOccurrencesAsync([Guid.NewGuid()], ct);

            _AssertDatabaseOwnsTheLeaseClock(capture, expectDeadlineWrites: false, expectPredicates: true);
            capture
                .Statements.SelectMany(LeaseSqlAnalysis.LeaseDeadlineWrites)
                .Should()
                .BeEmpty("a sweep runs in an explicit transaction and must never stamp a lease deadline");
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    private void _AssertDatabaseOwnsTheLeaseClock(
        LeaseSqlCapture capture,
        bool expectDeadlineWrites,
        bool expectPredicates
    )
    {
        var statements = capture.Statements;
        statements.Should().NotBeEmpty("the production lease path must have put at least one statement on the wire");

        var deadlineWrites = 0;
        var predicates = 0;

        foreach (var statement in statements)
        {
            foreach (var write in LeaseSqlAnalysis.LeaseDeadlineWrites(statement))
            {
                deadlineWrites++;

                write
                    .TemporalParameters.Should()
                    .BeEmpty(
                        "the lease deadline in '{0}' must be computed by the database, but it binds the timestamp "
                            + "parameter(s) {1} — an app clock is back in ownership math. Statement: {2}",
                        write.Fragment,
                        string.Join(", ", write.TemporalParameters.Select(name => _Describe(statement, name))),
                        statement.Sql
                    );

                write
                    .Fragment.Should()
                    .Contain(
                        fixture.EfTranslatedDatabaseClockSql,
                        "the lease deadline must be stamped from the provider's server clock"
                    );

                statement
                    .InExplicitTransaction.Should()
                    .BeFalse(
                        "a lease-deadline write must run in autocommit: inside an explicit transaction PostgreSQL's "
                            + "now() is frozen at transaction-open, which shortens the lease by the transaction's "
                            + "duration. Statement: {0}",
                        statement.Sql
                    );
            }

            foreach (var predicate in LeaseSqlAnalysis.LeasePredicates(statement))
            {
                predicates++;

                predicate
                    .TemporalParameters.Should()
                    .BeEmpty(
                        "the lease-expiry comparison in '{0}' must be made by the database, but it binds the "
                            + "timestamp parameter(s) {1} — a remote node's clock cannot decide ownership. "
                            + "Statement: {2}",
                        predicate.Fragment,
                        string.Join(", ", predicate.TemporalParameters.Select(name => _Describe(statement, name))),
                        statement.Sql
                    );

                predicate
                    .Fragment.Should()
                    .Contain(
                        fixture.EfTranslatedDatabaseClockSql,
                        "lease expiry must be evaluated against the provider's server clock"
                    );
            }
        }

        static string _Describe(CapturedSqlStatement statement, string name) =>
            statement.Parameters.TryGetValue(name, out var parameter)
                ? $"{name} ({parameter.DbType} = {parameter.Value})"
                : $"{name} (not bound — capture and SQL disagree)";

        if (expectDeadlineWrites)
        {
            deadlineWrites.Should().BePositive("the driven path is supposed to stamp a lease deadline");
        }

        if (expectPredicates)
        {
            predicates.Should().BePositive("the driven path is supposed to compare a lease deadline");
        }
    }
}

/// <summary>
/// Captures every command the REAL Jobs DbContext executes, along with its parameters and whether it ran inside an
/// explicit transaction. Attached through <see cref="JobsCoordinationFixtureExtensions.BuildInterceptedHost" />, so
/// what it records is production SQL, not a synthetic rehearsal of it.
/// </summary>
public sealed class LeaseSqlCapture : DbCommandInterceptor
{
    private readonly Lock _lock = new();
    private readonly List<CapturedSqlStatement> _statements = [];

    /// <summary>Only statements that touch the lease column — everything else is noise for this guard.</summary>
    public IReadOnlyList<CapturedSqlStatement> Statements
    {
        get
        {
            lock (_lock)
            {
                return [.. _statements];
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _statements.Clear();
        }
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result
    )
    {
        _Capture(command);
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        _Capture(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result
    )
    {
        _Capture(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        _Capture(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result
    )
    {
        _Capture(command);
        return base.ScalarExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default
    )
    {
        _Capture(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void _Capture(DbCommand command)
    {
        if (!LeaseSqlAnalysis.TouchesLeaseColumn(command.CommandText))
        {
            return;
        }

        var parameters = new Dictionary<string, CapturedSqlParameter>(StringComparer.OrdinalIgnoreCase);

        foreach (DbParameter parameter in command.Parameters)
        {
            var name = parameter.ParameterName.TrimStart('@');
            parameters[name] = new CapturedSqlParameter(name, parameter.DbType, parameter.Value);
        }

        var statement = new CapturedSqlStatement(
            command.CommandText,
            InExplicitTransaction: command.Transaction is not null,
            parameters
        );

        lock (_lock)
        {
            _statements.Add(statement);
        }
    }
}

/// <summary>One command as it went on the wire.</summary>
public sealed record CapturedSqlStatement(
    string Sql,
    bool InExplicitTransaction,
    IReadOnlyDictionary<string, CapturedSqlParameter> Parameters
);

/// <summary>One bound parameter. <see cref="IsTemporal" /> is the whole point: a duration is fine, an instant is not.</summary>
public sealed record CapturedSqlParameter(string Name, DbType DbType, object? Value)
{
    public bool IsTemporal =>
        Value is DateTime or DateTimeOffset
        || DbType is DbType.Date or DbType.DateTime or DbType.DateTime2 or DbType.DateTimeOffset or DbType.Time;
}

/// <summary>A <c>LockedUntil</c> assignment or comparison, isolated from the rest of the statement.</summary>
public sealed record LeaseSqlFragment(string Fragment, IReadOnlyList<string> TemporalParameters);

/// <summary>
/// Pulls the <c>LockedUntil</c> clauses out of a captured statement. Deliberately dialect-agnostic: it keys on the
/// quoted column identifier (<c>"LockedUntil"</c> on Postgres, <c>[LockedUntil]</c> on SQL Server) and on which
/// parameters the clause references, never on how a provider happens to spell its clock.
/// </summary>
public static class LeaseSqlAnalysis
{
    // Column reference in either dialect's quoting. A bare occurrence in a SELECT list or INSERT column list is
    // matched too, but carries no operator, so it falls out below.
    private static readonly Regex _LeaseColumn = new(
        """["\[]LockedUntil["\]]""",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1)
    );

    private static readonly Regex _ParameterReference = new(
        "@[A-Za-z0-9_]+",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1)
    );

    // Words that end the clause we are reading. INTERVAL / CAST / DATEADD arguments are intentionally absent: they
    // belong to the lease expression and must stay inside the fragment.
    private static readonly string[] _ClauseTerminators =
    [
        "AND",
        "OR",
        "WHERE",
        "FROM",
        "RETURNING",
        "ORDER",
        "GROUP",
        "LIMIT",
        "OFFSET",
        "OUTPUT",
        "UNION",
        "ON",
        "WHEN",
        "THEN",
        "ELSE",
        "END",
    ];

    public static bool TouchesLeaseColumn(string sql) => _LeaseColumn.IsMatch(sql);

    /// <summary>Assignments of a lease deadline: <c>LockedUntil = &lt;expression&gt;</c>, excluding the release to NULL.</summary>
    public static IEnumerable<LeaseSqlFragment> LeaseDeadlineWrites(CapturedSqlStatement statement) =>
        _Clauses(statement)
            .Where(clause =>
                string.Equals(clause.Operator, "=", StringComparison.Ordinal)
                && !string.Equals(clause.Fragment.Trim(), "NULL", StringComparison.OrdinalIgnoreCase)
            )
            .Select(clause => clause.Fragment)
            .Select(fragment => _Describe(fragment, statement));

    /// <summary>Ownership comparisons: <c>LockedUntil &lt;= &lt;clock&gt;</c> and friends. <c>IS NULL</c> tests carry no clock and are skipped.</summary>
    public static IEnumerable<LeaseSqlFragment> LeasePredicates(CapturedSqlStatement statement) =>
        _Clauses(statement)
            .Where(clause => !string.Equals(clause.Operator, "=", StringComparison.Ordinal))
            .Select(clause => clause.Fragment)
            .Select(fragment => _Describe(fragment, statement));

    private static LeaseSqlFragment _Describe(string fragment, CapturedSqlStatement statement)
    {
        // An unresolvable parameter reference means the capture and the SQL disagree; count it as temporal so the
        // guard fails loud rather than passing a clause it cannot vouch for.
        var temporal = _ParameterReference
            .Matches(fragment)
            .Select(match => match.Value.TrimStart('@'))
            .Where(name => !statement.Parameters.TryGetValue(name, out var parameter) || parameter.IsTemporal)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new LeaseSqlFragment(fragment, temporal);
    }

    private static IEnumerable<(string Operator, string Fragment)> _Clauses(CapturedSqlStatement statement)
    {
        var sql = statement.Sql;

        foreach (Match match in _LeaseColumn.Matches(sql))
        {
            var index = _SkipWhitespace(sql, match.Index + match.Length);
            var @operator = _ReadOperator(sql, index);

            if (@operator is null)
            {
                continue; // A select-list / insert-column-list occurrence, or an IS [NOT] NULL test.
            }

            yield return (@operator, _ReadClause(sql, index + @operator.Length));
        }
    }

    private static readonly string[] _Operators = ["<=", ">=", "<>", "!=", "<", ">", "="];

    private static string? _ReadOperator(string sql, int index)
    {
        if (index >= sql.Length)
        {
            return null;
        }

        return Array.Find(
            _Operators,
            candidate =>
                index + candidate.Length <= sql.Length
                && string.CompareOrdinal(sql, index, candidate, 0, candidate.Length) == 0
        );
    }

    /// <summary>Reads the expression that follows the operator, up to the end of its clause.</summary>
    private static string _ReadClause(string sql, int start)
    {
        var depth = 0;
        var index = start;

        while (index < sql.Length)
        {
            var current = sql[index];

            if (current == '\'')
            {
                index = _SkipStringLiteral(sql, index);
                continue;
            }

            if (current == '(')
            {
                depth++;
                index++;
                continue;
            }

            if (current == ')')
            {
                if (depth == 0)
                {
                    break;
                }

                depth--;
                index++;
                continue;
            }

            if (depth == 0 && current == ',')
            {
                break;
            }

            if (char.IsLetter(current))
            {
                var word = _ReadWord(sql, index);

                if (depth == 0 && _ClauseTerminators.Contains(word, StringComparer.OrdinalIgnoreCase))
                {
                    break;
                }

                index += word.Length;
                continue;
            }

            index++;
        }

        return sql[start..index].Trim();
    }

    private static string _ReadWord(string sql, int index)
    {
        var end = index;
        while (end < sql.Length && (char.IsLetterOrDigit(sql[end]) || sql[end] == '_'))
        {
            end++;
        }

        return sql[index..end];
    }

    private static int _SkipStringLiteral(string sql, int index)
    {
        index++; // opening quote

        while (index < sql.Length)
        {
            if (sql[index] != '\'')
            {
                index++;
                continue;
            }

            // '' is an escaped quote inside the literal.
            if (index + 1 < sql.Length && sql[index + 1] == '\'')
            {
                index += 2;
                continue;
            }

            return index + 1;
        }

        return index;
    }

    private static int _SkipWhitespace(string sql, int index)
    {
        while (index < sql.Length && char.IsWhiteSpace(sql[index]))
        {
            index++;
        }

        return index;
    }
}
