// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.AuditLog;

namespace Tests;

public sealed class AuditLogStorageOptionsValidatorTests
{
    [Fact]
    public void should_accept_the_defaults_on_every_relational_provider()
    {
        _Validate(StorageProvider.PostgreSql, new AuditLogStorageOptions()).Should().BeEmpty();
        _Validate(StorageProvider.SqlServer, new AuditLogStorageOptions()).Should().BeEmpty();
    }

    [Fact]
    public void should_accept_an_owner_purge_with_a_minimum_retention_on_an_append_only_postgresql_table()
    {
        // given
        var options = new AuditLogStorageOptions
        {
            AppendOnly = true,
            PurgeAuthority = AuditLogPurgeAuthority.Owner,
            MinimumRetention = TimeSpan.FromDays(90),
            Partitioning = AuditLogPartitioning.Monthly,
            PartitionMonthsAhead = 120,
        };

        // then
        _Validate(StorageProvider.PostgreSql, options).Should().BeEmpty();
    }

    [Fact]
    public void should_reject_an_owner_purge_without_the_append_only_trigger()
    {
        // given - without the trigger the function gates nothing: any login with DELETE purges the table
        var options = new AuditLogStorageOptions { PurgeAuthority = AuditLogPurgeAuthority.Owner };

        // then
        _Validate(StorageProvider.PostgreSql, options).Should().ContainSingle().Which.Should().Contain("AppendOnly");
    }

    [Fact]
    public void should_reject_a_minimum_retention_that_no_function_would_enforce()
    {
        // given
        var options = new AuditLogStorageOptions { AppendOnly = true, MinimumRetention = TimeSpan.FromDays(1) };

        // then
        _Validate(StorageProvider.PostgreSql, options)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("MinimumRetention");
    }

    [Fact]
    public void should_accept_a_minimum_retention_for_a_partitioned_table_alone()
    {
        // given - the partition drop function enforces it
        var options = new AuditLogStorageOptions
        {
            Partitioning = AuditLogPartitioning.Monthly,
            MinimumRetention = TimeSpan.FromDays(1),
        };

        // then
        _Validate(StorageProvider.PostgreSql, options).Should().BeEmpty();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(121)]
    public void should_reject_months_ahead_outside_the_range_the_partition_function_accepts(int monthsAhead)
    {
        // given
        var options = new AuditLogStorageOptions
        {
            Partitioning = AuditLogPartitioning.Monthly,
            PartitionMonthsAhead = monthsAhead,
        };

        // then
        _Validate(StorageProvider.PostgreSql, options).Should().ContainSingle();
    }

    [Fact]
    public void should_reject_a_minimum_retention_shorter_than_a_second()
    {
        // given - the functions compare whole seconds
        var options = new AuditLogStorageOptions
        {
            Partitioning = AuditLogPartitioning.Monthly,
            MinimumRetention = TimeSpan.FromMilliseconds(10),
        };

        // then
        _Validate(StorageProvider.PostgreSql, options).Should().ContainSingle();
    }

    [Fact]
    public void should_reject_the_postgresql_only_options_on_sql_server()
    {
        // given
        var options = new AuditLogStorageOptions
        {
            AppendOnly = true,
            PurgeAuthority = AuditLogPurgeAuthority.Owner,
            MinimumRetention = TimeSpan.FromDays(1),
            Partitioning = AuditLogPartitioning.Monthly,
        };

        // then
        _Validate(StorageProvider.SqlServer, options)
            .Should()
            .HaveCount(3)
            .And.OnlyContain(message => message.Contains("PostgreSql audit-log provider only"));
    }

    private static List<string> _Validate(StorageProvider provider, AuditLogStorageOptions options)
    {
        var validator = new RelationalAuditLogStorageOptionsValidator(
            provider,
            [AuditLogJsonColumnType.Jsonb, AuditLogJsonColumnType.Json, AuditLogJsonColumnType.NvarcharMax],
            "unused"
        );

        return validator.Validate(options).Errors.ConvertAll(failure => failure.ErrorMessage);
    }
}
