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
