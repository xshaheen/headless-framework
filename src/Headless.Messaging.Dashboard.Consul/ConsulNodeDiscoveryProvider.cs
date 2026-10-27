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
public sealed class ConsulNodeDiscoveryProvider(
    ILoggerFactory logger,
    MessagingDashboardCache cache,
    ConsulDiscoveryOptions options
) : INodeDiscoveryProvider
{
    private readonly ILogger<ConsulNodeDiscoveryProvider> _logger = logger.CreateLogger<ConsulNodeDiscoveryProvider>();

    public async Task<Node?> GetNodeAsync(
        string nodeName,
        string? ns = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            using var consul = new ConsulClient(config =>
            {
                config.WaitTime = TimeSpan.FromSeconds(5);
                config.Address = new Uri(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"http://{options.DiscoveryServerHostName}:{options.DiscoveryServerPort}"
                    )
                );
            });
            var serviceCatalog = await consul
                .Catalog.Service(nodeName, "messaging", cancellationToken)
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

            using var consul = new ConsulClient(config =>
            {
                config.WaitTime = TimeSpan.FromSeconds(5);
                config.Address = new Uri(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"http://{options.DiscoveryServerHostName}:{options.DiscoveryServerPort}"
                    )
                );
            });

            var services = await consul.Catalog.Services(cancellationToken).ConfigureAwait(false);

            foreach (var service in services.Response)
            {
                var serviceInfo = await consul
                    .Catalog.Service(service.Key, "messaging", cancellationToken)
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

            cache.Set("messaging.nodes.count", nodes.Count, TimeSpan.FromSeconds(60));

            return nodes;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            cache.Set("messaging.nodes.count", 0, TimeSpan.FromSeconds(20));

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

            if (options.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
            {
                healthCheck.HTTP = string.Create(
                    CultureInfo.InvariantCulture,
                    $"http://{options.CurrentNodeHostName}:{options.CurrentNodePort}{options.MatchPath}/api/health"
                );
            }
            else if (options.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                healthCheck.TCP = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{options.CurrentNodeHostName}:{options.CurrentNodePort}"
                );
            }

            var tags = new[] { "Headless", "Messaging", "Client", "Dashboard" };
            if (options.CustomTags is { Length: > 0 })
            {
                tags = [.. tags.Union(options.CustomTags, StringComparer.Ordinal)];
            }

            using var consul = new ConsulClient(config =>
            {
                config.WaitTime = TimeSpan.FromSeconds(5);
                config.Address = new Uri(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"http://{options.DiscoveryServerHostName}:{options.DiscoveryServerPort}"
                    )
                );
            });

            var result = await consul
                .Agent.ServiceRegister(
                    new AgentServiceRegistration
                    {
                        ID = options.NodeId,
                        Name = options.NodeName,
                        Address = options.CurrentNodeHostName,
                        Port = options.CurrentNodePort,
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
