// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Tests.Oracle;
using Tests.Scenarios;

namespace Tests;

/// <summary>
/// The oracle's controls on the in-process lock, which needs no container: a healthy lock passes, and a lock that hands
/// a later holder an old token fails the issuer rule.
/// </summary>
public sealed class LockOracleControlTests : TestBase
{
    private static readonly TimeSpan _Run = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task should_pass_the_fencing_oracle_when_the_lock_is_healthy()
    {
        // given
        await using var nodes = await LockNodes.StartAsync(1, setup => setup.UseInMemory(), null, AbortToken);
        var options = LockChaosScenario.Options(_Run) with { Workers = 4 };

        // when
        var result = await LockChaosScenario.RunAsync(nodes.Locks, options, seed: 7, _NoFaults, AbortToken);

        // then
        var violations = result.Report("InMemory", "S7 control: healthy lock", 7, "no faults");
        violations.Should().BeEmpty();
        result.Ledger.MaxConcurrentHolders.Should().Be(1);
    }

    [Fact]
    public async Task should_fail_the_issuer_rule_when_the_lock_grants_an_old_token()
    {
        // given
        await using var nodes = await LockNodes.StartAsync(1, setup => setup.UseInMemory(), null, AbortToken);
        var sabotaged = new SabotagedDistributedLock(nodes.Locks[0]);
        var options = LockChaosScenario.Options(_Run) with { Workers = 4 };

        // when
        var result = await LockChaosScenario.RunAsync([sabotaged], options, seed: 7, _NoFaults, AbortToken);

        // then
        var violations = result.Report("InMemory", "S7 negative control: reused token", 7, "sabotaged grants");
        sabotaged.Sabotaged.Should().BePositive();
        violations.Should().Contain(violation => violation.StartsWith(FencingVerdict.IssuerRule + ":"));
        violations.Should().NotContain(violation => violation.StartsWith(FencingVerdict.AcceptedOrderRule + ":"));
    }

    private static Task _NoFaults(CancellationToken stop) => Task.CompletedTask;
}
