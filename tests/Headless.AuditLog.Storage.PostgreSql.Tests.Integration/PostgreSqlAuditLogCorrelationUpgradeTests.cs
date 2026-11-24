// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Hosting;
using Headless.Hosting.Schema;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// A database provisioned by a release whose correlation column held 128 characters: the upgrade widens it with one new
/// step, and a host started before that step runs fails naming it.
/// </summary>
[Collection<PostgreSqlAuditLogFixture>]
public sealed class PostgreSqlAuditLogCorrelationUpgradeTests(PostgreSqlAuditLogFixture fixture) : TestBase
{
    private const string _Table = "audit_log_entries";
    private const string _WidenStep = "6";

    // A fixed clock far from the server's own date: the partitions follow the application's clock.
    private static readonly DateTimeOffset _Now = new(2031, 5, 10, 12, 0, 0, TimeSpan.Zero);

    // The checksums the released steps recorded for these schemas. A released step whose text moves fails every
    // upgraded database's start, so the upgrade must leave each one as it shipped.
    private static readonly Dictionary<string, string> _ReleasedChecksums = new(StringComparer.Ordinal)
    {
        ["audit_log_pg_correlation_upgrade/1"] = "287A5FC202F6CB7D053D66935FEF9E386552DE663581657BFAA78CD3B4D88FDC",
        ["audit_log_pg_correlation_upgrade/2"] = "1F8D0D225F85015614A3F88FE8FACB9906CFD99DCD1E81511862A476496A3056",
        ["audit_log_pg_correlation_upgrade_partitioned/1"] =
            "9B884346C2EF5388B4D45520556D7A0AD0BAC45E43662A1D7B790D4DBE58DE4B",
        ["audit_log_pg_correlation_upgrade_partitioned/2"] =
            "3EB56FAE495B521BE21A5F1E105AD8B55324CE4FA12A07049AB3D679B6571EEE",
        ["audit_log_pg_correlation_upgrade_partitioned/5"] =
            "B7B05E3C5E684C25DE6A726B185195544D205678CC9DE22DEBC2FB03096639F4",
    };

    [Theory]
    [InlineData("audit_log_pg_correlation_upgrade", false)]
    [InlineData("audit_log_pg_correlation_upgrade_partitioned", true)]
    public async Task should_widen_the_correlation_column_with_only_the_new_step_when_upgrading(
        string schema,
        bool partitioned
    )
    {
        // given - a database as the earlier release left it: every earlier step recorded, the column at 128
        await _ExecuteAsync($"""DROP SCHEMA IF EXISTS "{schema}" CASCADE;""");

        using (var provisioner = _CreateHost(schema, partitioned))
        {
            await provisioner.Services.ProvisionResourcesAsync(AbortToken);
        }

        var feature = _Feature(schema, partitioned);
        await _ExecuteAsync(
            $"""
            DELETE FROM "{schema}".headless_schema_history WHERE feature = '{feature}' AND step_version = '{_WidenStep}';
            ALTER TABLE "{schema}"."{_Table}" ALTER COLUMN "correlation_id" TYPE character varying(128);
            """
        );

        (await _CorrelationWidthsAsync(schema)).Should().OnlyContain(width => width == "128");

        // when - the new build starts before the deployment ran resources provision
        using var early = _CreateHost(schema, partitioned);
        var start = () => early.StartAsync(AbortToken);

        // then
        (await start.Should().ThrowAsync<ResourceProvisioningException>()).WithMessage(
            $"*{schema}/{feature}*Steps {_WidenStep} of {feature} are not applied*resources provision*"
        );

        // when - the deployment provisions
        using var deploy = _CreateHost(schema, partitioned);
        var applied = await deploy.Services.GetRequiredService<SchemaRunner>().ApplyAsync(AbortToken);

        // then - only the new step ran, each released step kept the checksum it shipped with, and the column (and every
        // partition) holds 200 characters
        applied.AppliedSteps.Should().Equal(new SchemaAppliedStep(schema, feature, _WidenStep));
        var history = await _HistoryChecksumsAsync(schema, feature);

        foreach (
            var (key, checksum) in _ReleasedChecksums.Where(pair =>
                pair.Key.StartsWith($"{schema}/", StringComparison.Ordinal)
            )
        )
        {
            history.Should().Contain($"{key[(schema.Length + 1)..]} {checksum}");
        }

        var widths = await _CorrelationWidthsAsync(schema);
        widths.Should().OnlyContain(width => width == "200");
        // The parent, plus the current month and the default three months ahead.
        widths.Should().HaveCount(partitioned ? 5 : 1);

        using var started = _CreateHost(schema, partitioned);
        await started.StartAsync(AbortToken);
        (await started.Services.GetRequiredService<SchemaRunner>().VerifyAsync(AbortToken)).Should().BeEmpty();
    }

    private string _Feature(string schema, bool partitioned)
    {
        using var host = _CreateHost(schema, partitioned);

        return host.Services.GetRequiredService<SchemaRunner>().Contributions.Single().Feature;
    }

    private IHost _CreateHost(string schema, bool partitioned)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(_Now));
        builder.Services.AddHeadlessAuditLog(setup =>
        {
            setup.ConfigureStorage(options =>
            {
                options.Schema = schema;

                if (partitioned)
                {
                    options.Partitioning = AuditLogPartitioning.Monthly;
                }
            });
            setup.UsePostgreSql(fixture.ConnectionString);
        });

        return builder.Build();
    }

    // The parent table and every attached partition: widening the parent must reach each one.
    private Task<List<string>> _CorrelationWidthsAsync(string schema)
    {
        return PostgreSqlTestDatabase.ListAsync(
            fixture.ConnectionString,
            $"""
            SELECT (a.atttypmod - 4)::text
            FROM pg_catalog.pg_attribute a
            WHERE a.attname = 'correlation_id'
                AND (
                    a.attrelid = '"{schema}"."{_Table}"'::regclass
                    OR a.attrelid IN (
                        SELECT i.inhrelid FROM pg_catalog.pg_inherits i
                        WHERE i.inhparent = '"{schema}"."{_Table}"'::regclass
                    )
                );
            """,
            AbortToken
        );
    }

    private Task<List<string>> _HistoryChecksumsAsync(string schema, string feature)
    {
        return PostgreSqlTestDatabase.ListAsync(
            fixture.ConnectionString,
            $"""
            SELECT step_version || ' ' || checksum FROM "{schema}".headless_schema_history
            WHERE feature = '{feature}' ORDER BY step_version;
            """,
            AbortToken
        );
    }

    private Task _ExecuteAsync(string sql)
    {
        return PostgreSqlTestDatabase.ExecuteAsync(fixture.ConnectionString, sql, AbortToken);
    }
}
