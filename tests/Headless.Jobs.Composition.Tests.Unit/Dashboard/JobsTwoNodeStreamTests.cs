// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Threading.Channels;
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
    private static readonly TimeSpan _PollInterval = TimeSpan.FromSeconds(2);

    // The publisher holds a change this long to coalesce edits to one object before the stream sees it.
    private static readonly TimeSpan _CoalesceWindow = TimeSpan.FromMilliseconds(250);

    private readonly TimerSignalingTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task should_show_a_run_failed_on_node_b_in_a_stream_on_node_a_within_the_poll_interval_plus_one_second()
    {
        // given
        var reads = new ChangeReadObserver();
        await using var nodeA = await _StartAsync(sharedStore: null, reads.Wrap);
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

        // The reader takes its start cursor when the stream opens; a failure written before that would predate the
        // cursor and never be read.
        await reads.StartCursorRead.Bounded();
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

        // Each step fires one poll tick; the next waits for that tick's read to finish, so a read that missed the row
        // costs one interval of clock time and the one that finds it ends the loop.
        for (var tick = 0; tick < 5; tick++)
        {
            _time.Advance(_PollInterval);

            if ((await reads.NextPageAsync(AbortToken).Bounded()).Changes.Count > 0)
            {
                break;
            }
        }

        // The publisher flushes the read change to the stream once its coalescing timer fires.
        await _time.WaitForTimerAsync(_CoalesceWindow, AbortToken).Bounded();
        _time.Advance(_CoalesceWindow);

        // then
        var json = await change.Bounded();
        var latency = _time.GetUtcNow() - failedAt;
        json.GetProperty("projection").GetProperty("status").GetString().Should().Be("Failed");
        latency.Should().BeLessThanOrEqualTo(_PollInterval + TimeSpan.FromSeconds(1));
    }

    private async Task<WebApplication> _StartAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity>? sharedStore,
        Func<JobsDashboardBackend<TimeJobEntity, CronJobEntity>, IJobsDashboardReaderWriter>? backend = null
    )
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

        if (backend is not null)
        {
            InterceptedJobsDashboardBackend.Replace(builder.Services, backend);
        }

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

    /// <summary>
    /// Reports when the stream's change reader has taken its start cursor, and each page it reads after, so the test
    /// steps the clock one poll tick at a time instead of sleeping between steps.
    /// </summary>
    private sealed class ChangeReadObserver
    {
        private readonly TaskCompletionSource _startCursorRead = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        private readonly Channel<JobChangePage> _pages = Channel.CreateUnbounded<JobChangePage>();

        public Task StartCursorRead => _startCursorRead.Task;

        public IJobsDashboardReaderWriter Wrap(JobsDashboardBackend<TimeJobEntity, CronJobEntity> inner)
        {
            return InterceptedJobsDashboardBackend.Wrap(
                inner,
                (method, _, proceed) =>
                    method switch
                    {
                        nameof(IJobsDashboardReader.GetChangeStartCursorAsync) => _ObserveStartAsync(
                            (Task<JobChangeCursor>)proceed()!
                        ),
                        nameof(IJobsDashboardReader.ReadChangesAsync) => _ObservePageAsync(
                            (Task<JobChangePage>)proceed()!
                        ),
                        _ => proceed(),
                    }
            );
        }

        public ValueTask<JobChangePage> NextPageAsync(CancellationToken cancellationToken)
        {
            return _pages.Reader.ReadAsync(cancellationToken);
        }

        private async Task<JobChangeCursor> _ObserveStartAsync(Task<JobChangeCursor> read)
        {
            var cursor = await read;
            _startCursorRead.TrySetResult();

            return cursor;
        }

        private async Task<JobChangePage> _ObservePageAsync(Task<JobChangePage> read)
        {
            var page = await read;
            _pages.Writer.TryWrite(page);

            return page;
        }
    }
}
