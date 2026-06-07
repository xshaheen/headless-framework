// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// R1: a durable Jobs node stamps work with its <c>node@incarnation</c> coordination identity, and that owner
/// string lands verbatim in the <c>LockHolder</c> column through real SQL.
/// </summary>
[Collection<JobsCoordinationFixture>]
public sealed class OwnerStampTests(JobsCoordinationFixture fixture)
{
    [Fact]
    public async Task queued_job_is_stamped_with_the_node_incarnation_owner()
    {
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);

        using var host = fixture.BuildHost("node-a");
        await JobsCoordinationFixture.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            // The gate registers during StartingAsync, which completes before StartAsync returns.
            var membership = host.Services.GetRequiredService<INodeMembership>();
            membership.Identity.Should().NotBeNull();
            var owner = membership.Identity!.Value.ToString();
            owner.Should().StartWith("node-a@");

            var jobId = Guid.NewGuid();
            await fixture.SeedTimeJobAsync(jobId, "Stamp_Sample", (int)JobStatus.Idle, lockHolder: null, ct);

            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            // Fetch the row with its current UpdatedAt (QueueTimeJobs uses optimistic concurrency on it), then stamp.
            var idle = await persistence.GetTimeJobs(x => x.Id == jobId, ct);
            idle.Should().ContainSingle();

            var stamped = await persistence.QueueTimeJobs(idle, ct).ToListAsync(ct);
            stamped.Should().ContainSingle();

            var (status, lockHolder) = await fixture.ReadTimeJobAsync(jobId, ct);
            status.Should().Be((int)JobStatus.Queued);
            lockHolder.Should().Be(owner);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }
}
