// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.InMemoryStorage;
using Headless.Messaging.Transactions;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;

namespace Tests;

public sealed class InMemoryOutboxTransactionTests : TestBase
{
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();
    private readonly IOutboxTransactionAccessor _accessor = Substitute.For<IOutboxTransactionAccessor>();

    [Fact]
    public void should_not_throw_on_commit()
    {
        // given
        var sut = new InMemoryOutboxTransaction(_dispatcher, _accessor);

        // when
        var act = () => sut.Commit();

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public async Task should_complete_commit_async()
    {
        // given
        var sut = new InMemoryOutboxTransaction(_dispatcher, _accessor);

        // when
        await sut.CommitAsync(AbortToken);

        // then - no exception thrown
    }

    [Fact]
    public void should_not_throw_on_rollback()
    {
        // given
        var sut = new InMemoryOutboxTransaction(_dispatcher, _accessor);

        // when
        var act = () => sut.Rollback();

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public async Task should_complete_rollback_async()
    {
        // given
        var sut = new InMemoryOutboxTransaction(_dispatcher, _accessor);

        // when
        await sut.RollbackAsync(AbortToken);

        // then - no exception thrown
    }

    [Fact]
    public void should_have_null_db_transaction_by_default()
    {
        // given
        var sut = new InMemoryOutboxTransaction(_dispatcher, _accessor);

        // then
        sut.DbTransaction.Should().BeNull();
    }

    [Fact]
    public void should_allow_setting_auto_commit()
    {
        // given
        var sut = new InMemoryOutboxTransaction(_dispatcher, _accessor);

        // when
        sut.AutoCommit = true;

        // then
        sut.AutoCommit.Should().BeTrue();
    }

    [Fact]
    public void should_handle_dispose()
    {
        // given
        var sut = new InMemoryOutboxTransaction(_dispatcher, _accessor);

        // when
        var act = () => sut.Dispose();

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public async Task should_handle_dispose_async()
    {
        // given
        var sut = new InMemoryOutboxTransaction(_dispatcher, _accessor);

        // when
        await sut.DisposeAsync();

        // then - no exception thrown
    }

    [Fact]
    public void should_implement_ioutbox_transaction()
    {
        // given
        var sut = new InMemoryOutboxTransaction(_dispatcher, _accessor);

        // then
        sut.Should().BeAssignableTo<Headless.Messaging.IOutboxTransaction>();
    }
}
