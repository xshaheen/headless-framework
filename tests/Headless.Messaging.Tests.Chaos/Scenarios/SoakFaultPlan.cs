// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;

namespace Tests.Scenarios;

/// <summary>A fault the soak can inject.</summary>
public enum SoakFaultKind
{
    /// <summary>Cuts every broker connection and refuses new ones for the fault's length.</summary>
    BrokerCut,

    /// <summary>Keeps the broker link open but drops everything the broker sends back for the fault's length.</summary>
    BrokerBlackhole,

    /// <summary>Cuts every database connection and refuses new ones for the fault's length.</summary>
    StorageCut,

    /// <summary>Stops one host gracefully and starts a replacement.</summary>
    HostRestart,
}

/// <summary>One fault of a soak run: when it starts, what it does, how long it lasts, and which host it hits.</summary>
public sealed record SoakFault(TimeSpan At, SoakFaultKind Kind, TimeSpan Length, int Host)
{
    public override string ToString() =>
        Kind == SoakFaultKind.HostRestart
            ? string.Create(CultureInfo.InvariantCulture, $"{At.TotalSeconds:F1}s {Kind} host {Host}")
            : string.Create(CultureInfo.InvariantCulture, $"{At.TotalSeconds:F1}s {Kind} {Length.TotalSeconds:F1}s");
}

/// <summary>
/// The faults one soak run injects, picked from its seed alone. The same seed always yields the same sequence, so a
/// failed run that printed its seed replays exactly.
/// </summary>
public sealed record SoakFaultPlan(int Seed, IReadOnlyList<SoakFault> Faults)
{
    private static readonly TimeSpan _ShortestFault = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _LongestFault = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Spreads <paramref name="faults"/> over <paramref name="duration"/>, one per equal slot so no two overlap, each
    /// starting in the first half of its slot and ending before the next begins.
    /// </summary>
    public static SoakFaultPlan Create(int seed, TimeSpan duration, int faults, int hosts)
    {
        var random = new Random(seed);
        var slot = duration / faults;
        var kinds = Enum.GetValues<SoakFaultKind>();
        var plan = new List<SoakFault>(faults);

        for (var i = 0; i < faults; i++)
        {
            var at = (slot * i) + (slot * 0.5 * random.NextDouble());
            var longest = TimeSpan.FromTicks(Math.Min(_LongestFault.Ticks, (long)(slot.Ticks * 0.4)));
            var length = _ShortestFault + ((longest - _ShortestFault) * random.NextDouble());
            var kind = kinds[random.Next(kinds.Length)];
            var host = random.Next(hosts);

            plan.Add(new SoakFault(at, kind, length < _ShortestFault ? _ShortestFault : length, host));
        }

        return new SoakFaultPlan(seed, plan);
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"seed {Seed}: ") + string.Join("; ", Faults);
}
