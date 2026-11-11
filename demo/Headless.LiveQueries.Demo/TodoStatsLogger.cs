// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.LiveQueries;
using Mediator;

namespace Demo;

/// <summary>Watches a query from server code: the subscription a browser gets, as an async sequence.</summary>
public sealed partial class TodoStatsLogger(ILiveQueries live, ILogger<TodoStatsLogger> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var stats = live.WatchAsync(
            async (services, token) =>
                (await services.GetRequiredService<ISender>().Send(new GetTodoStats(), token)).Value,
            stoppingToken
        );

        await foreach (var snapshot in stats.WithCancellation(stoppingToken))
        {
            _LogStats(logger, snapshot.Value.Done, snapshot.Value.Total);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Todo stats: {Done} of {Total} done")]
    private static partial void _LogStats(ILogger logger, int done, int total);
}
