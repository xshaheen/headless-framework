// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;
using Headless.RateLimiting;

namespace Headless.Dashboards.Sandbox;

/// <summary>The attempt-limit purposes the sandbox declares, and the fixture that spends attempts against them.</summary>
public static class SandboxRateLimits
{
    public const string SignIn = "sign-in.password";

    // A fixed key is safe only because the sandbox binds to loopback and holds no real subject; an app keeps its key
    // secret, since it is what makes a fingerprint unguessable.
    public static readonly string SubjectKey = Convert.ToBase64String("headless-sandbox-subject-key-32b!"u8.ToArray());

    public static readonly IReadOnlyDictionary<string, AttemptQuota> Purposes = new Dictionary<string, AttemptQuota>(
        StringComparer.Ordinal
    )
    {
        [SignIn] = new(5, TimeSpan.FromMinutes(15)),
        ["otp.verify"] = new(3, TimeSpan.FromMinutes(10)),
        ["password-reset.request"] = new(3, TimeSpan.FromHours(1)),
    };

    /// <summary>
    /// Locks one new subject out of sign-in and spends two attempts of another. Each call makes new subjects, so it is
    /// safe to run again and never depends on a window still being open.
    /// </summary>
    /// <returns>Each subject with its purpose and the end of its window, one line each.</returns>
    public static async Task<IReadOnlyList<string>> SeedAsync(
        IAttemptLimiter limiter,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(limiter);

        var suffix = Guid.CreateVersion7().ToString("N")[^8..];
        var quota = Purposes[SignIn];
        var lockedOut = $"locked-{suffix}@example.com";
        var partly = $"partly-{suffix}@example.com";

        for (var i = 0; i < quota.Limit; i++)
        {
            await limiter.AcquireAsync(SignIn, lockedOut, quota, cancellationToken).ConfigureAwait(false);
        }

        for (var i = 0; i < 2; i++)
        {
            await limiter.AcquireAsync(SignIn, partly, quota, cancellationToken).ConfigureAwait(false);
        }

        var lockedWindow = await limiter.PeekAsync(SignIn, lockedOut, quota, cancellationToken).ConfigureAwait(false);
        var partlyWindow = await limiter.PeekAsync(SignIn, partly, quota, cancellationToken).ConfigureAwait(false);

        return
        [
            $"{lockedOut} {SignIn} locked out until {lockedWindow.WindowEndsAt:O}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"{partly} {SignIn} 2 of {quota.Limit} until {partlyWindow.WindowEndsAt:O}"
            ),
        ];
    }
}
