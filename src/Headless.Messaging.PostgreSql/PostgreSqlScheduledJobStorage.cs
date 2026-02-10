// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Persistence;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Headless.Messaging.PostgreSql;

/// <summary>
/// PostgreSQL implementation of <see cref="IScheduledJobStorage"/> for scheduling persistence.
/// Uses SELECT FOR UPDATE SKIP LOCKED for atomic job acquisition in distributed environments.
/// </summary>
public sealed class PostgreSqlScheduledJobStorage(
    IOptions<PostgreSqlOptions> postgreSqlOptions,
    IStorageInitializer initializer,
    TimeProvider timeProvider
) : IScheduledJobStorage
{
    private readonly string _jobsTable = initializer.GetScheduledJobsTableName();
    private readonly string _executionsTable = initializer.GetJobExecutionsTableName();

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScheduledJob>> AcquireDueJobsAsync(
        int batchSize,
        string lockHolder,
        CancellationToken cancellationToken = default
    )
    {
        var now = timeProvider.GetUtcNow();

        // Use CTE to atomically select and update jobs in a single statement
        var sql = $"""
            WITH due_jobs AS (
                SELECT "Id"
                FROM {_jobsTable}
                WHERE "NextRunTime" <= @Now
                  AND "Status" = @PendingStatus
                  AND "IsEnabled" = true
                ORDER BY "NextRunTime"
                LIMIT @BatchSize
                FOR UPDATE SKIP LOCKED
            )
            UPDATE {_jobsTable} j
            SET "Status" = @RunningStatus,
                "LockHolder" = @LockHolder,
                "DateLocked" = @Now,
                "Version" = j."Version" + 1
            FROM due_jobs
            WHERE j."Id" = due_jobs."Id"
            RETURNING j."Id", j."Name", j."Type", j."CronExpression", j."TimeZone",
                      j."Payload", j."Status", j."NextRunTime", j."LastRunTime",
                      j."LastRunDuration", j."MaxRetries", j."RetryIntervals",
                      j."SkipIfRunning", j."LockHolder", j."DateLocked",
                      j."IsEnabled", j."DateCreated", j."DateUpdated",
                      j."Timeout", j."MisfireStrategy", j."ConsumerTypeName",
                      j."Version";
            """;

        object[] sqlParams =
        [
            new NpgsqlParameter("@Now", now),
            new NpgsqlParameter("@PendingStatus", ScheduledJobStatus.Pending.ToString("G")),
            new NpgsqlParameter("@RunningStatus", ScheduledJobStatus.Running.ToString("G")),
            new NpgsqlParameter("@LockHolder", lockHolder),
            new NpgsqlParameter("@BatchSize", batchSize),
        ];

        await using var connection = postgreSqlOptions.Value.CreateConnection();

        return await connection
            .ExecuteReaderAsync(sql, _ReadJobsAsync, cancellationToken: cancellationToken, sqlParams: sqlParams)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ScheduledJob?> GetJobByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var sql = $"""
            SELECT "Id", "Name", "Type", "CronExpression", "TimeZone",
                   "Payload", "Status", "NextRunTime", "LastRunTime",
                   "LastRunDuration", "MaxRetries", "RetryIntervals",
                   "SkipIfRunning", "LockHolder", "DateLocked",
                   "IsEnabled", "DateCreated", "DateUpdated",
                   "Timeout", "MisfireStrategy", "ConsumerTypeName",
                   "Version"
            FROM {_jobsTable}
            WHERE "Name" = @Name;
            """;

        object[] sqlParams = [new NpgsqlParameter("@Name", name)];

        await using var connection = postgreSqlOptions.Value.CreateConnection();

        var jobs = await connection
            .ExecuteReaderAsync(sql, _ReadJobsAsync, cancellationToken: cancellationToken, sqlParams: sqlParams)
            .ConfigureAwait(false);

        return jobs.Count > 0 ? jobs[0] : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScheduledJob>> GetAllJobsAsync(CancellationToken cancellationToken = default)
    {
        var sql = $"""
            SELECT "Id", "Name", "Type", "CronExpression", "TimeZone",
                   "Payload", "Status", "NextRunTime", "LastRunTime",
                   "LastRunDuration", "MaxRetries", "RetryIntervals",
                   "SkipIfRunning", "LockHolder", "DateLocked",
                   "IsEnabled", "DateCreated", "DateUpdated",
                   "Timeout", "MisfireStrategy", "ConsumerTypeName",
                   "Version"
            FROM {_jobsTable}
            ORDER BY "Name";
            """;

        await using var connection = postgreSqlOptions.Value.CreateConnection();

        return await connection
            .ExecuteReaderAsync(sql, _ReadJobsAsync, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> GetStaleJobCountAsync(
        DateTimeOffset threshold,
        CancellationToken cancellationToken = default
    )
    {
        var sql = $"""
            SELECT COUNT(*)
            FROM {_jobsTable}
            WHERE "Status" = @RunningStatus
              AND "DateLocked" IS NOT NULL
              AND "DateLocked" < @Threshold;
            """;

        object[] sqlParams =
        [
            new NpgsqlParameter("@RunningStatus", ScheduledJobStatus.Running.ToString("G")),
            new NpgsqlParameter("@Threshold", threshold),
        ];

        await using var connection = postgreSqlOptions.Value.CreateConnection();

        return await connection
            .ExecuteScalarAsync(sql, cancellationToken: cancellationToken, sqlParams: sqlParams)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpsertJobAsync(ScheduledJob job, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();

        var sql = $"""
            INSERT INTO {_jobsTable} (
                "Id", "Name", "Type", "CronExpression", "TimeZone",
                "Payload", "Status", "NextRunTime", "LastRunTime",
                "LastRunDuration", "MaxRetries", "RetryIntervals",
                "SkipIfRunning", "LockHolder", "DateLocked",
                "IsEnabled", "DateCreated", "DateUpdated",
                "Timeout", "MisfireStrategy", "ConsumerTypeName",
                "Version"
            ) VALUES (
                @Id, @Name, @Type, @CronExpression, @TimeZone,
                @Payload, @Status, @NextRunTime, @LastRunTime,
                @LastRunDuration, @MaxRetries, @RetryIntervals,
                @SkipIfRunning, @LockHolder, @DateLocked,
                @IsEnabled, @DateCreated, @DateUpdated,
                @Timeout, @MisfireStrategy, @ConsumerTypeName,
                @Version
            )
            ON CONFLICT ("Name") DO UPDATE SET
                "Type" = EXCLUDED."Type",
                "CronExpression" = EXCLUDED."CronExpression",
                "TimeZone" = EXCLUDED."TimeZone",
                "Payload" = EXCLUDED."Payload",
                "NextRunTime" = EXCLUDED."NextRunTime",
                "RetryIntervals" = EXCLUDED."RetryIntervals",
                "SkipIfRunning" = EXCLUDED."SkipIfRunning",
                "IsEnabled" = EXCLUDED."IsEnabled",
                "Timeout" = EXCLUDED."Timeout",
                "MisfireStrategy" = EXCLUDED."MisfireStrategy",
                "ConsumerTypeName" = EXCLUDED."ConsumerTypeName",
                "DateUpdated" = @Now;
            """;

        var sqlParams = _BuildJobParameters(job);
        sqlParams.Add(new NpgsqlParameter("@Now", now));

        await using var connection = postgreSqlOptions.Value.CreateConnection();

        await connection
            .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: [.. sqlParams])
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateJobAsync(ScheduledJob job, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();

        var sql = $"""
            UPDATE {_jobsTable}
            SET "Type" = @Type,
                "CronExpression" = @CronExpression,
                "TimeZone" = @TimeZone,
                "Payload" = @Payload,
                "Status" = @Status,
                "NextRunTime" = @NextRunTime,
                "LastRunTime" = @LastRunTime,
                "LastRunDuration" = @LastRunDuration,
                "MaxRetries" = @MaxRetries,
                "RetryIntervals" = @RetryIntervals,
                "SkipIfRunning" = @SkipIfRunning,
                "LockHolder" = @LockHolder,
                "DateLocked" = @DateLocked,
                "IsEnabled" = @IsEnabled,
                "Timeout" = @Timeout,
                "MisfireStrategy" = @MisfireStrategy,
                "ConsumerTypeName" = @ConsumerTypeName,
                "DateUpdated" = @Now,
                "Version" = "Version" + 1
            WHERE "Id" = @Id
              AND "Version" = @Version;
            """;

        var sqlParams = _BuildJobParameters(job);
        sqlParams.Add(new NpgsqlParameter("@Now", now));

        await using var connection = postgreSqlOptions.Value.CreateConnection();

        var affected = await connection
            .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: [.. sqlParams])
            .ConfigureAwait(false);

        if (affected == 0)
        {
            throw new ScheduledJobConcurrencyException(job.Id, job.Version);
        }

        job.Version++;
    }

    /// <inheritdoc />
    public async Task DeleteJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var sql = $"""DELETE FROM {_jobsTable} WHERE "Id" = @Id;""";

        object[] sqlParams = [new NpgsqlParameter("@Id", jobId)];

        await using var connection = postgreSqlOptions.Value.CreateConnection();

        await connection
            .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: sqlParams)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CreateExecutionAsync(JobExecution execution, CancellationToken cancellationToken = default)
    {
        var sql = $"""
            INSERT INTO {_executionsTable} (
                "Id", "JobId", "ScheduledTime", "DateStarted", "DateCompleted",
                "Status", "Duration", "RetryAttempt", "Error"
            ) VALUES (
                @Id, @JobId, @ScheduledTime, @DateStarted, @DateCompleted,
                @Status, @Duration, @RetryAttempt, @Error
            );
            """;

        var sqlParams = _BuildExecutionParameters(execution);

        await using var connection = postgreSqlOptions.Value.CreateConnection();

        await connection
            .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: [.. sqlParams])
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateExecutionAsync(JobExecution execution, CancellationToken cancellationToken = default)
    {
        var sql = $"""
            UPDATE {_executionsTable}
            SET "DateStarted" = @DateStarted,
                "DateCompleted" = @DateCompleted,
                "Status" = @Status,
                "Duration" = @Duration,
                "RetryAttempt" = @RetryAttempt,
                "Error" = @Error
            WHERE "Id" = @Id;
            """;

        var sqlParams = _BuildExecutionParameters(execution);

        await using var connection = postgreSqlOptions.Value.CreateConnection();

        await connection
            .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: [.. sqlParams])
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobExecution>> GetExecutionsAsync(
        Guid jobId,
        int limit,
        CancellationToken cancellationToken = default
    )
    {
        var sql = $"""
            SELECT "Id", "JobId", "ScheduledTime", "DateStarted", "DateCompleted",
                   "Status", "Duration", "RetryAttempt", "Error"
            FROM {_executionsTable}
            WHERE "JobId" = @JobId
            ORDER BY "ScheduledTime" DESC
            LIMIT @Limit;
            """;

        object[] sqlParams = [new NpgsqlParameter("@JobId", jobId), new NpgsqlParameter("@Limit", limit)];

        await using var connection = postgreSqlOptions.Value.CreateConnection();

        return await connection
            .ExecuteReaderAsync(sql, _ReadExecutionsAsync, cancellationToken: cancellationToken, sqlParams: sqlParams)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExecutionStatusCount>> GetExecutionStatusCountsAsync(
        Guid jobId,
        int days = 7,
        CancellationToken cancellationToken = default
    )
    {
        var cutoff = timeProvider.GetUtcNow().AddDays(-days);

        var sql = $"""
            SELECT DATE_TRUNC('day', "ScheduledTime") AS "Day",
                   "Status",
                   COUNT(*)::int AS "Count"
            FROM {_executionsTable}
            WHERE "JobId" = @JobId
              AND "ScheduledTime" >= @Cutoff
            GROUP BY "Day", "Status"
            ORDER BY "Day", "Status";
            """;

        object[] sqlParams = [new NpgsqlParameter("@JobId", jobId), new NpgsqlParameter("@Cutoff", cutoff)];

        await using var connection = postgreSqlOptions.Value.CreateConnection();

        return await connection
            .ExecuteReaderAsync(sql, _ReadStatusCountsAsync, cancellationToken: cancellationToken, sqlParams: sqlParams)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> TimeoutStaleExecutionsAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();

        // Mark Running executions whose parent job is no longer Running (already released)
        var sql = $"""
            UPDATE {_executionsTable} e
            SET "Status" = @TimedOutStatus,
                "DateCompleted" = @Now,
                "Error" = @Error,
                "Duration" = CASE
                    WHEN e."DateStarted" IS NOT NULL
                    THEN EXTRACT(EPOCH FROM (@Now - e."DateStarted")) * 1000
                    ELSE NULL
                END
            WHERE e."Status" = @RunningStatus
              AND NOT EXISTS (
                  SELECT 1 FROM {_jobsTable} j
                  WHERE j."Id" = e."JobId"
                    AND j."Status" = @JobRunningStatus
              );
            """;

        object[] sqlParams =
        [
            new NpgsqlParameter("@TimedOutStatus", JobExecutionStatus.TimedOut.ToString("G")),
            new NpgsqlParameter("@RunningStatus", JobExecutionStatus.Running.ToString("G")),
            new NpgsqlParameter("@JobRunningStatus", ScheduledJobStatus.Running.ToString("G")),
            new NpgsqlParameter("@Now", now),
            new NpgsqlParameter("@Error", "Terminated by stale job recovery: owning process became unresponsive."),
        ];

        await using var connection = postgreSqlOptions.Value.CreateConnection();

        return await connection
            .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: sqlParams)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> ReleaseStaleJobsAsync(TimeSpan staleness, CancellationToken cancellationToken = default)
    {
        var threshold = timeProvider.GetUtcNow() - staleness;

        var sql = $"""
            UPDATE {_jobsTable}
            SET "Status" = @Status,
                "LockHolder" = NULL,
                "DateLocked" = NULL
            WHERE "Status" = @RunningStatus
              AND "DateLocked" < @Threshold;
            """;

        object[] sqlParams =
        [
            new NpgsqlParameter("@Status", ScheduledJobStatus.Pending.ToString("G")),
            new NpgsqlParameter("@RunningStatus", ScheduledJobStatus.Running.ToString("G")),
            new NpgsqlParameter("@Threshold", threshold),
        ];

        await using var connection = postgreSqlOptions.Value.CreateConnection();

        return await connection
            .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: sqlParams)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> PurgeExecutionsAsync(TimeSpan retention, CancellationToken cancellationToken = default)
    {
        var threshold = timeProvider.GetUtcNow() - retention;

        var sql = $"""
            DELETE FROM {_executionsTable}
            WHERE "DateCompleted" IS NOT NULL
              AND "DateCompleted" < @Threshold;
            """;

        object[] sqlParams = [new NpgsqlParameter("@Threshold", threshold)];

        await using var connection = postgreSqlOptions.Value.CreateConnection();

        return await connection
            .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: sqlParams)
            .ConfigureAwait(false);
    }

    private static async Task<List<ScheduledJob>> _ReadJobsAsync(
        System.Data.Common.DbDataReader reader,
        CancellationToken cancellationToken
    )
    {
        var jobs = new List<ScheduledJob>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var job = new ScheduledJob
            {
                Id = reader.GetGuid(0),
                Name = reader.GetString(1),
                Type = Enum.Parse<ScheduledJobType>(reader.GetString(2)),
                CronExpression = await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false)
                    ? null
                    : reader.GetString(3),
                TimeZone = reader.GetString(4),
                Payload = await reader.IsDBNullAsync(5, cancellationToken).ConfigureAwait(false)
                    ? null
                    : reader.GetString(5),
                Status = Enum.Parse<ScheduledJobStatus>(reader.GetString(6)),
                NextRunTime = await reader.IsDBNullAsync(7, cancellationToken).ConfigureAwait(false)
                    ? null
                    : new DateTimeOffset(reader.GetDateTime(7), TimeSpan.Zero),
                LastRunTime = await reader.IsDBNullAsync(8, cancellationToken).ConfigureAwait(false)
                    ? null
                    : new DateTimeOffset(reader.GetDateTime(8), TimeSpan.Zero),
                LastRunDuration = await reader.IsDBNullAsync(9, cancellationToken).ConfigureAwait(false)
                    ? null
                    : reader.GetInt64(9),
                MaxRetries = reader.GetInt32(10),
                RetryIntervals = await reader.IsDBNullAsync(11, cancellationToken).ConfigureAwait(false)
                    ? null
                    : (int[])reader.GetValue(11),
                SkipIfRunning = reader.GetBoolean(12),
                LockHolder = await reader.IsDBNullAsync(13, cancellationToken).ConfigureAwait(false)
                    ? null
                    : reader.GetString(13),
                DateLocked = await reader.IsDBNullAsync(14, cancellationToken).ConfigureAwait(false)
                    ? null
                    : new DateTimeOffset(reader.GetDateTime(14), TimeSpan.Zero),
                IsEnabled = reader.GetBoolean(15),
                DateCreated = new DateTimeOffset(reader.GetDateTime(16), TimeSpan.Zero),
                DateUpdated = new DateTimeOffset(reader.GetDateTime(17), TimeSpan.Zero),
                Timeout = await reader.IsDBNullAsync(18, cancellationToken).ConfigureAwait(false)
                    ? null
                    : TimeSpan.FromMilliseconds(reader.GetInt64(18)),
                MisfireStrategy = Enum.Parse<MisfireStrategy>(reader.GetString(19)),
                ConsumerTypeName = await reader.IsDBNullAsync(20, cancellationToken).ConfigureAwait(false)
                    ? null
                    : reader.GetString(20),
                Version = reader.GetInt64(21),
            };

            jobs.Add(job);
        }

        return jobs;
    }

    private static async Task<List<JobExecution>> _ReadExecutionsAsync(
        System.Data.Common.DbDataReader reader,
        CancellationToken cancellationToken
    )
    {
        var executions = new List<JobExecution>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var execution = new JobExecution
            {
                Id = reader.GetGuid(0),
                JobId = reader.GetGuid(1),
                ScheduledTime = new DateTimeOffset(reader.GetDateTime(2), TimeSpan.Zero),
                DateStarted = await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false)
                    ? null
                    : new DateTimeOffset(reader.GetDateTime(3), TimeSpan.Zero),
                DateCompleted = await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false)
                    ? null
                    : new DateTimeOffset(reader.GetDateTime(4), TimeSpan.Zero),
                Status = Enum.Parse<JobExecutionStatus>(reader.GetString(5)),
                Duration = await reader.IsDBNullAsync(6, cancellationToken).ConfigureAwait(false)
                    ? null
                    : reader.GetInt64(6),
                RetryAttempt = reader.GetInt32(7),
                Error = await reader.IsDBNullAsync(8, cancellationToken).ConfigureAwait(false)
                    ? null
                    : reader.GetString(8),
            };

            executions.Add(execution);
        }

        return executions;
    }

    private static async Task<List<ExecutionStatusCount>> _ReadStatusCountsAsync(
        System.Data.Common.DbDataReader reader,
        CancellationToken cancellationToken
    )
    {
        var counts = new List<ExecutionStatusCount>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            counts.Add(
                new ExecutionStatusCount
                {
                    Date = new DateTimeOffset(reader.GetDateTime(0), TimeSpan.Zero),
                    Status = reader.GetString(1),
                    Count = reader.GetInt32(2),
                }
            );
        }

        return counts;
    }

    // CA1508 false positive: nullable value types can be null, but analyzer doesn't recognize this pattern
