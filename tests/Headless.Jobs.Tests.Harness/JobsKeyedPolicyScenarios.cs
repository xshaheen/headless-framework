// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;

namespace Tests;

public static class JobsKeyedPolicyScenarios
{
    public static JobOptions Policy(int host) =>
        new()
        {
            Retries = host + 2,
            RetryIntervals = [host + 3, host + 7],
            OnNodeDeath = host == 0 ? NodeDeathPolicy.Skip : NodeDeathPolicy.MarkFailed,
        };

    public static void Configure<TRequest>(
        JobsOptionsBuilder<TimeJobEntity, CronJobEntity> options,
        int host,
        string source
    )
    {
        options.ConfigureDefaults(
            string.Equals(source, "host", StringComparison.Ordinal) ? Policy(host) : new JobOptions { Retries = 9 }
        );
        if (string.Equals(source, "function", StringComparison.Ordinal))
        {
            options.ConfigureJob<TRequest>(Policy(host));
        }
    }

    public static async Task RunAsync<TRequest>(
        IJobScheduler first,
        IJobScheduler second,
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        TRequest request,
        string source,
        CancellationToken cancellationToken
    )
    {
        var schedulers = new[] { first, second };
        var due = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        foreach (var winner in new[] { 0, 1 })
        {
            var key = new JobKey($"{source}-winner-{winner}");
            var created = await schedule(winner, key);
            created.Disposition.Should().Be(JobScheduleDisposition.Created);
            var before = await store.GetTimeJobByIdAsync(created.RunId!.Value, cancellationToken);
            AssertPolicy(before!, Policy(winner));
            var observed = await schedule(1 - winner, key);
            observed.Disposition.Should().Be(JobScheduleDisposition.Existing);
            observed.RunId.Should().Be(created.RunId);
            observed.Generation.Should().Be(created.Generation);
            (await store.GetTimeJobByIdAsync(created.RunId.Value, cancellationToken)).Should().BeEquivalentTo(before);

            var conflict = await schedulers[1 - winner]
                .ScheduleKeyedAsync(key, request, due.AddSeconds(1), callOptions(1 - winner), cancellationToken);
            conflict.Disposition.Should().Be(JobScheduleDisposition.Conflict);
            var replacement = await schedulers[1 - winner]
                .ReplaceKeyedAsync(
                    key,
                    created.Generation!.Value,
                    request,
                    due,
                    callOptions(1 - winner),
                    cancellationToken
                );
            replacement.Disposition.Should().Be(JobScheduleDisposition.Replaced);
            replacement.Generation.Should().Be(2);
            var replaced = await store.GetTimeJobByIdAsync(replacement.RunId!.Value, cancellationToken);
            AssertPolicy(replaced!, Policy(1 - winner));
            replaced!.FingerprintAlgorithm.Should().Be("v2");
        }

        var raceKey = new JobKey($"{source}-race");
        var race = await Task.WhenAll(
            Task.Run(() => schedule(0, raceKey), cancellationToken),
            Task.Run(() => schedule(1, raceKey), cancellationToken)
        );
        race.Count(result => result.Disposition == JobScheduleDisposition.Created).Should().Be(1);
        race.Count(result => result.Disposition == JobScheduleDisposition.Existing).Should().Be(1);
        race.Select(result => result.RunId).Distinct().Should().ContainSingle();
        var winnerIndex = Array.FindIndex(race, result => result.Disposition == JobScheduleDisposition.Created);
        AssertPolicy((await store.GetTimeJobByIdAsync(race[0].RunId!.Value, cancellationToken))!, Policy(winnerIndex));

        Task<JobScheduleResult> schedule(int host, JobKey key) =>
            schedulers[host].ScheduleKeyedAsync(key, request, due, callOptions(host), cancellationToken);
        JobOptions? callOptions(int host) =>
            string.Equals(source, "call", StringComparison.Ordinal) ? Policy(host) : null;
    }

    public static void AssertPolicy(TimeJobEntity row, JobOptions policy)
    {
        row.Retries.Should().Be(policy.Retries);
        row.RetryIntervals.Should().Equal(policy.RetryIntervals!);
        row.OnNodeDeath.Should().Be(policy.OnNodeDeath);
    }
}
