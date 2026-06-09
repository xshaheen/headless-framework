// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Microsoft.Extensions.Logging;

namespace Headless.Coordination;

internal sealed class DefaultNodeIdProvider : INodeIdProvider
{
    private readonly CoordinationOptions _options;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ILogger<DefaultNodeIdProvider> _logger;
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly Func<string> _getHostName;

    public DefaultNodeIdProvider(
        CoordinationOptions options,
        IGuidGenerator guidGenerator,
        ILogger<DefaultNodeIdProvider> logger
    )
        : this(options, guidGenerator, logger, Environment.GetEnvironmentVariable, static () => Environment.MachineName)
    { }

    internal DefaultNodeIdProvider(
        CoordinationOptions options,
        IGuidGenerator guidGenerator,
        ILogger<DefaultNodeIdProvider> logger,
        Func<string, string?> getEnvironmentVariable,
        Func<string> getHostName
    )
    {
        _options = options;
        _guidGenerator = guidGenerator;
        _logger = logger;
        _getEnvironmentVariable = getEnvironmentVariable;
        _getHostName = getHostName;
    }

    public ValueTask<NodeId> GetNodeIdAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(_options.ConfiguredNodeId))
        {
            return ValueTask.FromResult(new NodeId(_options.ConfiguredNodeId));
        }

        var podName = _getEnvironmentVariable("POD_NAME");

        if (!string.IsNullOrWhiteSpace(podName))
        {
            var podNamespace = _getEnvironmentVariable("POD_NAMESPACE");
            var value = string.IsNullOrWhiteSpace(podNamespace) ? podName : $"{podNamespace}/{podName}";

            return ValueTask.FromResult(new NodeId(value));
        }

        var hostName = _getHostName();

        if (!string.IsNullOrWhiteSpace(hostName))
        {
            return ValueTask.FromResult(new NodeId(hostName));
        }

        var generated = $"generated-{_guidGenerator.Create():N}";
        _logger.GeneratedFallbackNodeId(generated);

        return ValueTask.FromResult(new NodeId(generated));
    }
}