#pragma warning disable CA1508
    private static List<NpgsqlParameter> _BuildJobParameters(ScheduledJob job)
    {
        return
        [
            new NpgsqlParameter("@Id", job.Id),
            new NpgsqlParameter("@Name", job.Name),
            new NpgsqlParameter("@Type", job.Type.ToString("G")),
            new NpgsqlParameter("@CronExpression", (object?)job.CronExpression ?? DBNull.Value),
            new NpgsqlParameter("@TimeZone", job.TimeZone),
            new NpgsqlParameter("@Payload", (object?)job.Payload ?? DBNull.Value),
            new NpgsqlParameter("@Status", job.Status.ToString("G")),
            new NpgsqlParameter("@NextRunTime", (object?)job.NextRunTime ?? DBNull.Value),
            new NpgsqlParameter("@LastRunTime", (object?)job.LastRunTime ?? DBNull.Value),
            new NpgsqlParameter("@LastRunDuration", (object?)job.LastRunDuration ?? DBNull.Value),
            new NpgsqlParameter("@MaxRetries", job.MaxRetries),
            new NpgsqlParameter("@RetryIntervals", NpgsqlDbType.Array | NpgsqlDbType.Integer)
            {
                Value = (object?)job.RetryIntervals ?? DBNull.Value,
            },
            new NpgsqlParameter("@SkipIfRunning", job.SkipIfRunning),
            new NpgsqlParameter("@LockHolder", (object?)job.LockHolder ?? DBNull.Value),
            new NpgsqlParameter("@DateLocked", (object?)job.DateLocked ?? DBNull.Value),
            new NpgsqlParameter("@IsEnabled", job.IsEnabled),
            new NpgsqlParameter("@DateCreated", job.DateCreated),
            new NpgsqlParameter("@DateUpdated", job.DateUpdated),
            new NpgsqlParameter(
                "@Timeout",
                (object?)(job.Timeout.HasValue ? (long)job.Timeout.Value.TotalMilliseconds : null) ?? DBNull.Value
            ),
            new NpgsqlParameter("@MisfireStrategy", job.MisfireStrategy.ToString("G")),
            new NpgsqlParameter("@ConsumerTypeName", (object?)job.ConsumerTypeName ?? DBNull.Value),
            new NpgsqlParameter("@Version", job.Version),
        ];
    }

    private static List<NpgsqlParameter> _BuildExecutionParameters(JobExecution execution)
    {
        return
        [
            new NpgsqlParameter("@Id", execution.Id),
            new NpgsqlParameter("@JobId", execution.JobId),
            new NpgsqlParameter("@ScheduledTime", execution.ScheduledTime),
            new NpgsqlParameter("@DateStarted", (object?)execution.DateStarted ?? DBNull.Value),
            new NpgsqlParameter("@DateCompleted", (object?)execution.DateCompleted ?? DBNull.Value),
            new NpgsqlParameter("@Status", execution.Status.ToString("G")),
            new NpgsqlParameter("@Duration", (object?)execution.Duration ?? DBNull.Value),
            new NpgsqlParameter("@RetryAttempt", execution.RetryAttempt),
            new NpgsqlParameter("@Error", (object?)execution.Error ?? DBNull.Value),
        ];
    }
#pragma warning restore CA1508
}
