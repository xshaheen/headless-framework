// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

// An ordinary edit may only touch a job no node has started: an idle unclaimed row, or a queued row whose claim the
// edit releases. A stale copy of a running or finished row must never overwrite what the executing node writes.
public abstract partial class JobsCoordinationConformanceTests<TFixture>
{
    private const string _GuardedOwner = "node-a@1";

    public virtual async Task update_refuses_and_changes_nothing_when_the_row_is_owned_running_or_finished(int status)
    {
        var ct = AbortToken;
        await Fixture.ResetDatabaseAsync(ct);

        using var host = Fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
        var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

        var id = Guid.NewGuid();
        await Fixture.SeedTimeJobAsync(
            id,
            "Guarded",
            status,
            _GuardedOwner,
            ct,
            lockedUntil: DateTime.UtcNow.AddMinutes(5)
        );
        var before = _GuardedSnapshot((await persistence.GetTimeJobByIdAsync(id, ct))!);

        // A stale copy that would also rewrite the runtime columns if the store trusted them.
        var staleCopy = (await persistence.GetTimeJobByIdAsync(id, ct))!;
        staleCopy.Description = "edited";
        staleCopy.ExecutionTime = DateTime.UtcNow.AddHours(1);
        staleCopy.Status = JobStatus.Idle;
        staleCopy.RetryCount = 3;
        staleCopy.OwnerId = null;

        (await manager.UpdateAsync(staleCopy, ct)).IsSucceeded.Should().BeFalse();

        _GuardedSnapshot((await persistence.GetTimeJobByIdAsync(id, ct))!).Should().Be(before);
    }

    public virtual async Task update_applies_to_an_idle_unowned_row_and_keeps_its_stored_runtime_state()
    {
        var ct = AbortToken;
        await Fixture.ResetDatabaseAsync(ct);

        using var host = Fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
        var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

        var id = Guid.NewGuid();
        await Fixture.SeedTimeJobAsync(id, "Guarded", (int)JobStatus.Idle, ownerId: null, ct);

        var copy = (await persistence.GetTimeJobByIdAsync(id, ct))!;
        copy.Description = "edited";
        copy.ExecutionTime = DateTime.UtcNow.AddHours(1);
        copy.Status = JobStatus.Succeeded;
        copy.RetryCount = 7;
        copy.ExceptionMessage = "forged";

        _ShouldSucceed(await manager.UpdateAsync(copy, ct));

        var stored = (await persistence.GetTimeJobByIdAsync(id, ct))!;
        stored.Description.Should().Be("edited");
        stored.Status.Should().Be(JobStatus.Idle);
        stored.RetryCount.Should().Be(0);
        stored.ExceptionMessage.Should().BeNull();
    }

    /// <summary>
    /// The scheduler claims the next-due job up to a day early, so rescheduling it must still work: the edit releases
    /// the claim in the same write, and the node that claimed it can no longer start it.
    /// </summary>
    public virtual async Task update_releases_a_preclaimed_queued_job_and_its_former_owner_cannot_start_it()
    {
        var ct = AbortToken;
        await Fixture.ResetDatabaseAsync(ct);

        using var host = Fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var owner = host.Services.GetRequiredService<INodeMembership>().Identity!.Value.ToString();
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var job = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "Guarded",
                // Due now, so the claim below takes it: a direct claim refuses a row that is not yet due.
                ExecutionTime = DateTime.UtcNow.AddMilliseconds(500),
            };
            await persistence.AddTimeJobsAsync([job], ct);
            var due = (await persistence.GetEarliestTimeJobsAsync(ct)).Jobs;
            (await persistence.QueueTimeJobsAsync(due, ct).ToListAsync(ct)).Should().ContainSingle();
            (await Fixture.ReadTimeJobAsync(job.Id, ct)).Should().Be(((int)JobStatus.Queued, owner));

            var copy = (await persistence.GetTimeJobByIdAsync(job.Id, ct))!;
            var newTime = new DateTime(2035, 1, 1, 10, 0, 0, DateTimeKind.Utc);
            copy.ExecutionTime = newTime;

            _ShouldSucceed(await manager.UpdateAsync(copy, ct));

            var (status, ownerId, lockedUntil, _, _) = await Fixture.ReadTimeJobDetailAsync(job.Id, ct);
            status.Should().Be((int)JobStatus.Idle);
            ownerId.Should().BeNull();
            lockedUntil.Should().BeNull();
            (await persistence.GetTimeJobByIdAsync(job.Id, ct))!.ExecutionTime.Should().Be(newTime);

