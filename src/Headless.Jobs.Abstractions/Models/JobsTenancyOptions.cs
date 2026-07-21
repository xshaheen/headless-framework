// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Models;

/// <summary>
/// Tenancy behavior for Jobs. Defaults keep ambient capture and strict enforcement off; the
/// <c>HeadlessTenancyBuilder.Jobs(...)</c> seam flips these flags. Structural validation of explicitly supplied
/// tenant fields (length, blank, cron scope, system-job contradictions) always runs regardless of these options.
/// </summary>
[PublicAPI]
public sealed class JobsTenancyOptions
{
    /// <summary>Maximum accepted <c>TenantId</c> length. Mirrors the Messaging bound.</summary>
    public const int TenantIdMaxLength = 200;

    /// <summary>
    /// Capture the ambient tenant onto time jobs at schedule time when no explicit value is supplied.
    /// </summary>
    public bool PropagateTenant { get; set; }

    /// <summary>
    /// Reject a time-job enqueue that resolves no explicit or ambient tenant unless the job is a system job.
    /// </summary>
    public bool TenantContextRequired { get; set; }
}
