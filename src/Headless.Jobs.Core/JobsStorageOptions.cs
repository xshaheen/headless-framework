// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
    /// <summary>The schema Jobs tables are mapped into when no override is configured.</summary>
    public const string DefaultSchema = "jobs";

    /// <summary>
    /// Gets or sets the database schema that contains every Jobs table — the time-job, cron-job, cron-occurrence,
    /// and idempotency-reservation tables alike. Default: <see cref="DefaultSchema"/>.
    /// </summary>
    public string Schema { get; set; } = DefaultSchema;
}
