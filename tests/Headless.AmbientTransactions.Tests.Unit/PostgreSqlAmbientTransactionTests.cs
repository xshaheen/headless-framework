// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.AmbientTransactions;
using Headless.AmbientTransactions.PostgreSql;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class PostgreSqlAmbientTransactionTests
    : AmbientTransactionConformanceTests<PostgreSqlAmbientTransactionFixture>
{
    public PostgreSqlAmbientTransactionTests()
        : base(new PostgreSqlAmbientTransactionFixture()) { }

    [Fact]
    public override Task should_flush_registered_work_once_after_commit_async()
    {
        return base.should_flush_registered_work_once_after_commit_async();
    }

    [Fact]
    public override Task should_flush_registered_work_once_after_commit()
    {
        return base.should_flush_registered_work_once_after_commit();
    }

    [Fact]
    public override Task should_discard_registered_work_after_rollback_async()
    {
        return base.should_discard_registered_work_after_rollback_async();
    }

    [Fact]
    public override Task should_discard_registered_work_after_rollback()
    {
        return base.should_discard_registered_work_after_rollback();
    }

    [Fact]
    public override Task should_complete_detached_external_transaction_without_retouching_current()
    {
        return base.should_complete_detached_external_transaction_without_retouching_current();
    }

    [Fact]
    public override Task should_clear_current_when_transaction_detaches()
    {
        return base.should_clear_current_when_transaction_detaches();
    }

    [Fact]
    public override Task should_set_auto_commit_flag_when_transaction_begins()
    {
        return base.should_set_auto_commit_flag_when_transaction_begins();
    }

    [Fact]
    public override Task should_honor_requested_isolation_level_when_supported()
    {
        return base.should_honor_requested_isolation_level_when_supported();
    }

    [Fact]
    public override Task should_reject_registering_work_after_completion()
    {
        return base.should_reject_registering_work_after_completion();
    }

    [Fact]
    public async Task setup_should_register_current_accessor_and_transaction()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddPostgreSqlAmbientTransactions();
        await using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<ICurrentAmbientTransaction>().Should().BeOfType<AsyncLocalCurrentAmbientTransaction>();
        provider.GetRequiredService<IAmbientTransaction>().Should().BeOfType<PostgreSqlAmbientTransaction>();
    }
}

public sealed class PostgreSqlAmbientTransactionFixture : RecordingDbAmbientTransactionFixture
{
    protected override IAmbientTransaction CreateTransaction(ICurrentAmbientTransaction current)
    {
        return new PostgreSqlAmbientTransaction(current);
    }
}
