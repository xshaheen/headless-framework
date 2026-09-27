// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Testing.Tests;
using Headless.UnitOfWork;

namespace Tests;

public sealed class UnitOfWorkTransactionsTests : TestBase
{
    [Fact]
    public void should_refuse_a_unit_that_is_not_active()
    {
        // given
        var unit = Substitute.For<IUnitOfWork>();
        unit.State.Returns(UnitOfWorkState.Completed);

        // when
        var act = () => UnitOfWorkTransactions.RequireTransaction<DbTransaction>(unit, "Test");

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*Completed*live transaction*");
    }

    [Fact]
    public void should_refuse_a_unit_with_no_relational_resource()
    {
        // given
        var unit = Substitute.For<IUnitOfWork>();
        unit.State.Returns(UnitOfWorkState.Active);
        unit.Resource.Returns((IUnitOfWorkResource?)null);

        // when
        var act = () => UnitOfWorkTransactions.RequireTransaction<DbTransaction>(unit, "Test");

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*no relational resource*Test*");
    }

    [Fact]
    public void should_refuse_a_resource_whose_transaction_completed()
    {
        // given
        var resource = Substitute.For<IRelationalUnitOfWorkResource>();
        resource.IsTransactionCompleted.Returns(true);
        var unit = Substitute.For<IUnitOfWork>();
        unit.State.Returns(UnitOfWorkState.Active);
        unit.Resource.Returns(resource);

        // when
        var act = () => UnitOfWorkTransactions.RequireTransaction<DbTransaction>(unit, "Test");

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*already completed*");
    }

    [Fact]
    public void should_refuse_a_transaction_of_another_provider()
    {
        // given
        var resource = Substitute.For<IRelationalUnitOfWorkResource>();
        resource.IsTransactionCompleted.Returns(false);
        resource.Transaction.Returns(Substitute.For<DbTransaction>());
        var unit = Substitute.For<IUnitOfWork>();
        unit.State.Returns(UnitOfWorkState.Active);
        unit.Resource.Returns(resource);

        // when
        var act = () => UnitOfWorkTransactions.RequireTransaction<OtherProviderTransaction>(unit, "Test");

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*cannot run inside*");
    }

    [Fact]
    public void should_return_the_live_transaction_when_it_matches()
    {
        // given
        var transaction = Substitute.For<DbTransaction>();
        var resource = Substitute.For<IRelationalUnitOfWorkResource>();
        resource.IsTransactionCompleted.Returns(false);
        resource.Transaction.Returns(transaction);
        var unit = Substitute.For<IUnitOfWork>();
        unit.State.Returns(UnitOfWorkState.Active);
        unit.Resource.Returns(resource);

        // when
        var result = UnitOfWorkTransactions.RequireTransaction<DbTransaction>(unit, "Test");

        // then
        result.Should().BeSameAs(transaction);
    }

    private abstract class OtherProviderTransaction : DbTransaction;
}
