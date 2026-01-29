using System.Buffers;
using Headless.Ticker.DependencyInjection;
using Headless.Ticker.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Ticker;

internal class TickerQRedisContext : ITickerQRedisContext
{
    private readonly IDistributedCache _cache;
    private readonly SchedulerOptionsBuilder _schedulerOptions;
    private readonly ServiceExtension.TickerQRedisOptionBuilder _tickerQRedisOptionBuilder;
    private readonly ITickerQNotificationHubSender _notificationHubSender;
    public IDistributedCache DistributedCache { get; }
    public bool HasRedisConnection => true;

    public TickerQRedisContext(
        [FromKeyedServices("tickerq")] IDistributedCache cache,
        SchedulerOptionsBuilder schedulerOptions,
        ServiceExtension.TickerQRedisOptionBuilder tickerQRedisOptionBuilder,
        ITickerQNotificationHubSender notificationHubSender
    )
    {
        DistributedCache = cache;
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _schedulerOptions = schedulerOptions ?? throw new ArgumentNullException(nameof(schedulerOptions));
        _tickerQRedisOptionBuilder = tickerQRedisOptionBuilder;
        _notificationHubSender = notificationHubSender;
    }

    public async Task NotifyNodeAliveAsync()
    {
        var node = _schedulerOptions.NodeIdentifier;
        var key = $"hb:{node}";

        var payload = new { ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), node };

        await _notificationHubSender.UpdateNodeHeartBeatAsync(payload);

        var interval = _tickerQRedisOptionBuilder.NodeHeartbeatInterval;
        var ttl = TimeSpan.FromSeconds(interval.TotalSeconds + 20);

        await _cache.SetStringAsync(
            key,
            JsonSerializer.Serialize(payload),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }
        );

        await _AddNodeToRegistryAsync(node);
    }

    public async Task<string[]> GetDeadNodesAsync()
    {
        // Get all registered nodes
        var nodesJson = await _cache.GetStringAsync("nodes:registry");
        if (string.IsNullOrEmpty(nodesJson))
        {
            return [];
        }

        var allNodes = JsonSerializer.Deserialize<HashSet<string>>(nodesJson) ?? [];
        var deadNodes = new HashSet<string>(StringComparer.Ordinal);

        // Check which ones are dead
        foreach (var node in allNodes)
        {
            var heartbeat = await _cache.GetStringAsync($"hb:{node}");
            if (string.IsNullOrEmpty(heartbeat))
            {
                deadNodes.Add(node);
            }
        }

        if (deadNodes.Count != 0)
        {
            await _RemoveNodesFromRegistryAsync(deadNodes);
        }

        //if(deadNodes.Count != 0)
        //Todo notification
        return deadNodes.ToArray();
    }

    private async Task _RemoveNodesFromRegistryAsync(HashSet<string> nodes)
    {
        var nodesJson = await _cache.GetStringAsync("nodes:registry");

        var nodesList = string.IsNullOrEmpty(nodesJson)
            ? []
            : JsonSerializer.Deserialize<HashSet<string>>(nodesJson) ?? [];

        nodesList.RemoveWhere(nodes.Contains);

        await _cache.SetStringAsync(
            "nodes:registry",
            JsonSerializer.Serialize(nodesList),
            new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromDays(30) }
        );
    }

    private async Task _AddNodeToRegistryAsync(string node)
    {
        var nodesJson = await _cache.GetStringAsync("nodes:registry");

        var nodesList = string.IsNullOrEmpty(nodesJson)
            ? []
            : JsonSerializer.Deserialize<HashSet<string>>(nodesJson) ?? [];

        if (nodesList.Add(node))
        {
            await _cache.SetStringAsync(
                "nodes:registry",
                JsonSerializer.Serialize(nodesList),
                new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromDays(30) }
            );
        }
    }

    public async Task<TResult[]?> GetOrSetArrayAsync<TResult>(
        string cacheKey,
        Func<CancellationToken, Task<TResult[]?>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    )
        where TResult : class
    {
        try
        {
            var cachedBytes = await _cache.GetAsync(cacheKey, cancellationToken);
            if (cachedBytes?.Length > 0)
            {
                ReadOnlySpan<byte> cachedSpan = cachedBytes.AsSpan();
                var cached = JsonSerializer.Deserialize<TResult[]>(cachedSpan);

                if (cached != null)
                {
                    return cached;
                }
            }
        }
        // ERP022/RCS1075: Cache failures are expected and should not affect business logic.
        // Fall back to factory when cache is unavailable.
#pragma warning disable ERP022, RCS1075
        catch (Exception)
        {
            // Cache miss or failure - continue with factory
        }
#pragma warning restore ERP022, RCS1075

        var result = await factory(cancellationToken);

        if (result == null)
        {
            return null;
        }

        try
        {
            var bufferWriter = new ArrayBufferWriter<byte>();
            await using var writer = new Utf8JsonWriter(bufferWriter);

            JsonSerializer.Serialize(writer, result);
            await writer.FlushAsync(cancellationToken);

            await _cache.SetAsync(cacheKey, bufferWriter.WrittenMemory.ToArray(), cancellationToken);
        }
        // ERP022/RCS1075: Cache set failures should not affect the result returned to caller.
#pragma warning disable ERP022, RCS1075
        catch (Exception)
        {
            // Cache set failure - result already computed, just can't cache it
        }
#pragma warning restore ERP022, RCS1075

        return null;
    }
}
