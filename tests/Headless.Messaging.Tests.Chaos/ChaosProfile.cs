// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;

namespace Tests;

/// <summary>
/// The sizes and fault timings of every chaos scenario. The smoke profile, the default, runs the whole suite on one
/// database in a few minutes so a change can be checked locally; the full profile runs the sizes and timings the
/// multi-host stress issue states, for the scheduled run.
/// </summary>
/// <remarks>
/// Set <see cref="ProfileVariable"/> to <c>full</c> to choose the full profile. Any other value, or none, is smoke.
/// </remarks>
public sealed record ChaosProfile(
    string Name,
    RetryBacklogSize RetryBacklog,
    RollingRestartSize RollingRestart,
    BrownOutSize BrownOut,
    StorageFlapSize StorageFlap,
    ConnectionChurnSize ConnectionChurn,
    FanInSize FanIn,
    ShutdownBacklogSize ShutdownBacklog,
    KilledHostSize KilledHost,
    SoakSize Soak
)
{
    /// <summary>The environment variable that picks the profile: <c>smoke</c> (default) or <c>full</c>.</summary>
    public const string ProfileVariable = "HEADLESS_CHAOS_PROFILE";

    /// <summary>The environment variable that replays a soak run with the seed a failed run printed.</summary>
    public const string SeedVariable = "HEADLESS_CHAOS_SEED";

    /// <summary>The environment variable naming the directory the run writes its <c>summary.md</c> into.</summary>
    public const string SummaryDirectoryVariable = "HEADLESS_CHAOS_SUMMARY_DIR";

    public static ChaosProfile Smoke { get; } =
        new(
            "smoke",
            new RetryBacklogSize(
                Hosts: 3,
                Rows: 300,
                MinWork: TimeSpan.FromMilliseconds(5),
                MaxWork: TimeSpan.FromMilliseconds(50),
                RetryBatchSize: 50
            ),
            new RollingRestartSize(
                Hosts: 3,
                Rows: 300,
                Spread: TimeSpan.FromSeconds(30),
                RestartEvery: TimeSpan.FromSeconds(8),
                PollInterval: TimeSpan.FromSeconds(3)
            ),
            new BrownOutSize(Messages: 40, Outage: TimeSpan.FromSeconds(15)),
            new StorageFlapSize(
                MessagesPerSecond: 40,
                Duration: TimeSpan.FromSeconds(40),
                Down: TimeSpan.FromSeconds(2),
                Every: TimeSpan.FromSeconds(10),
                DeliveryLimit: 5
            ),
            new ConnectionChurnSize(
                MessagesPerSecond: 10,
                Duration: TimeSpan.FromSeconds(50),
                DropEvery: TimeSpan.FromSeconds(15)
            ),
            new FanInSize(Hosts: 2, ClientsPerHost: 2, Copies: 8, Rounds: 3),
            new ShutdownBacklogSize(
                Messages: 400,
                StopBudget: TimeSpan.FromSeconds(5),
                MinWork: TimeSpan.FromMilliseconds(50),
                MaxWork: TimeSpan.FromMilliseconds(150)
            ),
            new KilledHostSize(Survivors: 2, Messages: 30),
            new SoakSize(Hosts: 3, MessagesPerSecond: 20, Duration: TimeSpan.FromSeconds(45), Faults: 4)
        );

    public static ChaosProfile Full { get; } =
        new(
            "full",
            new RetryBacklogSize(
                Hosts: 3,
                Rows: 10_000,
                MinWork: TimeSpan.FromMilliseconds(50),
                MaxWork: TimeSpan.FromMilliseconds(500),
                RetryBatchSize: 200
            ),
            new RollingRestartSize(
                Hosts: 3,
                Rows: 5_000,
                Spread: TimeSpan.FromMinutes(3),
                RestartEvery: TimeSpan.FromSeconds(20),
                PollInterval: TimeSpan.FromSeconds(10)
            ),
            new BrownOutSize(Messages: 300, Outage: TimeSpan.FromSeconds(60)),
            new StorageFlapSize(
                MessagesPerSecond: 1_000,
                Duration: TimeSpan.FromMinutes(5),
                Down: TimeSpan.FromSeconds(2),
                Every: TimeSpan.FromSeconds(20),
                DeliveryLimit: 5
            ),
            new ConnectionChurnSize(
                MessagesPerSecond: 20,
                Duration: TimeSpan.FromMinutes(10),
                DropEvery: TimeSpan.FromSeconds(30)
            ),
            new FanInSize(Hosts: 3, ClientsPerHost: 4, Copies: 32, Rounds: 20),
            new ShutdownBacklogSize(
                Messages: 5_000,
                StopBudget: TimeSpan.FromSeconds(5),
                MinWork: TimeSpan.FromMilliseconds(20),
                MaxWork: TimeSpan.FromMilliseconds(100)
            ),
            new KilledHostSize(Survivors: 2, Messages: 200),
            new SoakSize(Hosts: 3, MessagesPerSecond: 50, Duration: TimeSpan.FromMinutes(15), Faults: 40)
        );

    /// <summary>The profile <see cref="ProfileVariable"/> selects.</summary>
    public static ChaosProfile Current { get; } = Select(Environment.GetEnvironmentVariable(ProfileVariable));

    public static ChaosProfile Select(string? name) =>
        string.Equals(name?.Trim(), Full.Name, StringComparison.OrdinalIgnoreCase) ? Full : Smoke;

    /// <summary>
    /// The seed <see cref="SeedVariable"/> names, or a fresh one. A soak prints the seed it ran with, so a failure can be
    /// replayed with the same fault sequence.
    /// </summary>
    public static int ResolveSeed(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed)
            ? seed
            : Random.Shared.Next();
}

/// <summary>Retry backlog across hosts: how many hosts drain how many due rows, and how long each handler runs.</summary>
public sealed record RetryBacklogSize(int Hosts, int Rows, TimeSpan MinWork, TimeSpan MaxWork, int RetryBatchSize);

/// <summary>Delayed rows during rolling restarts.</summary>
public sealed record RollingRestartSize(
    int Hosts,
    int Rows,
    TimeSpan Spread,
    TimeSpan RestartEvery,
    TimeSpan PollInterval
);

/// <summary>Broker brown-out: how many messages are published while the broker stops answering, and for how long.</summary>
public sealed record BrownOutSize(int Messages, TimeSpan Outage);

/// <summary>Storage flap under load.</summary>
public sealed record StorageFlapSize(
    int MessagesPerSecond,
    TimeSpan Duration,
    TimeSpan Down,
    TimeSpan Every,
    int DeliveryLimit
);

/// <summary>Connection churn: a steady load while the broker link is dropped on a fixed cadence.</summary>
public sealed record ConnectionChurnSize(int MessagesPerSecond, TimeSpan Duration, TimeSpan DropEvery);

/// <summary>One message to many consumers: copies of one message ID reach clients spread over hosts.</summary>
public sealed record FanInSize(int Hosts, int ClientsPerHost, int Copies, int Rounds);

/// <summary>Shutdown under backlog.</summary>
public sealed record ShutdownBacklogSize(int Messages, TimeSpan StopBudget, TimeSpan MinWork, TimeSpan MaxWork);

/// <summary>A host killed with deliveries in flight, beside the hosts that must take its work over.</summary>
public sealed record KilledHostSize(int Survivors, int Messages);

/// <summary>Seeded soak: a steady load while faults picked from the seed hit the stack.</summary>
public sealed record SoakSize(int Hosts, int MessagesPerSecond, TimeSpan Duration, int Faults);
