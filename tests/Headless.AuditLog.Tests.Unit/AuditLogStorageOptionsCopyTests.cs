// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;

namespace Tests;

public sealed class AuditLogStorageOptionsCopyTests
{
    [Fact]
    public void should_copy_every_storage_option_to_the_registered_instance()
    {
        // given
        var source = new AuditLogStorageOptions
        {
            Schema = "compliance",
            TableName = "audit",
            JsonColumnType = AuditLogJsonColumnType.Json,
            CreatedAtColumnType = "timestamp with time zone",
            AppendOnly = true,
            PurgeAuthority = AuditLogPurgeAuthority.Owner,
            MinimumRetention = TimeSpan.FromDays(90),
            Partitioning = AuditLogPartitioning.Monthly,
            PartitionMonthsAhead = 6,
        };
        var target = new AuditLogStorageOptions();

        // when
        source.CopyTo(target);

        // then
        target.Should().BeEquivalentTo(source);
    }
}
