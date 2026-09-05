// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;

namespace Tests;

/// <summary>The same durable operation matrix runs against memory, PostgreSQL, and SQL Server.</summary>
public static class JobsKeyedSchedulingScenarios
{
    public static async Task RunAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        CancellationToken cancellationToken
    )
    {
        var key = new JobKey("invoice-42");
        var scope = new JobKeyScope("deadline");
        var creates = await Task.WhenAll(
            Enumerable
                .Range(0, 12)
                .Select(_ => store.ScheduleKeyedTimeJobAsync(key, Candidate(), cancellationToken: cancellationToken))
        );
        creates.Count(result => result.Disposition == JobScheduleDisposition.Created).Should().Be(1);
        creates.Count(result => result.Disposition == JobScheduleDisposition.Existing).Should().Be(11);
        creates.Select(result => result.RunId).Distinct().Should().ContainSingle();
        creates.Should().OnlyContain(result => result.Generation == 1);
        var originalId = creates[0].RunId!.Value;
        var original = await store.GetTimeJobByIdAsync(originalId, cancellationToken);
        var fingerprintBeforeRead = original!.IntentFingerprint;
        var detachedBytes = await store.GetTimeJobRequestAsync(originalId, cancellationToken);
        detachedBytes[0] = 99;
        original.Request![1] = 88;
        var afterMutatingRead = await store.GetTimeJobByIdAsync(originalId, cancellationToken);
        afterMutatingRead!.Request.Should().Equal(1, 2, 3);
        afterMutatingRead.IntentFingerprint.Should().Be(fingerprintBeforeRead);
        (await store.ScheduleKeyedTimeJobAsync(key, Candidate(), cancellationToken: cancellationToken))
            .Disposition.Should()
            .Be(JobScheduleDisposition.Existing);

        var conflictingCreates = await Task.WhenAll(
            store.ScheduleKeyedTimeJobAsync(
                new JobKey("competing-intents"),
                Candidate(),
                cancellationToken: cancellationToken
            ),
            store.ScheduleKeyedTimeJobAsync(
                new JobKey("competing-intents"),
                Candidate([9]),
                cancellationToken: cancellationToken
            )
        );
        conflictingCreates.Count(result => result.Disposition == JobScheduleDisposition.Created).Should().Be(1);
        conflictingCreates.Count(result => result.Disposition == JobScheduleDisposition.Conflict).Should().Be(1);

        var different = Candidate();
        different.Request = [9];
        (await store.ScheduleKeyedTimeJobAsync(key, different, cancellationToken: cancellationToken))
            .Disposition.Should()
            .Be(JobScheduleDisposition.Conflict);

        var presentation = Candidate();
        presentation.Description = "Changed display";
        presentation.CorrelationId = "different-root";
        presentation.CausationId = "different-parent";
        (await store.ScheduleKeyedTimeJobAsync(key, presentation, cancellationToken: cancellationToken))
            .Disposition.Should()
            .Be(JobScheduleDisposition.Existing);

        var replacement = await Task.WhenAll(
            Enumerable
                .Range(0, 12)
                .Select(_ => store.ScheduleKeyedTimeJobAsync(key, Candidate([4]), 1, cancellationToken))
        );
        replacement.Count(result => result.Disposition == JobScheduleDisposition.Replaced).Should().Be(1);
        replacement.Count(result => result.Disposition == JobScheduleDisposition.StaleGeneration).Should().Be(11);
        replacement.Should().OnlyContain(result => result.Generation == 2);
        var currentId = replacement[0].RunId!.Value;
        currentId.Should().NotBe(originalId);
        var historical = await store.GetTimeJobByIdAsync(originalId, cancellationToken);
        historical!.IsCurrentGeneration.Should().BeFalse();
        historical.Generation.Should().Be(1);
        historical.Status.Should().Be(JobStatus.Skipped);
        historical.Request.Should().Equal(1, 2, 3);

        (await store.CancelKeyedTimeJobAsync(scope, key, 1, cancellationToken))
            .Disposition.Should()
            .Be(JobScheduleDisposition.StaleGeneration);
        var current = await store.GetTimeJobByIdAsync(currentId, cancellationToken);
        current!.CancelRequested.Should().BeFalse();

        var cancel = await store.CancelKeyedTimeJobAsync(scope, key, 2, cancellationToken);
        cancel.Disposition.Should().Be(JobScheduleDisposition.Cancelled);
        cancel.State.Should().Be(JobStatus.Cancelled);
        (await store.ScheduleKeyedTimeJobAsync(key, Candidate([4]), cancellationToken: cancellationToken))
            .Disposition.Should()
            .Be(JobScheduleDisposition.Existing);
        (await store.ScheduleKeyedTimeJobAsync(key, Candidate([5]), 2, cancellationToken))
            .Disposition.Should()
            .Be(JobScheduleDisposition.Conflict);
        (await store.CancelKeyedTimeJobAsync(scope, key, 2, cancellationToken))
            .Disposition.Should()
            .Be(JobScheduleDisposition.Terminal);

        var unkeyed = Candidate();
        await store.AddTimeJobsAsync([unkeyed], cancellationToken);
        var delete = async () =>
            await store.RemoveTimeJobsAsync([unkeyed.Id, originalId, currentId], cancellationToken);
        await delete.Should().ThrowAsync<InvalidOperationException>();
        (await store.GetTimeJobByIdAsync(unkeyed.Id, cancellationToken)).Should().NotBeNull();
        (await store.GetTimeJobByIdAsync(originalId, cancellationToken)).Should().NotBeNull();
        var update = Candidate([8]);
        update.Id = currentId;
        var mutate = async () => await store.UpdateTimeJobsAsync([update], cancellationToken);
        await mutate.Should().ThrowAsync<InvalidOperationException>();
        (await store.GetTimeJobByIdAsync(currentId, cancellationToken))!.Request.Should().Equal(4);
        (await store.RemoveTimeJobsAsync([unkeyed.Id], cancellationToken)).Should().Be(1);

        foreach (var tenant in new[] { "tenant-a", "tenant-b", "Tenant-A" })
        {
            var tenanted = Candidate();
            tenanted.TenantId = tenant;
            (await store.ScheduleKeyedTimeJobAsync(key, tenanted, cancellationToken: cancellationToken))
                .Disposition.Should()
                .Be(JobScheduleDisposition.Created);
        }

        var otherContract = Candidate();
        otherContract.Function = "Deadline";
        (await store.ScheduleKeyedTimeJobAsync(key, otherContract, cancellationToken: cancellationToken))
            .Disposition.Should()
            .Be(JobScheduleDisposition.Created);
        (
            await store.ScheduleKeyedTimeJobAsync(
                new JobKey("Invoice-42"),
                Candidate(),
                cancellationToken: cancellationToken
            )
        )
            .Disposition.Should()
            .Be(JobScheduleDisposition.Created);
        (await store.CancelKeyedTimeJobAsync(scope, new JobKey("missing"), 1, cancellationToken))
            .Disposition.Should()
            .Be(JobScheduleDisposition.NotFound);

        var chain = Candidate();
        chain.Children.Add(Candidate());
        var chainSchedule = async () =>
            await store.ScheduleKeyedTimeJobAsync(new JobKey("chain"), chain, cancellationToken: cancellationToken);
        await chainSchedule.Should().ThrowAsync<NotSupportedException>().WithMessage("*JobChain*");
    }

    public static async Task RunParentAttachmentRejectionsAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        ITimeJobManager<TimeJobEntity> manager,
        Action<TimeJobEntity, Guid> setMappedParent,
        CancellationToken cancellationToken
    )
    {
        var key = new JobKey("retained-parent");
        var first = await store.ScheduleKeyedTimeJobAsync(key, Candidate(), cancellationToken: cancellationToken);
        var second = await store.ScheduleKeyedTimeJobAsync(key, Candidate([4]), 1, cancellationToken);
        foreach (var parentId in new[] { first.RunId!.Value, second.RunId!.Value })
        {
            var child = Candidate();
            setMappedParent(child, parentId);
            var unrelated = Candidate();
            var add = async () => await store.AddTimeJobsAsync([unrelated, child], cancellationToken);
            await add.Should().ThrowAsync<InvalidOperationException>().WithMessage("*keyed*parent*");
            (await store.GetTimeJobByIdAsync(child.Id, cancellationToken)).Should().BeNull();
            (await store.GetTimeJobByIdAsync(unrelated.Id, cancellationToken)).Should().BeNull();

            var ordinary = Candidate();
            await store.AddTimeJobsAsync([ordinary], cancellationToken);
            var reparented = Candidate();
            reparented.Id = ordinary.Id;
            setMappedParent(reparented, parentId);
            var result = await manager.UpdateAsync(reparented, cancellationToken);
            result.IsSucceeded.Should().BeFalse();
            result.Exception.Should().BeOfType<InvalidOperationException>();
            (await store.GetTimeJobByIdAsync(ordinary.Id, cancellationToken))!.ParentId.Should().BeNull();
            (await store.GetTimeJobByIdAsync(parentId, cancellationToken))!.Children.Should().BeEmpty();
        }
    }

    public static async Task RunClaimRacesAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        Func<TimeJobEntity, Task<bool>> claim,
        CancellationToken cancellationToken
    )
    {
        var scope = new JobKeyScope("deadline");
        for (var index = 0; index < 8; index++)
        {
            var key = new JobKey("claim-replace-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var scheduled = await store.ScheduleKeyedTimeJobAsync(
                key,
                Candidate(),
                cancellationToken: cancellationToken
            );
            var stored = await store.GetTimeJobByIdAsync(scheduled.RunId!.Value, cancellationToken);
            var claimTask = Task.Run(() => claim(stored!), cancellationToken);
            var replacementTask = Task.Run(
                () => store.ScheduleKeyedTimeJobAsync(key, Candidate([9]), 1, cancellationToken),
                cancellationToken
            );
            await Task.WhenAll(claimTask, replacementTask);
            var replacement = await replacementTask;
            if (await claimTask)
            {
                replacement.Disposition.Should().Be(JobScheduleDisposition.Conflict);
                var cancel = await store.CancelKeyedTimeJobAsync(scope, key, 1, cancellationToken);
                cancel.Disposition.Should().Be(JobScheduleDisposition.CancellationRequested);
                cancel.State.Should().Be(JobStatus.Queued);
            }
            else
            {
                replacement.Disposition.Should().Be(JobScheduleDisposition.Replaced);
                (await store.GetTimeJobByIdAsync(stored!.Id, cancellationToken))!.Status.Should().Be(JobStatus.Skipped);
            }

            var cancelKey = new JobKey(
                "claim-cancel-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)
            );
            var cancelScheduled = await store.ScheduleKeyedTimeJobAsync(
                cancelKey,
                Candidate(),
                cancellationToken: cancellationToken
            );
            var cancelStored = await store.GetTimeJobByIdAsync(cancelScheduled.RunId!.Value, cancellationToken);
            var competingClaim = Task.Run(() => claim(cancelStored!), cancellationToken);
            var competingCancel = Task.Run(
                () => store.CancelKeyedTimeJobAsync(scope, cancelKey, 1, cancellationToken),
                cancellationToken
            );
            await Task.WhenAll(competingClaim, competingCancel);
            (await competingCancel)
                .Disposition.Should()
                .Be(
                    await competingClaim
                        ? JobScheduleDisposition.CancellationRequested
                        : JobScheduleDisposition.Cancelled
                );
        }
    }

    public static async Task RunLegacyMutationRacesAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        CancellationToken cancellationToken
    )
    {
        for (var index = 0; index < 8; index++)
        {
            var candidate = Candidate();
            var key = new JobKey("delete-race-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var insertion = Task.Run(
                () => store.ScheduleKeyedTimeJobAsync(key, candidate, cancellationToken: cancellationToken),
                cancellationToken
            );
            var deletion = Task.Run(
                async () =>
                {
                    try
                    {
                        await store.RemoveTimeJobsAsync([candidate.Id], cancellationToken);
                    }
                    catch (InvalidOperationException) { }
                },
                cancellationToken
            );
            await Task.WhenAll(insertion, deletion);
            (await store.GetTimeJobByIdAsync((await insertion).RunId!.Value, cancellationToken))!
                .BusinessKey.Should()
                .Be(key.Value);

            var updateCandidate = Candidate();
            var updateKey = new JobKey(
                "update-race-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)
            );
            var dto = Candidate([8]);
            dto.Id = updateCandidate.Id;
            JobScheduleResult? scheduled = null;
            var create = Task.Run(
                async () =>
                {
                    try
                    {
                        scheduled = await store.ScheduleKeyedTimeJobAsync(
                            updateKey,
                            updateCandidate,
                            cancellationToken: cancellationToken
                        );
                    }
                    catch (InvalidOperationException) { }
                },
                cancellationToken
            );
            var update = Task.Run(
                async () =>
                {
                    try
                    {
                        await store.UpdateTimeJobsAsync([dto], cancellationToken);
                    }
                    catch (InvalidOperationException) { }
                    catch (Exception exception) when (exception.GetType().Name == "DbUpdateConcurrencyException") { }
                },
                cancellationToken
            );
            await Task.WhenAll(create, update);
            if (scheduled is not null)
            {
                var retained = await store.GetTimeJobByIdAsync(scheduled.RunId!.Value, cancellationToken);
                retained!.BusinessKey.Should().Be(updateKey.Value);
                retained.Request.Should().Equal(1, 2, 3);
            }
        }
    }

    public static TimeJobEntity Candidate(byte[]? request = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = "deadline",
            ContractVersion = "1",
            ExecutionTime = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(7),
            Request = request ?? [1, 2, 3],
        };
}
