using System.Buffers;
using Headless.Jobs.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Headless.Jobs;

internal class JobsRedisContext(
    [FromKeyedServices("jobs")] IDistributedCache cache,
    SchedulerOptionsBuilder schedulerOptions,
    ServiceExtension.JobsRedisOptionBuilder tickerQRedisOptionBuilder,
    IJobsNotificationHubSender notificationHubSender
) : IJobsRedisContext
{
    private readonly IDistributedCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly SchedulerOptionsBuilder _schedulerOptions =
        schedulerOptions ?? throw new ArgumentNullException(nameof(schedulerOptions));

    private volatile IDatabase? _database;

    private readonly string _registryKey = $"{tickerQRedisOptionBuilder.InstanceName}nodes:registry";

    public IDistributedCache DistributedCache { get; } = cache;

    public bool HasRedisConnection => true;

    public async Task NotifyNodeAliveAsync()
    {
        var node = _schedulerOptions.NodeIdentifier;
        var key = $"hb:{node}";

        var payload = new { ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), node };

        await notificationHubSender.UpdateNodeHeartBeatAsync(payload);

        var interval = tickerQRedisOptionBuilder.NodeHeartbeatInterval;
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
        var db = await _GetDatabaseAsync();

        // Get all registered nodes atomically via SMEMBERS
        var members = await db.SetMembersAsync(_registryKey);
        if (members.Length == 0)
        {
            return [];
        }

        // Check heartbeats concurrently to avoid N+1 sequential Redis reads
        var nodes = Array.ConvertAll(members, m => m.ToString());
        var heartbeatTasks = Array.ConvertAll(nodes, node => _cache.GetStringAsync($"hb:{node}"));
        var heartbeats = await Task.WhenAll(heartbeatTasks);

        var deadNodes = new List<string>();
        for (var i = 0; i < nodes.Length; i++)
        {
            if (string.IsNullOrEmpty(heartbeats[i]))
            {
                deadNodes.Add(nodes[i]);
            }
        }

        if (deadNodes.Count != 0)
        {
            await _RemoveNodesFromRegistryAsync(db, deadNodes);
        }

        return deadNodes.ToArray();
    }

    private async Task _RemoveNodesFromRegistryAsync(IDatabase db, List<string> nodes)
    {
        var values = nodes.ConvertAll(n => (RedisValue)n).ToArray();
        await db.SetRemoveAsync(_registryKey, values);
    }

    private async Task _AddNodeToRegistryAsync(string node)
    {
        var db = await _GetDatabaseAsync();
        await db.SetAddAsync(_registryKey, node);
    }

    private async Task<IDatabase> _GetDatabaseAsync()
    {
        if (_database is not null)
        {
            return _database;
        }

        IConnectionMultiplexer multiplexer;

        if (tickerQRedisOptionBuilder.ConnectionMultiplexerFactory is { } factory)
        {
            multiplexer = await factory();
        }
        else
        {
            var configOptions =
                tickerQRedisOptionBuilder.ConfigurationOptions
                ?? ConfigurationOptions.Parse(
                    tickerQRedisOptionBuilder.Configuration
                        ?? throw new InvalidOperationException(
                            "Redis connection is not configured. Provide ConnectionMultiplexerFactory, ConfigurationOptions, or Configuration."
                        )
                );

            multiplexer = await ConnectionMultiplexer.ConnectAsync(configOptions);
        }

        _database = multiplexer.GetDatabase();
        return _database;
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
#pragma warning disable ERP022, RCS1075
        // ERP022/RCS1075: Cache failures are expected and should not affect business logic.
        // Fall back to factory when cache is unavailable.
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

        return result;
    }
}
