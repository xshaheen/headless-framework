// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tests;

/// <summary>
/// Proves that lease predicates and stamps are evaluated by the DATABASE clock, not in-process.
/// </summary>
/// <remarks>
/// <para>
/// The Jobs lease paths rely on EF translating a bare <c>DateTime.UtcNow</c> inside an <c>ExecuteUpdate</c>
/// expression tree into the provider's server-time expression. If EF ever client-evaluated it instead, the
/// value would silently become the reclaiming node's wall clock and cross-node lease math would depend on
/// host skew again — the exact defect the database-clock design removes.
/// </para>
/// <para>
/// A skewed-<c>TimeProvider</c> test canNOT catch that regression: a client-evaluated <c>DateTime.UtcNow</c>
/// ignores <c>TimeProvider</c> entirely, so it dodges the injected skew and the assertion still passes. The
/// only decisive evidence is the SQL actually sent to the server, which is what this test captures.
/// </para>
/// </remarks>
[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlDatabaseClockTranslationTests(PostgreSqlJobsCoordinationFixture fixture) : TestBase
{
    [Fact]
    public async Task lease_expiry_predicate_and_stamp_are_emitted_as_server_time_not_a_bound_parameter()
    {
        // given
        var capture = new SqlCaptureInterceptor();

        var options = new DbContextOptionsBuilder<ClockProbeDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .AddInterceptors(capture)
            .Options;

        await using var db = new ClockProbeDbContext(options);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS clock_probe (
                "Id" uuid PRIMARY KEY,
                "LockedUntil" timestamptz NULL,
                "UpdatedAt" timestamptz NOT NULL
            );
            """,
            AbortToken
        );

        var leaseSeconds = TimeSpan.FromMinutes(5).TotalSeconds;

        // when — the exact shape the reclaim/renewal paths build: the clock appears in BOTH the
        // eligibility predicate and the deadline stamp, inside one statement.
        await db
            .Probes.Where(x => x.LockedUntil <= DateTime.UtcNow)
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.LockedUntil, _ => DateTime.UtcNow.AddSeconds(leaseSeconds))
                        .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                AbortToken
            );

        // then
        var sql = capture.LastNonQuerySql;
        sql.Should().NotBeNull();

        // Npgsql maps DateTime.UtcNow to the server's now(). Its presence proves the clock is read by
        // PostgreSQL, so the write and every remote node's comparison share one authority.
        sql!.Should().Contain("now()", "the DB clock must be evaluated server-side, never in-process");

        // And it must NOT have been client-evaluated into a bound timestamp parameter — that would put the
        // reclaiming node's wall clock back into cross-node lease math.
        sql.Should()
            .NotMatchRegex(
                "\"LockedUntil\"\\s*<=\\s*@",
                "a bound parameter here means EF client-evaluated the clock — the skew defect is back"
            );
    }

    private sealed class SqlCaptureInterceptor : DbCommandInterceptor
    {
        public string? LastNonQuerySql { get; private set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            // The CREATE TABLE above also lands here; keep only the ExecuteUpdate we care about.
            if (command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                LastNonQuerySql = command.CommandText;
            }

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class ClockProbeEntity
    {
        public Guid Id { get; set; }

        public DateTime? LockedUntil { get; set; }

        public DateTime UpdatedAt { get; set; }
    }

    private sealed class ClockProbeDbContext(DbContextOptions<ClockProbeDbContext> options) : DbContext(options)
    {
        public DbSet<ClockProbeEntity> Probes => Set<ClockProbeEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ClockProbeEntity>().ToTable("clock_probe").HasKey(x => x.Id);
        }
    }
}
