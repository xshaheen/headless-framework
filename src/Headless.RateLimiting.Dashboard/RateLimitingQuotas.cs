// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;

namespace Headless.RateLimiting;

/// <summary>
/// The quota a lookup or a reset reads with: a declared purpose always uses its declared quota, so the screen reads
/// the counter the app's flow writes; an undeclared purpose uses the quota the caller names.
/// </summary>
internal static class RateLimitingQuotas
{
    /// <summary>The longest window a caller may name, in seconds: one year.</summary>
    public const long MaxWindowSeconds = 366L * 24 * 60 * 60;

    public static RateLimitingQuotaResolution Resolve(
        AttemptLimiterOptions options,
        string purpose,
        int? limit,
        long? windowSeconds
    )
    {
        var hasQuota = limit is not null || windowSeconds is not null;

        if (options.Purposes.TryGetValue(purpose, out var declared))
        {
            if (hasQuota && (limit != declared.Limit || windowSeconds != (long)declared.Window.TotalSeconds))
            {
                return RateLimitingQuotaResolution.Refused(
                    $"The purpose is declared with {_Describe(declared)}; leave the quota out or send the declared one."
                );
            }

            return new RateLimitingQuotaResolution(declared, Declared: true, Refusal: null);
        }

        if (!hasQuota)
        {
            return RateLimitingQuotaResolution.Refused(
                $"The purpose is not declared in {nameof(AttemptLimiterOptions)}.{nameof(AttemptLimiterOptions.Purposes)}; "
                    + "send the quota the app acquires it with: { limit, windowSeconds }."
            );
        }

        // Checked here, not by AttemptQuota, whose constructor throws: a bad quota is the caller's input, not a fault.
        if (limit is not >= 1 || windowSeconds is not (>= 1 and <= MaxWindowSeconds))
        {
            return RateLimitingQuotaResolution.Refused(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A custom quota needs a limit of at least 1 and a window of 1 to {MaxWindowSeconds} seconds."
                )
            );
        }

        return new RateLimitingQuotaResolution(
            new AttemptQuota(limit.Value, TimeSpan.FromSeconds(windowSeconds.Value)),
            Declared: false,
            Refusal: null
        );
    }

    private static string _Describe(AttemptQuota quota) =>
        string.Create(CultureInfo.InvariantCulture, $"{quota.Limit} per {(long)quota.Window.TotalSeconds} seconds");
}
