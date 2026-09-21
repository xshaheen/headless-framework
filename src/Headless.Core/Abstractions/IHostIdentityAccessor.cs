// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.Abstractions;

/// <summary>
/// Identifies the running process: the application it belongs to and the host it runs on. One vocabulary for
/// every subsystem that stamps an origin on shared state — cluster membership, change announcements, log
/// attribution.
/// </summary>
/// <remarks>
/// <see cref="HostName"/> is stable across restarts of the same host (a Kubernetes pod keeps its name when
/// its container restarts), so a store can recognise a returning node. There is deliberately no per-start
/// identity here: Coordination allocates one (<c>NodeIdentity</c>, <c>host@incarnation</c>) from its store on
/// top of this same host name, and a second, locally minted one would only disagree with it.
/// </remarks>
[PublicAPI]
public interface IHostIdentityAccessor
{
    /// <summary>
    /// Name of the application. Distinguishes the resources of several applications sharing one backing
    /// store, such as a cache or a definition table.
    /// </summary>
    string ApplicationName { get; }

    /// <summary>
    /// Stable name of the host this process runs on. Resolved in order from <see cref="HostIdentityOptions.HostName"/>,
    /// the <c>POD_NAMESPACE</c>/<c>POD_NAME</c> environment variables, and the machine name.
    /// </summary>
    string HostName { get; }
}

/// <summary>Overrides for <see cref="IHostIdentityAccessor"/>. Unset members are discovered.</summary>
[PublicAPI]
public sealed class HostIdentityOptions
{
    /// <summary>Application name to report instead of the entry assembly title.</summary>
    public string? ApplicationName { get; set; }

    /// <summary>
    /// Host name to report instead of the discovered one. Set it when the deployment has a stable identity the
    /// environment does not expose, such as a StatefulSet ordinal passed through configuration.
    /// </summary>
    public string? HostName { get; set; }
}

/// <summary>Default <see cref="IHostIdentityAccessor"/>.</summary>
public sealed class HostIdentityAccessor : IHostIdentityAccessor
{
    /// <summary>
    /// Creates the accessor from the entry assembly, the process environment, and the machine name.
    /// </summary>
    /// <param name="options">Explicit overrides; every unset member is discovered.</param>
    /// <param name="buildInformation">Source of the entry assembly title used as the default application name.</param>
    /// <param name="guidGenerator">Source of the last-resort generated host name.</param>
    /// <param name="logger">Receives a warning when no stable host name could be discovered.</param>
    /// <exception cref="ArgumentException">An option is set to an empty or whitespace value.</exception>
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
