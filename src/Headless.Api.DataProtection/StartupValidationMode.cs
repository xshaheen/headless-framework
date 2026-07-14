// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.DataProtection;

/// <summary>How data-protection startup validation failures are surfaced to the host.</summary>
[PublicAPI]
public enum StartupValidationMode
{
    /// <summary>
    /// Default: a validation failure throws from the hosted service's <c>StartAsync</c>, failing host startup with an
    /// actionable message naming the <c>DataProtection</c> container and the provisioning/manager remediation. Use
    /// this to make a misconfigured key store block the deployment instead of the first (lazy) key write.
    /// </summary>
    Throw = 0,

    /// <summary>
    /// A validation failure is logged at <c>Critical</c> level and startup continues. Use this when data protection
    /// is not on the critical path for the service and a delayed failure is acceptable.
    /// </summary>
    LogOnly = 1,
}
