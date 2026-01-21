// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Consul;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Dashboard.NodeDiscovery;

public class ConsulNodeDiscoveryProvider(ILoggerFactory logger, ConsulDiscoveryOptions options) : INodeDiscoveryProvider
{
    private readonly ILogger<ConsulNodeDiscoveryProvider> _logger = logger.CreateLogger<ConsulNodeDiscoveryProvider>();

    public async Task<Node?> GetNode(string nodeName, string? ns = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var consul = new ConsulClient(config =>
            {
                config.WaitTime = TimeSpan.FromSeconds(5);
                config.Address = new Uri($"http://{options.DiscoveryServerHostName}:{options.DiscoveryServerPort}");
            });
            var serviceCatalog = await consul.Catalog.Service(nodeName, "messaging", cancellationToken);

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
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Get consul nodes raised an exception. Exception:{ex.Message}");
        }

        return null;
    }

    public async Task<IList<Node>> GetNodes(string? ns = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var nodes = new List<Node>();

            using var consul = new ConsulClient(config =>
            {
                config.WaitTime = TimeSpan.FromSeconds(5);
                config.Address = new Uri($"http://{options.DiscoveryServerHostName}:{options.DiscoveryServerPort}");
            });

            var services = await consul.Catalog.Services(cancellationToken);

            foreach (var service in services.Response)
            {
                var serviceInfo = await consul.Catalog.Service(service.Key, "messaging", cancellationToken);

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

            MessagingCache.Global.AddOrUpdate("messaging.nodes.count", nodes.Count, TimeSpan.FromSeconds(60), true);

            return nodes;
        }
        catch (Exception ex)
        {
            MessagingCache.Global.AddOrUpdate("messaging.nodes.count", 0, TimeSpan.FromSeconds(20));

            _logger.LogError(
                $"Get consul nodes raised an exception. Exception:{ex.Message},{ex.InnerException?.Message}"
            );
            return [];
        }
    }

    public async Task RegisterNode(CancellationToken cancellationToken = default)
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
                healthCheck.HTTP =
                    $"http://{options.CurrentNodeHostName}:{options.CurrentNodePort}{options.MatchPath}/api/health";
            }
            else if (options.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                healthCheck.TCP = $"{options.CurrentNodeHostName}:{options.CurrentNodePort}";
            }

            var tags = new[] { "Headless", "Messaging", "Client", "Dashboard" };
            if (options.CustomTags is { Length: > 0 })
            {
                tags = tags.Union(options.CustomTags, StringComparer.Ordinal).ToArray();
            }

            using var consul = new ConsulClient(config =>
            {
                config.WaitTime = TimeSpan.FromSeconds(5);
                config.Address = new Uri($"http://{options.DiscoveryServerHostName}:{options.DiscoveryServerPort}");
            });

            var result = await consul.Agent.ServiceRegister(
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
            );

            if (result.StatusCode == HttpStatusCode.OK)
            {
                _logger.LogInformation("Consul node register success!");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                $"Get consul nodes raised an exception. Exception:{ex.Message},{ex.InnerException?.Message}"
            );
        }
    }
}
