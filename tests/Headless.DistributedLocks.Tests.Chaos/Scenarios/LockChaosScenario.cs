// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.DistributedLocks;
using Tests.Oracle;

namespace Tests.Scenarios;

/// <summary>The outcome of one lock chaos run: the oracle's verdict, the ledger's holder metrics, and the counts.</summary>
public sealed record LockChaosResult(
    FencingContract Contract,
    FencingVerdict Verdict,
    FencingLedger Ledger,
    LockHolderWorkloadCounts Counts,
    TimeSpan Elapsed
)
{
    private static readonly Lock _ObservationsLock = new();

    /// <summary>
    /// Records the run through <see cref="ChaosReport"/> and returns the violations to assert on. Issuer breaks the
    /// contract only reports go to the test output and to <c>observations.md</c> beside the summary, with their log
    /// slice, so a pass still carries the evidence.
    /// </summary>
    public IReadOnlyList<string> Report(string store, string scenario, int seed, string faults)
    {
        List<string> violations = [.. Verdict.Violations];

        if (Verdict.AcceptedWrites == 0)
        {
            violations.Add("no write was accepted, so the run proved nothing");
        }

        ChaosReport.Record(
            store,
            scenario,
            violations,
            string.Create(
                CultureInfo.InvariantCulture,
                $"scenario seed {seed}; issuer rule {Contract.Issuer} ({Contract.Name}); {Verdict.Describe(Ledger)}; {Counts}; {faults}; wall {Elapsed.TotalSeconds:F0}s"
            ),
            Verdict.Evidence,
            line => TestContext.Current.TestOutputHelper?.WriteLine(line)
        );

        if (Verdict.Observations.Count > 0)
        {
            _Observe(store, scenario, seed);
        }

        return violations;
    }

    private void _Observe(string store, string scenario, int seed)
    {
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"## {store} · {scenario} ({ChaosRunSettings.Current}, scenario seed {seed}) — reported, not failed\n\n{string.Join('\n', Verdict.Observations)}\n\n```text\n{Verdict.ObservationEvidence}\n```\n\n"
        );
        TestContext.Current.TestOutputHelper?.WriteLine(text);

        if (Environment.GetEnvironmentVariable(ChaosRunSettings.SummaryDirectoryVariable) is not { Length: > 0 } dir)
        {
            return;
        }

        lock (_ObservationsLock)
        {
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "observations.md"), text);
        }
    }
}

/// <summary>Sizes and runs the S7 workload: K workers taking fenced locks on a few resources while faults hit.</summary>
public static class LockChaosScenario
{
    /// <summary>The workload size for the current profile: about 8 workers on 3 resources for smoke, 32 on 4 for full.</summary>
    public static LockHolderWorkloadOptions Options(TimeSpan? duration = null, TimeSpan? timeUntilExpires = null)
    {
        var settings = ChaosRunSettings.Current;
        var resources = settings.Pick(3, 4);

        return new LockHolderWorkloadOptions(
            Workers: settings.Pick(8, 32),
            Resources: [.. Enumerable.Range(0, resources).Select(i => $"s7-resource-{i}")],
            Duration: duration ?? settings.SoakDuration(TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(15)),
            MaxHold: TimeSpan.FromMilliseconds(50),
            AcquireOptions: new DistributedLockAcquireOptions
            {
                AcquireTimeout = TimeSpan.FromSeconds(2),
                TimeUntilExpires = timeUntilExpires,
                ReleaseOnDispose = false,
            }
        );
    }

    public static async Task<LockChaosResult> RunAsync(
        IReadOnlyList<IDistributedLock> nodes,
        LockHolderWorkloadOptions options,
        int seed,
        FencingContract contract,
        Func<FencingLedger, CancellationToken, Task> faults,
        CancellationToken cancellationToken
    )
    {
        // wall-clock: the ledger stamps each entry and the report states wall time; nothing is decided on it.
        var clock = TimeProvider.System;
        var ledger = new FencingLedger(clock);
        var store = new FencedResourceStore(ledger);
        var started = clock.GetTimestamp();
        var counts = await new LockHolderWorkload(nodes, ledger, store, seed)
            .RunAsync(options, faults, cancellationToken)
            .ConfigureAwait(false);

        return new LockChaosResult(
            contract,
            FencingVerdict.Evaluate(ledger.Snapshot(), contract),
            ledger,
            counts,
            clock.GetElapsedTime(started)
        );
    }

    /// <summary>A fault loop that calls <paramref name="inject"/> at seeded intervals until the run stops.</summary>
    public static async Task RepeatAsync(
        int seed,
        TimeSpan minInterval,
        TimeSpan maxInterval,
        Func<CancellationToken, Task> inject,
        CancellationToken stop
    )
    {
        var random = new Random(seed);

        while (!stop.IsCancellationRequested)
        {
            var interval = minInterval + ((maxInterval - minInterval) * random.NextDouble());

            // real-wait: faults land on a real backend at seeded intervals across the soak.
            await Task.Delay(interval, stop).ConfigureAwait(false);
            await inject(stop).ConfigureAwait(false);
        }
    }
}