            var start = new JobExecutionState { FunctionName = string.Empty, ClaimOwner = owner }.SetProperty(
                x => x.Status,
                JobStatus.InProgress
            );
            (await persistence.UpdateTimeJobsWithUnifiedContextAsync([job.Id], start, ct)).Should().BeEmpty();
            (await Fixture.ReadTimeJobAsync(job.Id, ct)).Should().Be(((int)JobStatus.Idle, null));
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task update_batch_with_one_refused_row_rolls_back_every_row()
    {
        var ct = AbortToken;
        await Fixture.ResetDatabaseAsync(ct);

        using var host = Fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
        var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

        var editableId = Guid.NewGuid();
        var runningId = Guid.NewGuid();
        await Fixture.SeedTimeJobAsync(editableId, "Guarded", (int)JobStatus.Idle, ownerId: null, ct);
        await Fixture.SeedTimeJobAsync(
            runningId,
            "Guarded",
            (int)JobStatus.InProgress,
            _GuardedOwner,
            ct,
            lockedUntil: DateTime.UtcNow.AddMinutes(5)
        );

        var editable = (await persistence.GetTimeJobByIdAsync(editableId, ct))!;
        editable.Description = "edited";
        editable.ExecutionTime = DateTime.UtcNow.AddHours(1);
        var running = (await persistence.GetTimeJobByIdAsync(runningId, ct))!;
        running.Description = "edited";
        running.ExecutionTime = DateTime.UtcNow.AddHours(1);

        (await manager.UpdateBatchAsync([editable, running], ct)).IsSucceeded.Should().BeFalse();

        (await persistence.GetTimeJobByIdAsync(editableId, ct))!.Description.Should().Be("Guarded");
        (await persistence.GetTimeJobByIdAsync(runningId, ct))!.Description.Should().Be("Guarded");
    }

    public virtual async Task update_inserts_a_new_child_attached_to_an_idle_root()
    {
        var ct = AbortToken;
        await Fixture.ResetDatabaseAsync(ct);

        using var host = Fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
        var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var root = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "root",
            ExecutionTime = DateTime.UtcNow.AddHours(1),
        };
        await persistence.AddTimeJobsAsync([root], ct);

        var copy = (await persistence.GetTimeJobByIdAsync(root.Id, ct))!;
        // No Id: the store tells a new child from an existing row by an unset key.
        var child = new TimeJobEntity { Function = "child", RunCondition = RunCondition.OnSuccess };
        copy.Children.Add(child);

        _ShouldSucceed(await manager.UpdateAsync(copy, ct));

