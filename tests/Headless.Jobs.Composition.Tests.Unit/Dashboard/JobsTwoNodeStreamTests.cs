// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Dashboard;

/// <summary>
/// Two nodes sharing one Jobs store and nothing else: a run that fails on node B reaches a stream on node A through
/// node A's own read of the store, with no broker between them.
/// </summary>
public sealed class JobsTwoNodeStreamTests : TestBase
{
    private static readonly TimeSpan _Step = TimeSpan.FromMilliseconds(50);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task should_show_a_run_failed_on_node_b_in_a_stream_on_node_a_within_the_poll_interval_plus_one_second()
    {
        // given
        await using var nodeA = await _StartAsync(sharedStore: null);
        var store = nodeA.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        await using var nodeB = await _StartAsync(store);
        var job = JobsModuleNode.IdleJob(_time.GetUtcNow());
        await store.AddTimeJobsAsync([job], AbortToken);
        var claimed = (await store.AcquireImmediateTimeJobsAsync([job.Id], AbortToken))
            .Should()
            .ContainSingle()
            .Subject;

        using var client = nodeA.GetTestClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri("/headless/api/stream?topics=jobs.run", UriKind.Relative)
        );
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, AbortToken);
        response.EnsureSuccessStatusCode();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(AbortToken));
#pragma warning disable CA2025 // False positive: the test awaits the read before the reader is disposed, and fails first if it never completes.
        var change = _NextChangeAsync(reader, job.Id, AbortToken);
#pragma warning restore CA2025

        // when: node B's executor fails the run and publishes it to node B's streams only
        var failedAt = _time.GetUtcNow();
        var failure = new JobExecutionState
        {
            FunctionName = JobsModuleNode.Charge,
            JobId = job.Id,
            Type = JobType.TimeJob,
            ClaimOwner = claimed.Lease.Holder!,
            ClaimGeneration = claimed.Lease.Generation,
        }.SetProperty(x => x.Status, JobStatus.Failed);
        (await nodeB.Services.GetRequiredService<IInternalJobManager>().UpdateTickerAsync(failure, AbortToken))
            .Should()
            .Be(1);

        var limit = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!change.IsCompleted && DateTime.UtcNow < limit)
        {
            _time.Advance(_Step);
            await Task.Delay(10, AbortToken);
        }

        // then
        change.IsCompleted.Should().BeTrue("node A's change reader should deliver the row");
        var json = await change;
        var latency = _time.GetUtcNow() - failedAt;
        json.GetProperty("projection").GetProperty("status").GetString().Should().Be("Failed");
        latency.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(2) + TimeSpan.FromSeconds(1));
    }

    private async Task<WebApplication> _StartAsync(IJobPersistenceProvider<TimeJobEntity, CronJobEntity>? sharedStore)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<TimeProvider>(_time);
        builder.Services.AddHeadlessJobs(jobs => jobs.DisableBackgroundServices().AddModule<DashboardTestJobs>());

        if (sharedStore is not null)
        {
            builder.Services.AddSingleton(sharedStore);
        }

        builder.Services.AddHeadlessDashboard(dashboard => dashboard.WithNoAuth().AddJobs());

        var app = builder.Build();
        app.MapHeadlessDashboard();
        await app.StartAsync(AbortToken);

        return app;
    }

    // Reads stream events until a change for the run arrives, and returns its data.
    private static async Task<JsonElement> _NextChangeAsync(
        StreamReader reader,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        string? type = null;
        var data = new List<string>();

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0)
            {
                if (string.Equals(type, "change", StringComparison.Ordinal) && data.Count > 0)
                {
                    using var document = JsonDocument.Parse(string.Join('\n', data));

                    if (
                        string.Equals(
                            document.RootElement.GetProperty("id").GetString(),
                            id.ToString(),
                            StringComparison.Ordinal
                        )
                    )
                    {
                        return document.RootElement.Clone();
                    }
                }

                (type, data) = (null, []);

                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                type = line["event:".Length..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                data.Add(line["data:".Length..].TrimStart(' '));
            }
        }

        throw new InvalidOperationException("The stream ended before the change.");
    }
}
