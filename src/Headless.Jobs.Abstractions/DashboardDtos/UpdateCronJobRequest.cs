// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.DashboardDtos;

/// <summary>
/// Request body for the dashboard "update cron job" endpoint. Only non-null fields are applied.
/// </summary>
public class UpdateCronJobRequest
{
    /// <summary>Registered function name to assign to the cron job.</summary>
    public string Function { get; set; } = string.Empty;

    /// <summary>Six-field NCrontab cron expression that governs scheduling.</summary>
    public string Expression { get; set; } = string.Empty;

    /// <summary>JSON-serialized request payload; <see langword="null"/> leaves the existing payload unchanged.</summary>
    public string? Request { get; set; }

    /// <summary>Maximum retry attempts; <see langword="null"/> leaves the existing value unchanged.</summary>
    public int? Retries { get; set; }

    /// <summary>Human-readable description; <see langword="null"/> leaves the existing value unchanged.</summary>
    public string? Description { get; set; }

    /// <summary>Per-attempt retry delay in seconds; <see langword="null"/> leaves the existing intervals unchanged.</summary>
    public int[]? Intervals { get; set; }
}
