// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Tests.Scenarios;

namespace Tests;

/// <summary>
/// S7 on Redis: workers on several nodes take fenced Redis locks through a route the scenario moves from the primary
/// to a promoted asynchronous replica while writes are in flight. Two grants for one resource are legitimate here; the
/// oracle asks whether the per-resource fence counter ever hands a new grant a token the resource already accepted.
/// </summary>
public sealed class RedisLockFailoverChaosTests : TestBase
{
    [Fact]
    public async Task should_never_grant_a_token_at_or_below_an_accepted_one_when_redis_fails_over()
    {
        // given
        var seed = ChaosRunSettings.Current.SeedFor("S7 Redis failover");
        await using var pair = await RedisFailoverPair.StartAsync(AbortToken);
        var nodeCount = ChaosRunSettings.Current.Pick(2, 4);
        var multiplexers = new List<ConnectionMultiplexer>();

        try
        {
            for (var i = 0; i < nodeCount; i++)
            {
                multiplexers.Add(await ConnectionMultiplexer.ConnectAsync(pair.ConnectionString()));
            }

            var next = 0;
            await using var nodes = await LockNodes.StartAsync(
                nodeCount,
                setup => setup.UseRedis(),
                services => services.AddSingleton<IConnectionMultiplexer>(multiplexers[next++]),
                AbortToken
            );
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"S7 Redis: {ChaosRunSettings.Current}, scenario seed {seed}"
            );

            // when — a short lease, so a release lost in the failover frees the resource soon
            var result = await LockChaosScenario.RunAsync(
                nodes.Locks,
                LockChaosScenario.Options(timeUntilExpires: TimeSpan.FromSeconds(5)),
                seed,
                stop =>
                    LockChaosScenario.RepeatAsync(
                        seed,
                        TimeSpan.FromSeconds(8),
                        TimeSpan.FromSeconds(15),
                        pair.FailoverAsync,
                        stop
                    ),
                AbortToken
            );

            // then
            var violations = result.Report("Redis", "S7 primary fails over to async replica", seed, pair.ToString());
            pair.Failovers.Should().BePositive("the scenario must actually fail over");
            violations.Should().BeEmpty();
        }
        finally
        {
            foreach (var multiplexer in multiplexers)
            {
                await multiplexer.DisposeAsync();
            }
        }
    }
}
