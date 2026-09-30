// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;

namespace Headless.Jobs;

/// <summary>
/// Storage-layer configuration shared by every Jobs database provider. The feature owns the naming so a single
/// setting moves every Jobs table at once; a provider package never declares a schema of its own, which is what
/// previously let one registration path honor an override while another silently kept the default.
/// </summary>
/// <remarks>Configure through <c>JobsOptionsBuilder.ConfigureStorage</c> inside the <c>AddHeadlessJobs</c> callback.</remarks>
[PublicAPI]
public sealed class JobsStorageOptions
{
    /// <summary>
    /// Gets or sets the database schema that contains every Jobs table — the time-job, cron-job, cron-occurrence,
    /// and idempotency-reservation tables alike. Default: <see cref="HeadlessStorageDefaults.Schema"/>
    /// (<c>"headless"</c>), the schema every Headless feature shares.
    /// </summary>
    public string Schema { get; set; } = HeadlessStorageDefaults.Schema;
}
