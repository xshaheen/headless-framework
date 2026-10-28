// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data.Common;
using Headless.Jobs;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// Provider-neutral conformance for run-now enqueue under clock skew: a job enqueued with no due time is stamped by the
/// STORE inside its write, from the store's current-statement clock, and takes the immediate-dispatch path whichever
/// way the enqueuing node's clock is off. Each host here runs a deliberately skewed <see cref="TimeProvider"/>, so a
/// stamp taken from the node's clock lands two minutes away from the database's and fails the tolerance below.
/// </summary>
/// <remarks>
/// Tolerances compare a database instant with database instants read before and after the call on independent
/// connections, so they absorb only round-trip latency, never the injected skew.
/// </remarks>
public abstract class JobsStoreClockEnqueueConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    private static readonly TimeSpan _Skew = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan _Tolerance = TimeSpan.FromSeconds(1);

    // Well inside the 30-second fallback sweep, so a job found Succeeded by then went through the immediate path.
    private static readonly TimeSpan _ImmediateDispatchBound = TimeSpan.FromSeconds(10);

    public virtual Task run_now_enqueue_from_a_host_ahead_of_the_store_is_stamped_by_the_store_and_runs_immediately()
    {
        return _RunNowIsStampedByTheStoreAndRunsImmediatelyAsync("run-now-ahead", _Skew);
    }

    public virtual Task run_now_enqueue_from_a_host_behind_the_store_is_stamped_by_the_store_and_runs_immediately()
    {
        return _RunNowIsStampedByTheStoreAndRunsImmediatelyAsync("run-now-behind", -_Skew);
    }

    /// <summary>
    /// A coordinated run-now write joins a transaction that opened seconds earlier and commits seconds later. The stamp
    /// must be the statement's instant, not the transaction's start (PostgreSQL's <c>now()</c>), and the post-commit
    /// worker must still take the job on the immediate path after the slow commit.
    /// </summary>
    public virtual async Task coordinated_run_now_enqueue_is_stamped_at_the_write_and_runs_immediately_after_a_slow_commit()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost(
            "run-now-coordinated",
            timeProvider: new SkewedTimeProvider(-_Skew),
            runBackgroundServices: true,
            configureServices: fixture.ConfigureUnitOfWork
        );
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var job = _RunNowJob();
            TimeJobEntity? returned = null;
            DateTime transactionStart = default;
            DateTime beforeWrite = default;
            DateTime afterWrite = default;

            await fixture.RunCoordinatedTransactionAsync(
                host.Services,
                async (_, unitOfWork, connection, transaction, innerCt) =>
                {
                    transactionStart = await _ReadEnlistedClockAsync(connection, transaction, innerCt);
                    await Task.Delay(TimeSpan.FromSeconds(3), innerCt);
                    beforeWrite = await _StoreUtcNowAsync(innerCt);
                    returned = await unitOfWork.TimeJobs<TimeJobEntity>().AddAsync(job, innerCt);
                    afterWrite = await _StoreUtcNowAsync(innerCt);
                    await Task.Delay(TimeSpan.FromSeconds(2), innerCt);
                },
                ct
            );
            var committedAt = DateTime.UtcNow;

            var stamp = await _ReadExecutionTimeAsync(job.Id, ct);
            stamp.Should().BeOnOrAfter(beforeWrite - _Tolerance).And.BeOnOrBefore(afterWrite + _Tolerance);
            stamp
                .Should()
                .BeAfter(
                    transactionStart.AddSeconds(2),
                    "the stamp is the statement's clock, not the transaction's start"
                );
            returned!.ExecutionTime.Should().BeCloseTo(stamp, TimeSpan.FromMilliseconds(1));

            await _WaitForSucceededAsync(job.Id, ct);
            (DateTime.UtcNow - committedAt).Should().BeLessThan(_ImmediateDispatchBound);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task run_now_batch_roots_share_one_store_instant_and_an_absolute_root_is_kept()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("run-now-batch", timeProvider: new SkewedTimeProvider(_Skew));
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
            var runNow = new[] { _RunNowJob(), _RunNowJob(), _RunNowJob() };
            // Whole seconds, so neither backend's timestamp precision can round the stored value.
            var absoluteTime = new DateTime(2031, 3, 4, 5, 6, 7, DateTimeKind.Utc);
            var absolute = _RunNowJob();
            absolute.ExecutionTime = absoluteTime;

            var before = await _StoreUtcNowAsync(ct);
            var returned = await manager.AddBatchAsync([.. runNow, absolute], ct);
            var after = await _StoreUtcNowAsync(ct);

            var stamps = new List<DateTime>();
            foreach (var job in runNow)
            {
                stamps.Add(await _ReadExecutionTimeAsync(job.Id, ct));
            }

            stamps.Distinct().Should().ContainSingle("every run-now root of one write shares one store instant");
            stamps[0].Should().BeOnOrAfter(before - _Tolerance).And.BeOnOrBefore(after + _Tolerance);
            (await _ReadExecutionTimeAsync(absolute.Id, ct)).Should().Be(absoluteTime);
            returned
                .Take(3)
                .Should()
                .AllSatisfy(x => x.ExecutionTime.Should().BeCloseTo(stamps[0], TimeSpan.FromMilliseconds(1)));
            returned[3].ExecutionTime.Should().Be(absoluteTime);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task idempotent_run_now_enqueue_is_stamped_by_the_store_and_a_repeat_writes_nothing()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("run-now-idempotent", timeProvider: new SkewedTimeProvider(_Skew));
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
            var first = _RunNowJob();

            var before = await _StoreUtcNowAsync(ct);
            var created = await manager.AddIdempotentAsync(first, "store-clock-key", TimeSpan.FromMinutes(10), ct);
            var after = await _StoreUtcNowAsync(ct);
            var repeat = await manager.AddIdempotentAsync(
                _RunNowJob(),
                "store-clock-key",
                TimeSpan.FromMinutes(10),
                ct
            );

            var stamp = await _ReadExecutionTimeAsync(created.Id, ct);
            stamp.Should().BeOnOrAfter(before - _Tolerance).And.BeOnOrBefore(after + _Tolerance);
            created.ExecutionTime.Should().BeCloseTo(stamp, TimeSpan.FromMilliseconds(1));
            repeat.Id.Should().Be(created.Id);
            (await fixture.CountTimeJobsAsync(ct)).Should().Be(1);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>A write whose due times are all absolute has nothing for the store to resolve, so it reads no clock.</summary>
    public virtual async Task absolute_only_enqueue_reads_no_store_clock()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var capture = new CommandCapture();
        using var host = fixture.BuildInterceptedHost("absolute-only", capture);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
        var job = _RunNowJob();
        job.ExecutionTime = DateTime.UtcNow.AddHours(1);

        capture.Clear();
        await manager.AddAsync(job, ct);

        capture.Commands.Should().NotBeEmpty();
        capture
            .Commands.Should()
            .NotContain(sql =>
                sql.Contains("clock_timestamp", StringComparison.OrdinalIgnoreCase)
                || sql.Contains("SYSUTCDATETIME", StringComparison.OrdinalIgnoreCase)
            );
    }

    private async Task _RunNowIsStampedByTheStoreAndRunsImmediatelyAsync(string nodeId, TimeSpan skew)
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost(
            nodeId,
            timeProvider: new SkewedTimeProvider(skew),
            runBackgroundServices: true
        );
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
            var job = _RunNowJob();

            var before = await _StoreUtcNowAsync(ct);
            var returned = await manager.AddAsync(job, ct);
            var after = await _StoreUtcNowAsync(ct);
            var enqueuedAt = DateTime.UtcNow;

            var stamp = await _ReadExecutionTimeAsync(job.Id, ct);
            stamp.Should().BeOnOrAfter(before - _Tolerance).And.BeOnOrBefore(after + _Tolerance);
            returned.ExecutionTime.Should().BeCloseTo(stamp, TimeSpan.FromMilliseconds(1));

            await _WaitForSucceededAsync(job.Id, ct);
            (DateTime.UtcNow - enqueuedAt).Should().BeLessThan(_ImmediateDispatchBound);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    private static TimeJobEntity _RunNowJob()
    {
        return new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
            Description = JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
            Request = [],
        };
    }

    private async Task _WaitForSucceededAsync(Guid id, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_ImmediateDispatchBound);
        var status = -1;

        try
        {
            while ((status = (await fixture.ReadTimeJobAsync(id, timeout.Token)).Status) != (int)JobStatus.Succeeded)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Fall through to the assertion, which reports the status the job was stuck in.
        }

        ((JobStatus)status).Should().Be(JobStatus.Succeeded, "a run-now job is taken by the immediate path");
    }

    private async Task<DateTime> _ReadExecutionTimeAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = fixture.Sql(
            $"SELECT \"ExecutionTime\" FROM {fixture.QualifiedTimeJobsTable} WHERE \"Id\" = @id;"
        );
        JobsCoordinationFixtureExtensions.AddParameter(command, "@id", id);

        return _AsUtc(await command.ExecuteScalarAsync(cancellationToken));
    }

    /// <summary>The store's own instant, read on an independent autocommit connection.</summary>
    private async Task<DateTime> _StoreUtcNowAsync(CancellationToken cancellationToken)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {fixture.UtcNowSqlExpression}";

        return _AsUtc(await command.ExecuteScalarAsync(cancellationToken));
    }

    /// <summary>The clock an EF-translated <c>DateTime.UtcNow</c> resolves to inside the supplied transaction.</summary>
    private async Task<DateTime> _ReadEnlistedClockAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {fixture.EfTranslatedDatabaseClockSql}";

        return _AsUtc(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static DateTime _AsUtc(object? value)
    {
        return value switch
        {
            DateTimeOffset instant => instant.UtcDateTime,
            DateTime instant => DateTime.SpecifyKind(instant, DateTimeKind.Utc),
            _ => throw new InvalidOperationException("The store did not return an instant."),
        };
    }

    /// <summary>A TimeProvider deliberately offset from real time. Only <c>GetUtcNow</c> is skewed.</summary>
    private sealed class SkewedTimeProvider(TimeSpan offset) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.Add(offset);
    }

    private sealed class CommandCapture : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> _commands = new();

        public string[] Commands => [.. _commands];

        public void Clear() => _commands.Clear();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            _commands.Enqueue(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            _commands.Enqueue(command.CommandText);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default
        )
        {
            _commands.Enqueue(command.CommandText);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
