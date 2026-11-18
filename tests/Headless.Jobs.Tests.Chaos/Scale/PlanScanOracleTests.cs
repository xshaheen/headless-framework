// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;

namespace Tests.Scale;

/// <summary>The plan oracle must find a full scan of a jobs table in each store's plan format, and nothing else.</summary>
public sealed class PlanScanOracleTests : TestBase
{
    private static readonly string[] _PgTables = ["time_jobs", "cron_job_occurrences"];
    private static readonly string[] _SqlTables = ["TimeJobs", "CronJobOccurrences"];

    // The shape auto_explain writes to stderr: a LOG line ending in "plan:", then the plan, each line tab-indented.
    private const string _Log =
        "2026-11-18 01:00:00.000 UTC [42] LOG:  statement: SELECT 1\n"
        + "2026-11-18 01:00:00.001 UTC [42] LOG:  duration: 0.020 ms  plan:\n"
        + "\tQuery Text: SELECT id FROM headless.time_jobs WHERE status = $1\n"
        + "\tSeq Scan on time_jobs  (cost=0.00..3576.10 rows=90112 width=16) (actual rows=90001.00 loops=1)\n"
        + "\t  Filter: ((status)::text = 'Succeeded'::text)\n"
        + "2026-11-18 01:00:00.002 UTC [42] LOG:  duration: 0.010 ms  plan:\n"
        + "\tQuery Text: SELECT id FROM headless.cron_job_occurrences WHERE id = $1\n"
        + "\tIndex Scan using pk_cron_job_occurrences on cron_job_occurrences  (cost=0.42..8.44 rows=1 width=16)\n"
        + "2026-11-18 01:00:00.003 UTC [42] LOG:  duration: 0.010 ms  plan:\n"
        + "\tQuery Text: /* chaos-probe */ SELECT COUNT(*) FROM headless.time_jobs\n"
        + "\tSeq Scan on time_jobs  (cost=0.00..3576.10 rows=100000 width=0)\n";

    [Fact]
    public void should_split_an_auto_explain_log_into_one_plan_per_statement()
    {
        var plans = PlanScanOracle.ParseAutoExplainLog(_Log);

        plans
            .Select(x => x.QueryText)
            .Should()
            .HaveCount(3)
            .And.Contain("SELECT id FROM headless.time_jobs WHERE status = $1");
    }

    [Fact]
    public void should_catch_a_postgresql_seq_scan_and_skip_index_scans_and_probes()
    {
        var plans = PlanScanOracle.ParseAutoExplainLog(_Log);

        var violations = PlanScanOracle.Violations(plans, plan => PlanScanOracle.PostgreSqlScans(plan, _PgTables));

        violations.Should().ContainSingle().Which.Should().StartWith("Seq Scan on time_jobs in: SELECT id");
    }

    [Fact]
    public void should_name_the_alias_of_a_postgresql_seq_scan()
    {
        var plan = new CapturedPlan(
            "q",
            "->  Seq Scan on time_jobs gate_parent  (cost=0.00..3576.12 rows=89946 width=16)"
        );

        PlanScanOracle.PostgreSqlScans(plan, _PgTables).Should().Equal("Seq Scan on time_jobs gate_parent");
    }

    [Fact]
    public void should_ignore_a_postgresql_seq_scan_of_another_table()
    {
        var plan = new CapturedPlan("q", "Seq Scan on cron_jobs  (cost=0.00..1.01 rows=1 width=16)");

        PlanScanOracle.PostgreSqlScans(plan, _PgTables).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Clustered Index Scan", "IndexScan", "[TimeJobs]", true)]
    [InlineData("Table Scan", "TableScan", "[CronJobOccurrences]", true)]
    [InlineData("Clustered Index Seek", "IndexScan", "[TimeJobs]", false)]
    [InlineData("Index Seek", "IndexScan", "[TimeJobs]", false)]
    [InlineData("Clustered Index Scan", "IndexScan", "[CronJobs]", false)]
    public void should_catch_only_full_scans_of_a_jobs_table_in_a_showplan(
        string physicalOp,
        string element,
        string table,
        bool expected
    )
    {
        var xml = $"""
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <BatchSequence><Batch><Statements><StmtSimple><QueryPlan>
                <RelOp PhysicalOp="Top">
                  <Top>
                    <RelOp PhysicalOp="{physicalOp}">
                      <{element}><Object Database="[db]" Schema="[headless]" Table="{table}" Index="[PK]" /></{element}>
                    </RelOp>
                  </Top>
                </RelOp>
              </QueryPlan></StmtSimple></Statements></Batch></BatchSequence>
            </ShowPlanXML>
            """;

        var scans = PlanScanOracle.SqlServerScans(new CapturedPlan("q", xml), _SqlTables);

        scans.Should().HaveCount(expected ? 1 : 0);
    }
}
