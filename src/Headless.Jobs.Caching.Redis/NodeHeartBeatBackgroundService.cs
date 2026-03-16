using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs;

internal sealed class NodeHeartBeatBackgroundService(
    ServiceExtension.JobsRedisOptionBuilder schedulerOptionsBuilder,
    IJobsRedisContext context,
    IInternalJobManager internalJobsManager,
    ILogger<NodeHeartBeatBackgroundService> logger
) : BackgroundService
{
    private int _started;
    private readonly PeriodicTimer _tickerHeartBeatPeriodicTimer = new(schedulerOptionsBuilder.NodeHeartbeatInterval);

    public override Task StartAsync(CancellationToken ct)
    {
        return Interlocked.CompareExchange(ref _started, 1, 0) != 0 ? Task.CompletedTask : base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _RunJobsFallbackAsync(stoppingToken);
        }
        catch (Exception e)
        {
            logger.LogError("Heartbeat background service failed: {Exception}", e);
        }
    }

    private async Task _RunJobsFallbackAsync(CancellationToken stoppingToken)
    {
        await context.NotifyNodeAliveAsync();

        while (await _tickerHeartBeatPeriodicTimer.WaitForNextTickAsync(stoppingToken))
        {
            var deadNodes = await context.GetDeadNodesAsync();

            if (deadNodes.Length != 0)
            {
                foreach (var deadNode in deadNodes)
                {
                    await internalJobsManager.ReleaseDeadNodeResources(deadNode, stoppingToken);
                }
            }

            await context.NotifyNodeAliveAsync();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _started, 0);
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _tickerHeartBeatPeriodicTimer.Dispose();
        base.Dispose();
    }
}
