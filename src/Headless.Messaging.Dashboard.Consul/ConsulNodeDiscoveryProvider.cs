// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Consul;
using Microsoft.Extensions.Logging;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Headless.Messaging.Dashboard.Consul;

/// <summary>
/// Consul-backed implementation of <see cref="INodeDiscoveryProvider"/>.
/// Queries the Consul catalog for services tagged <c>messaging</c> to discover peer dashboard nodes,
/// and registers the current node so peers can discover it.
/// </summary>
public sealed class ConsulNodeDiscoveryProvider : INodeDiscoveryProvider, IDisposable
{
    private const string _MessagingTag = "messaging";

    private readonly ILogger<ConsulNodeDiscoveryProvider> _logger;
    private readonly MessagingDashboardCache _cache;
    private readonly ConsulDiscoveryOptions _options;

    // One client for the provider's lifetime: each ConsulClient owns an HttpClient and its handler, so a client per
    // call opens new sockets on every stats refresh and gateway lookup.
    private readonly ConsulClient _consul;

    public ConsulNodeDiscoveryProvider(
        ILoggerFactory logger,
        MessagingDashboardCache cache,
        ConsulDiscoveryOptions options
    )
        : this(logger, cache, options, _CreateClient(options)) { }

    internal ConsulNodeDiscoveryProvider(
        ILoggerFactory logger,
        MessagingDashboardCache cache,
        ConsulDiscoveryOptions options,
        ConsulClient consul
    )
    {
        _logger = logger.CreateLogger<ConsulNodeDiscoveryProvider>();
        _cache = cache;
        _options = options;
        _consul = consul;
    }

    /// <inheritdoc />
    public void Dispose() => _consul.Dispose();

    private static ConsulClient _CreateClient(ConsulDiscoveryOptions options)
    {
        return new ConsulClient(config =>
        {
            config.WaitTime = TimeSpan.FromSeconds(5);
            config.Address = new Uri(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"http://{options.DiscoveryServerHostName}:{options.DiscoveryServerPort}"
                )
            );
        });
    }

    public async Task<Node?> GetNodeAsync(
        string nodeName,
        string? ns = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var serviceCatalog = await _consul
                .Catalog.Service(nodeName, _MessagingTag, cancellationToken)
                .ConfigureAwait(false);

            if (serviceCatalog.StatusCode == HttpStatusCode.OK)
            {
                return serviceCatalog
                    .Response.Select(info => new Node
                    {
                        Id = info.ServiceID,
                        Name = info.ServiceName,
                        Address = info.ServiceAddress,
                        Port = info.ServicePort,
                        Tags = string.Join(", ", info.ServiceTags),
                    })
                    .FirstOrDefault();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            _logger.LogConsulGetNodeException(e, e.Message);
        }

        return null;
    }

    public async Task<IList<Node>> GetNodesAsync(string? ns = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var nodes = new List<Node>();

            var services = await _consul.Catalog.Services(cancellationToken).ConfigureAwait(false);

            foreach (var service in services.Response)
            {
                // The catalog listing already carries each service's tags, so a service without the messaging tag
                // is skipped here instead of costing one round trip that returns nothing. Consul's own tag filter
                // ignores case, and nodes register the tag as "Messaging", so this match ignores case too.
                if (!service.Value.Contains(_MessagingTag, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var serviceInfo = await _consul
                    .Catalog.Service(service.Key, _MessagingTag, cancellationToken)
                    .ConfigureAwait(false);

                var node = serviceInfo
                    .Response.Select(info => new Node
                    {
                        Id = info.ServiceID,
                        Name = info.ServiceName,
                        Address = "http://" + info.ServiceAddress,
                        Port = info.ServicePort,
                        Tags = string.Join(", ", info.ServiceTags),
                    })
                    .ToList();

                nodes.AddRange(node);
            }

            _cache.Set("messaging.nodes.count", nodes.Count, TimeSpan.FromSeconds(60));

            return nodes;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _cache.Set("messaging.nodes.count", 0, TimeSpan.FromSeconds(20));

            _logger.LogConsulGetNodesException(ex.Message, ex.InnerException?.Message);
            return [];
        }
    }

    public async Task RegisterNodeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var healthCheck = new AgentServiceCheck
            {
                DeregisterCriticalServiceAfter = TimeSpan.FromSeconds(30),
                Interval = TimeSpan.FromSeconds(10),
                Status = HealthStatus.Passing,
            };

            if (_options.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
            {
                healthCheck.HTTP = string.Create(
                    CultureInfo.InvariantCulture,
                    $"http://{_options.CurrentNodeHostName}:{_options.CurrentNodePort}{_options.MatchPath}/api/health"
                );
            }
            else if (_options.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                healthCheck.TCP = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{_options.CurrentNodeHostName}:{_options.CurrentNodePort}"
                );
            }

            var tags = new[] { "Headless", "Messaging", "Client", "Dashboard" };
            if (_options.CustomTags is { Length: > 0 })
            {
                tags = [.. tags.Union(_options.CustomTags, StringComparer.Ordinal)];
            }

            var result = await _consul
                .Agent.ServiceRegister(
                    new AgentServiceRegistration
                    {
                        ID = _options.NodeId,
                        Name = _options.NodeName,
                        Address = _options.CurrentNodeHostName,
                        Port = _options.CurrentNodePort,
                        Tags = tags,
                        Check = healthCheck,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (result.StatusCode == HttpStatusCode.OK)
            {
                _logger.LogConsulNodeRegisterSuccess();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            _logger.LogConsulGetNodesException(e.Message, e.InnerException?.Message);
        }
    }
}

internal static partial class ConsulNodeDiscoveryProviderLog
{
    [LoggerMessage(
        EventId = 3,
        EventName = "ConsulGetNodeException",
        Level = LogLevel.Error,
        Message = "Consul get node failed: {Message}"
    )]
    internal static partial void LogConsulGetNodeException(this ILogger logger, Exception exception, string message);

    [LoggerMessage(
        EventId = 4,
        EventName = "ConsulGetNodesException",
        Level = LogLevel.Error,
        Message = "Consul get nodes failed: {Message}, inner: {InnerExceptionMessage}"
    )]
    internal static partial void LogConsulGetNodesException(
        this ILogger logger,
        string? message,
        string? innerExceptionMessage
    );

    [LoggerMessage(
        EventId = 5,
        EventName = "ConsulNodeRegisterSuccess",
        Level = LogLevel.Information,
        Message = "Consul node registered successfully"
    )]
    internal static partial void LogConsulNodeRegisterSuccess(this ILogger logger);
}
