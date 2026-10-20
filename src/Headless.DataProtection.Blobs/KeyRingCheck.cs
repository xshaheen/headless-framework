// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.DataProtection;

/// <summary>How a failed data-protection key-ring validation affects the host.</summary>
[PublicAPI]
public enum KeyRingCheck
{
    /// <summary>
    /// Default: the host stays unready until the validation succeeds. A failure keeps the <c>warmup</c> readiness
    /// check <c>Unhealthy</c> with an actionable message naming the <c>DataProtection</c> container and the
    /// provisioning or manager remediation, and the validation is retried until the store works. Use this so an
    /// orchestrator sends no traffic to a node whose key store is broken.
    /// </summary>
    GateReadiness = 0,

    /// <summary>
    /// The validation runs once and does not gate readiness. A failure is logged at <c>Critical</c> level and is not
    /// retried. Use this when data protection is not on the service's critical path and a later failure is acceptable.
    /// </summary>
    LogOnly = 1,
}