        child.Id.Should().NotBe(Guid.Empty);
        (await Fixture.ReadTimeJobAsync(child.Id, ct)).Should().Be(((int)JobStatus.Idle, null));
        (await persistence.GetTimeJobByIdAsync(root.Id, ct))!.Children.Should().ContainSingle(x => x.Id == child.Id);
    }

    /// <summary>
    /// Reading a pre-claimed chain root loads its descendants, which the claim stamped with the root's owner. Editing
    /// that graph must still succeed and leave the whole carried tree unowned, so no node runs part of it.
    /// </summary>
    public virtual async Task update_of_a_preclaimed_chain_root_releases_its_carried_descendants()
    {
        var ct = AbortToken;
        await Fixture.ResetDatabaseAsync(ct);

        using var host = Fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var owner = host.Services.GetRequiredService<INodeMembership>().Identity!.Value.ToString();
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var child = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "child",
                RunCondition = RunCondition.OnSuccess,
            };
            var root = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "root",
                // Due now, so the claim below takes it: a direct claim refuses a row that is not yet due.
                ExecutionTime = DateTime.UtcNow.AddMilliseconds(500),
                Children = [child],
            };
            await persistence.AddTimeJobsAsync([root], ct);
            var due = (await persistence.GetEarliestTimeJobsAsync(ct)).Jobs;
            (await persistence.QueueTimeJobsAsync(due, ct).ToListAsync(ct)).Should().ContainSingle();
            (await Fixture.ReadTimeJobAsync(child.Id, ct)).Should().Be(((int)JobStatus.Idle, owner));

            var copy = (await persistence.GetTimeJobByIdAsync(root.Id, ct))!;
            copy.ExecutionTime = DateTime.UtcNow.AddHours(1);
            copy.Children.Single().Description = "edited child";

            _ShouldSucceed(await manager.UpdateAsync(copy, ct));

            foreach (var id in new[] { root.Id, child.Id })
            {
                var (status, ownerId, lockedUntil, _, _) = await Fixture.ReadTimeJobDetailAsync(id, ct);
                status.Should().Be((int)JobStatus.Idle);
                ownerId.Should().BeNull();
                lockedUntil.Should().BeNull();
            }

            (await persistence.GetTimeJobByIdAsync(child.Id, ct))!.Description.Should().Be("edited child");
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task update_is_refused_when_a_carried_descendant_belongs_to_another_owner()
    {
        var ct = AbortToken;
        await Fixture.ResetDatabaseAsync(ct);

        using var host = Fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
        var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var child = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "child",
            RunCondition = RunCondition.OnSuccess,
        };
        var root = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "root",
            ExecutionTime = DateTime.UtcNow.AddHours(1),
            Children = [child],
        };
        await persistence.AddTimeJobsAsync([root], ct);
        await _StampTimeJobAsync(root.Id, JobStatus.Queued, _GuardedOwner, ct);
        await _StampTimeJobAsync(child.Id, JobStatus.Idle, "node-b@1", ct);
        var rootBefore = _GuardedSnapshot((await persistence.GetTimeJobByIdAsync(root.Id, ct))!);
        var childBefore = _GuardedSnapshot((await persistence.GetTimeJobByIdAsync(child.Id, ct))!);

        var copy = (await persistence.GetTimeJobByIdAsync(root.Id, ct))!;
        copy.Description = "edited";

        (await manager.UpdateAsync(copy, ct)).IsSucceeded.Should().BeFalse();

        _GuardedSnapshot((await persistence.GetTimeJobByIdAsync(root.Id, ct))!).Should().Be(rootBefore);
        _GuardedSnapshot((await persistence.GetTimeJobByIdAsync(child.Id, ct))!).Should().Be(childBefore);
    }

    /// <summary>
    /// <c>UpdatedAt</c> is the exact-match token a claim compares, so an edit stamps it from the store's statement
    /// clock, never from the node's clock. A node whose clock runs an hour slow must still store a current stamp, and
    /// a claim that read the row after the edit must win.
    /// </summary>
    public virtual async Task update_stamps_the_store_clock_and_a_claim_after_it_succeeds()
    {
        var ct = AbortToken;
        await Fixture.ResetDatabaseAsync(ct);

        using var host = Fixture.BuildHost("node-skew", timeProvider: new SkewedTimeProvider(TimeSpan.FromHours(-1)));
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var id = Guid.NewGuid();
            await Fixture.SeedTimeJobAsync(id, "Guarded", (int)JobStatus.Idle, ownerId: null, ct);

            var copy = (await persistence.GetTimeJobByIdAsync(id, ct))!;
            copy.Description = "edited";
            // Due now, so the claim below can take it once the edit is in.
            copy.ExecutionTime = DateTime.UtcNow;
            _ShouldSucceed(await manager.UpdateAsync(copy, ct));

            var (_, updatedAt) = await Fixture.ReadTimeJobClaimTimestampsAsync(id, ct);
            updatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));

            var reread = (await persistence.GetTimeJobByIdAsync(id, ct))!;
            (await persistence.QueueTimeJobsAsync([reread], ct).ToListAsync(ct)).Should().ContainSingle();
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    private static void _ShouldSucceed<T>(JobResult<T> result)
        where T : class
    {
        result.IsSucceeded.Should().BeTrue(result.Exception?.ToString());
    }

    private async Task _StampTimeJobAsync(Guid id, JobStatus status, string? ownerId, CancellationToken ct)
    {
        await using var connection = Fixture.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = Fixture.Sql(
            $"UPDATE {Fixture.QualifiedTimeJobsTable} SET \"Status\" = @status, \"OwnerId\" = @ownerId WHERE \"Id\" = @id;"
        );
        // Status persists as its enum name.
        JobsCoordinationFixtureExtensions.AddParameter(command, "@status", status.ToString());
        JobsCoordinationFixtureExtensions.AddParameter(command, "@ownerId", (object?)ownerId ?? DBNull.Value);
        JobsCoordinationFixtureExtensions.AddParameter(command, "@id", id);
        (await command.ExecuteNonQueryAsync(ct)).Should().Be(1);
    }

    private static (
        JobStatus Status,
        string? OwnerId,
        DateTime? LockedUntil,
        int RetryCount,
        string? Description,
        DateTime? ExecutionTime,
        DateTimeOffset UpdatedAt
    ) _GuardedSnapshot(TimeJobEntity job) =>
        (job.Status, job.OwnerId, job.LockedUntil, job.RetryCount, job.Description, job.ExecutionTime, job.UpdatedAt);
}
