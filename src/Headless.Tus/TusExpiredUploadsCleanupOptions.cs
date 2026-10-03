// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using tusdotnet.Interfaces;

namespace Headless.Tus;

/// <summary>Options for <see cref="TusExpiredUploadsCleanupService"/>.</summary>
[PublicAPI]
public sealed class TusExpiredUploadsCleanupOptions
{
    /// <summary>How often expired incomplete uploads are removed.</summary>
    /// <remarks>
    /// Defaults to 5 minutes. Each pass calls
    /// <see cref="ITusExpirationStore.RemoveExpiredFilesAsync"/>, which typically scans the
    /// store's uploads (a prefix listing for the Azure store) — prefer a coarser interval on
    /// containers with many uploads; the expiration window itself is configured on
    /// <c>DefaultTusConfiguration.Expiration</c>. In multi-node deployments every node runs its
    /// own loop against the same store: deletions are idempotent so this is safe, but the scan
    /// load multiplies and the logged removal counts are per-node — wrap the service in a
    /// distributed-lock single-flight guard if that matters.
    /// </remarks>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);
}

internal sealed class TusExpiredUploadsCleanupOptionsValidator : AbstractValidator<TusExpiredUploadsCleanupOptions>
{
    public TusExpiredUploadsCleanupOptionsValidator()
    {
        RuleFor(x => x.Interval).GreaterThan(TimeSpan.Zero);
    }
}
