// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Headless.UnitOfWork;

namespace Tests;

public sealed class UnitOfWorkAdvisoryLocksTests : TestBase
{
    [Fact]
    public void should_name_the_capable_providers_when_no_feature_is_registered()
    {
        // given
        var unit = Substitute.For<IUnitOfWork>();
        unit.GetFeature<IUnitOfWorkAdvisoryLocks>().Returns((IUnitOfWorkAdvisoryLocks?)null);

        // when
        var act = () => unit.AdvisoryLocks;

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*UsePostgreSql*UseSqlServer*");
    }

    [Fact]
    public void should_bind_the_feature_once_per_unit_through_unit_local_state()
    {
        // given
        var feature = Substitute.For<IUnitOfWorkAdvisoryLocks>();
        var unit = Substitute.For<IUnitOfWork>();
        unit.GetFeature<IUnitOfWorkAdvisoryLocks>().Returns(feature);
        unit.GetOrAdd(
                Arg.Any<IUnitOfWorkAdvisoryLocks>(),
                Arg.Any<Func<IUnitOfWork, IUnitOfWorkAdvisoryLocks, UnitOfWorkAdvisoryLocks>>()
            )
            .Returns(call =>
                call.Arg<Func<IUnitOfWork, IUnitOfWorkAdvisoryLocks, UnitOfWorkAdvisoryLocks>>()(unit, feature)
            );

        // when
        var locks = unit.AdvisoryLocks;
        _ = locks.TryAcquireAsync("orders:1", AbortToken);

        // then — the binding forwards the handle it was created for
        locks.Should().NotBeNull();
        _ = feature.Received(1).TryAcquireAsync(unit, "orders:1", AbortToken);
    }

    [Fact]
    public void should_refuse_a_unit_that_is_not_active()
    {
        // given
        var unit = Substitute.For<IUnitOfWork>();
        unit.State.Returns(UnitOfWorkState.Completed);

        // when
        var act = () => UnitOfWorkTransactionResolver.Require<DbTransaction>(unit, "Test");

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
        var act = () => UnitOfWorkTransactionResolver.Require<DbTransaction>(unit, "Test");

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
        var act = () => UnitOfWorkTransactionResolver.Require<DbTransaction>(unit, "Test");

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
        var act = () => UnitOfWorkTransactionResolver.Require<OtherProviderTransaction>(unit, "Test");

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*cannot lock inside*");
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
        var result = UnitOfWorkTransactionResolver.Require<DbTransaction>(unit, "Test");

        // then
        result.Should().BeSameAs(transaction);
    }

    private abstract class OtherProviderTransaction : DbTransaction;
}
