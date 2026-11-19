// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.DistributedLocks;
using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;
using Npgsql;
using Tests.Oracle;
using Tests.Scenarios;

namespace Tests;

/// <summary>A private PostgreSQL server for the lock chaos scenarios, which terminate sessions on it.</summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlLockChaosFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<PostgreSqlLockChaosFixture>
{
    protected override bool ShareContainer => false;
}

/// <summary>
/// S7 on PostgreSQL: workers on several nodes take session advisory locks while the scenario terminates the backends
/// that hold them. A terminated holder keeps believing it holds the lock until it notices, so two holders can overlap;
/// the fencing token must still stop the stale write.
/// </summary>
[Collection<PostgreSqlLockChaosFixture>]
public sealed class PostgreSqlLockHolderChaosTests(PostgreSqlLockChaosFixture fixture) : TestBase
{
    private const string _Store = "PostgreSQL";

    [Fact]
    public async Task should_never_grant_a_token_at_or_below_an_accepted_one_when_holder_sessions_are_terminated()
    {
        // given
        var seed = ChaosRunSettings.Current.SeedFor("S7 PostgreSQL session kills");
        var application = _ApplicationName();
        await using var nodes = await _StartNodesAsync(application);
        var options = LockChaosScenario.Options();
        var killer = new BackendKiller(fixture.ConnectionString, application);
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"S7 PostgreSQL: {ChaosRunSettings.Current}, scenario seed {seed}"
        );

        // when
        var result = await LockChaosScenario.RunAsync(
            nodes.Locks,
            options,
            seed,
            FencingContract.DurableSequence,
            (_, stop) =>
                LockChaosScenario.RepeatAsync(
                    seed,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(3),
                    killer.KillAsync,
                    stop
                ),
            AbortToken
        );

        // then
        var violations = result.Report(_Store, "S7 holder sessions terminated", seed, killer.ToString());
        killer.Killed.Should().BePositive("the scenario must actually terminate lock-holding sessions");
        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task should_fail_the_issuer_rule_when_a_sabotaged_lock_reuses_tokens_under_session_kills()
    {
        // given — the negative control: the same workload and faults, with a lock that hands out an old token
        var seed = ChaosRunSettings.Current.SeedFor("S7 PostgreSQL negative control");
        var application = _ApplicationName();
        await using var nodes = await _StartNodesAsync(application);
        var sabotaged = nodes.Locks.Select(inner => new SabotagedDistributedLock(inner)).ToList();
        var options = LockChaosScenario.Options(TimeSpan.FromSeconds(15));
        var killer = new BackendKiller(fixture.ConnectionString, application);

        // when
        var result = await LockChaosScenario.RunAsync(
            sabotaged,
            options,
            seed,
            FencingContract.DurableSequence,
            (_, stop) =>
                LockChaosScenario.RepeatAsync(
                    seed,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(3),
                    killer.KillAsync,
                    stop
                ),
            AbortToken
        );

        // then
        var violations = result.Report(
            _Store,
            "S7 negative control: reused token",
            seed,
            killer.ToString(),
            negativeControl: true
        );
        sabotaged.Sum(item => item.Sabotaged).Should().BePositive();
        violations.Should().Contain(violation => violation.StartsWith(FencingVerdict.IssuerRuleName + ":"));
    }

    private static string _ApplicationName() => "s7-" + Guid.NewGuid().ToString("N")[..12];

    private Task<LockNodes> _StartNodesAsync(string application)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            ApplicationName = application,
        }.ConnectionString;
        var keyPrefix = application + ":";

        return LockNodes.StartAsync(
            ChaosRunSettings.Current.Pick(2, 4),
            setup =>
                setup.UsePostgreSql(options =>
                {
                    options.ConnectionString = connectionString;
                    options.KeyPrefix = keyPrefix;
                    options.KeepAlive = TimeSpan.FromSeconds(1);
                }),
            null,
            AbortToken
        );
    }

    /// <summary>Terminates the backends of this run's nodes that hold an advisory lock in the fixture's database.</summary>
    private sealed class BackendKiller(string adminConnectionString, string application)
    {
        private int _rounds;

        public int Killed { get; private set; }

        public async Task KillAsync(CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();

            // Filtered to this fixture's database and this run's application name, so the fixture's own ownership
            // session and every other connection survive.
            command.CommandText = """
                SELECT count(*)
                FROM (
                    SELECT pg_catalog.pg_terminate_backend(a.pid) AS terminated
                    FROM pg_catalog.pg_stat_activity a
                    WHERE a.datname = pg_catalog.current_database()
                      AND a.application_name = @application
                      AND a.pid <> pg_catalog.pg_backend_pid()
                      AND EXISTS (
                          SELECT 1 FROM pg_catalog.pg_locks l
                          WHERE l.pid = a.pid AND l.locktype = 'advisory' AND l.granted
                      )
                ) killed
                WHERE killed.terminated
                """;
            command.Parameters.AddWithValue("application", application);

            Killed += Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture
            );
            _rounds++;
        }

        public override string ToString() =>
            string.Create(CultureInfo.InvariantCulture, $"kill rounds {_rounds}, sessions terminated {Killed}");
    }
}
