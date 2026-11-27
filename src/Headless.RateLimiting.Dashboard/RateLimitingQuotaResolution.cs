// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.RateLimiting;

/// <summary>The quota to read with, or why there is none.</summary>
internal readonly record struct RateLimitingQuotaResolution(AttemptQuota? Quota, bool Declared, string? Refusal)
{
    public static RateLimitingQuotaResolution Refused(string refusal) =>
        new(Quota: null, Declared: false, Refusal: refusal);
}
