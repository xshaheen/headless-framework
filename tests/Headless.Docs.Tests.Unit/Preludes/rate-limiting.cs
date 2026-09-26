// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the variables the rate-limiting guide's examples assume.

global using static RateLimitingAmbient;

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class RateLimitingAmbient
{
    public static IAttemptLimiter limiter => null!;

    public static string cardId => null!;

    public static AttemptQuota quota => null!;
}
