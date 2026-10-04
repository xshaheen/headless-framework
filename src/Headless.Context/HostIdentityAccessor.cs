// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.Context;

/// <summary>Provides the default implementation of <see cref="IHostIdentityAccessor"/>.</summary>
public sealed class HostIdentityAccessor : IHostIdentityAccessor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HostIdentityAccessor"/> class using environment discovery.
    /// </summary>
    /// <param name="options">Configuration options containing explicit overrides.</param>
    /// <param name="buildInformation">The build information accessor for resolving default application name.</param>
    /// <param name="guidGenerator">The GUID generator for fallback host naming.</param>
    /// <param name="logger">The optional logger for warning messages.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/>, <paramref name="buildInformation"/>, or <paramref name="guidGenerator"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><see cref="HostIdentityOptions.ApplicationName"/> or <see cref="HostIdentityOptions.HostName"/> is empty.</exception>
    public HostIdentityAccessor(
        HostIdentityOptions options,
        IBuildInformationAccessor buildInformation,
        IGuidGenerator guidGenerator,
        ILogger<HostIdentityAccessor>? logger = null
    )
        : this(
            options,
            buildInformation,
            guidGenerator,
            logger,
            Environment.GetEnvironmentVariable,
            static () => Environment.MachineName
        ) { }

    internal HostIdentityAccessor(
        HostIdentityOptions options,
        IBuildInformationAccessor buildInformation,
        IGuidGenerator guidGenerator,
        ILogger<HostIdentityAccessor>? logger,
        Func<string, string?> getEnvironmentVariable,
        Func<string> getMachineName
    )
    {
        Argument.IsNotNull(options);
        Argument.IsNotNull(buildInformation);
        Argument.IsNotNull(guidGenerator);

        if (options.ApplicationName is not null)
        {
            Argument.IsNotNullOrWhiteSpace(options.ApplicationName);
        }

        if (options.HostName is not null)
        {
            Argument.IsNotNullOrWhiteSpace(options.HostName);
        }

        ApplicationName = options.ApplicationName ?? buildInformation.GetTitle() ?? "Unknown";
        HostName =
            options.HostName
            ?? _DiscoverHostName(
                getEnvironmentVariable,
                getMachineName,
                guidGenerator,
                logger ?? NullLogger<HostIdentityAccessor>.Instance
            );
    }

    /// <inheritdoc/>
    public string ApplicationName { get; }

    /// <inheritdoc/>
    public string HostName { get; }

    private static string _DiscoverHostName(
        Func<string, string?> getEnvironmentVariable,
        Func<string> getMachineName,
        IGuidGenerator guidGenerator,
        ILogger logger
    )
    {
        // Pod name first: in Kubernetes the machine name is the container hostname, which a Deployment
        // regenerates on every rollout, while the pod name is what operators see in kubectl.
        var podName = getEnvironmentVariable("POD_NAME");

        if (!string.IsNullOrWhiteSpace(podName))
        {
            var podNamespace = getEnvironmentVariable("POD_NAMESPACE");

            return string.IsNullOrWhiteSpace(podNamespace) ? podName : $"{podNamespace}/{podName}";
        }

        var machineName = getMachineName();

        if (!string.IsNullOrWhiteSpace(machineName))
        {
            return machineName;
        }

        var generated = $"generated-{guidGenerator.Create():N}";
        logger.GeneratedFallbackHostName(generated);

        return generated;
    }
}

internal static partial class HostIdentityAccessorLogs
{
    [LoggerMessage(
        EventId = 1,
        EventName = "GeneratedFallbackHostName",
        Level = LogLevel.Warning,
        Message = "No stable host name could be discovered; using generated host name {HostName}. Every start is a brand-new host, so cluster membership cannot recognise this process returning. Set HostIdentityOptions.HostName or POD_NAME."
    )]
    public static partial void GeneratedFallbackHostName(this ILogger logger, string hostName);
}
