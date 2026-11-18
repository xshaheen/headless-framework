// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.DistributedLocks;
using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Tests.Scenarios;

namespace Tests;

/// <summary>A private SQL Server for the lock chaos scenario, which kills sessions on it.</summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerLockChaosFixture : HeadlessSqlServerFixture, ICollectionFixture<SqlServerLockChaosFixture>
{
    protected override bool ShareContainer => false;
}

/// <summary>
/// S7 on SQL Server: workers on several nodes take session application locks (<c>sp_getapplock</c>) while the scenario
/// kills the sessions that hold them. The fencing token, drawn from a sequence in the grant batch, must still stop the
/// stale write of a holder that has not noticed its session died.
/// </summary>
[Collection<SqlServerLockChaosFixture>]
public sealed class SqlServerLockHolderChaosTests(SqlServerLockChaosFixture fixture) : TestBase
{
    [Fact]
    public async Task should_never_grant_a_token_at_or_below_an_accepted_one_when_holder_sessions_are_killed()
    {
        // given
        var seed = ChaosRunSettings.Current.SeedFor("S7 SQL Server session kills");
        var application = "s7-" + Guid.NewGuid().ToString("N")[..12];
        var connectionString = new SqlConnectionStringBuilder(fixture.ConnectionString)
        {
            ApplicationName = application,
        }.ConnectionString;
        await using var nodes = await LockNodes.StartAsync(
            ChaosRunSettings.Current.Pick(2, 4),
            setup =>
                setup.UseSqlServer(options =>
                {
                    options.ConnectionString = connectionString;
                    options.KeyPrefix = application + ":";
                    options.ConnectionProbeInterval = TimeSpan.FromSeconds(1);
                }),
            null,
            AbortToken
        );
        var killer = new SessionKiller(fixture.ConnectionString, application);
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"S7 SQL Server: {ChaosRunSettings.Current}, scenario seed {seed}"
        );

        // when
        var result = await LockChaosScenario.RunAsync(
            nodes.Locks,
            LockChaosScenario.Options(),
            seed,
            stop =>
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
        var violations = result.Report("SQL Server", "S7 holder sessions killed", seed, killer.ToString());
        killer.Killed.Should().BePositive("the scenario must actually kill lock-holding sessions");
        violations.Should().BeEmpty();
    }

    /// <summary>Kills the sessions of this run's nodes that hold an application lock in the fixture's database.</summary>
    private sealed class SessionKiller(string adminConnectionString, string application)
    {
        private int _rounds;

        public int Killed { get; private set; }

        public async Task KillAsync(CancellationToken cancellationToken)
        {
            await using var connection = new SqlConnection(adminConnectionString);
            await connection.OpenAsync(cancellationToken);
            var sessions = new List<short>();

            await using (var find = connection.CreateCommand())
            {
                // Filtered to this database and this run's program name, so the fixture's ownership session and every
                // other connection survive. dm_tran_locks truncates application lock names, so the name is not used.
                find.CommandText = """
                    SELECT s.session_id
                    FROM sys.dm_exec_sessions s
                    WHERE s.program_name = @application
                      AND s.database_id = DB_ID()
                      AND s.session_id <> @@SPID
                      AND EXISTS (
                          SELECT 1 FROM sys.dm_tran_locks l
                          WHERE l.request_session_id = s.session_id
                            AND l.resource_type = N'APPLICATION'
                            AND l.request_status = N'GRANT'
                      )
                    """;
                find.Parameters.AddWithValue("application", application);

                await using var reader = await find.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    sessions.Add(reader.GetInt16(0));
                }
            }

            foreach (var session in sessions)
            {
                try
                {
                    await using var kill = connection.CreateCommand();
#pragma warning disable CA2100 // False positive: the session id is a smallint read from the server, not input.
                    kill.CommandText = string.Create(CultureInfo.InvariantCulture, $"KILL {session}");
#pragma warning restore CA2100
                    await kill.ExecuteNonQueryAsync(cancellationToken);
                    Killed++;
                }
                catch (SqlException)
                {
                    // The session ended between the query and the kill.
                }
            }

            _rounds++;
        }

        public override string ToString() =>
            string.Create(CultureInfo.InvariantCulture, $"kill rounds {_rounds}, sessions killed {Killed}");
    }
}
