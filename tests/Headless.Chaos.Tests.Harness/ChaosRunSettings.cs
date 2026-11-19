// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;

namespace Tests;

/// <summary>
/// How one chaos run is sized and replayed, read once from the environment: the profile (smoke for a local check, full
/// for the scheduled run), how long a soak scenario keeps injecting faults, and the seed every random choice derives
/// from. A scenario prints <see cref="Seed"/> so a failed run replays with the same fault sequence.
/// </summary>
public sealed record ChaosRunSettings(string Profile, TimeSpan? Duration, int Seed)
{
    /// <summary>The environment variable that picks the profile: <c>smoke</c> (default) or <c>full</c>.</summary>
    public const string ProfileVariable = "HEADLESS_CHAOS_PROFILE";

    /// <summary>The environment variable that replays a run with the seed a failed run printed.</summary>
    public const string SeedVariable = "HEADLESS_CHAOS_SEED";

    /// <summary>
    /// The environment variable that overrides how long a soak scenario injects faults, as a <see cref="TimeSpan"/>
    /// (<c>00:30:00</c>) or a whole number of minutes (<c>30</c>).
    /// </summary>
    public const string DurationVariable = "HEADLESS_CHAOS_DURATION";

    /// <summary>The environment variable naming the directory the run writes its <c>summary.md</c> into.</summary>
    public const string SummaryDirectoryVariable = "HEADLESS_CHAOS_SUMMARY_DIR";

    public const string SmokeProfile = "smoke";

    public const string FullProfile = "full";

    /// <summary>The settings the environment of this process selects.</summary>
    public static ChaosRunSettings Current { get; } =
        new(
            SelectProfile(Environment.GetEnvironmentVariable(ProfileVariable)),
            ParseDuration(Environment.GetEnvironmentVariable(DurationVariable)),
            ResolveSeed(Environment.GetEnvironmentVariable(SeedVariable))
        );

    /// <summary>Whether the run uses the full sizes and timings of the scheduled run.</summary>
    public bool IsFull => string.Equals(Profile, FullProfile, StringComparison.Ordinal);

    /// <summary>Picks a size by profile: <paramref name="smoke"/> for a local check, <paramref name="full"/> otherwise.</summary>
    public T Pick<T>(T smoke, T full) => IsFull ? full : smoke;

    /// <summary>The soak length: <see cref="Duration"/> when set, otherwise the profile's default.</summary>
    public TimeSpan SoakDuration(TimeSpan smoke, TimeSpan full) => Duration ?? Pick(smoke, full);

    /// <summary>
    /// A seed for one scenario, derived from <see cref="Seed"/> and the scenario's name, so scenarios that run in one
    /// process do not share a random stream while one run seed still replays all of them.
    /// </summary>
    public int SeedFor(string scenario)
    {
        // FNV-1a over the name: string.GetHashCode is randomized per process, which would break replay.
        var hash = 2166136261u;

        foreach (var c in scenario)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return unchecked((int)hash ^ Seed);
    }

    public static string SelectProfile(string? name) =>
        string.Equals(name?.Trim(), FullProfile, StringComparison.OrdinalIgnoreCase) ? FullProfile : SmokeProfile;

    public static TimeSpan? ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) && minutes > 0)
        {
            return TimeSpan.FromMinutes(minutes);
        }

        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var duration) && duration > TimeSpan.Zero)
        {
            return duration;
        }

        throw new InvalidOperationException(
            $"{DurationVariable}='{value}' is neither a whole number of minutes nor a positive TimeSpan."
        );
    }

    public static int ResolveSeed(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed)
            ? seed
            : Random.Shared.Next();

    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"profile {Profile}, seed {Seed}{(Duration is { } d ? $", duration {d}" : string.Empty)}"
        );
}
