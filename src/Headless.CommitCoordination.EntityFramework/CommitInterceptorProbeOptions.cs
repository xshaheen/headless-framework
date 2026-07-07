// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination.EntityFramework;

/// <summary>
/// Options for the startup commit-interceptor self-probe that verifies the
/// <see cref="CommitCoordinationTransactionInterceptor" /> is actually firing for a registered
/// <c>DbContext</c>.
/// </summary>
/// <remarks>
/// The default mode is <see cref="CommitProbeMode.Warn" />: a mis-wire logs loudly but does not
/// block startup, because the durable outbox row plus the relay sweep recover the work regardless — the
/// interceptor is a dispatch accelerator, not the durability mechanism. Opt into
/// <see cref="CommitProbeMode.Strict" /> to fail startup when the interceptor is not firing.
/// </remarks>
[PublicAPI]
public sealed class CommitInterceptorProbeOptions
{
    /// <summary>
    /// Gets or sets the probe mode. Defaults to <see cref="CommitProbeMode.Warn" />.
    /// </summary>
    public CommitProbeMode Mode { get; set; } = CommitProbeMode.Warn;
}
