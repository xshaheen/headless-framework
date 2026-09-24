// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Headless.UnitOfWork;

namespace Tests;

public sealed class UnitOfWorkTransactionLocksTests : TestBase
{
    [Fact]
    public void should_name_the_capable_providers_when_no_feature_is_registered()
    {
        // given
        var unit = Substitute.For<IUnitOfWork>();
        unit.GetFeature<IUnitOfWorkTransactionLocks>().Returns((IUnitOfWorkTransactionLocks?)null);

        // when
        var act = () => unit.TransactionLocks;

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*UsePostgreSql*UseSqlServer*");
    }

    [Fact]
    public void should_bind_the_feature_once_per_unit_through_unit_local_state()
    {
        // given
        var feature = Substitute.For<IUnitOfWorkTransactionLocks>();
        var unit = Substitute.For<IUnitOfWork>();
        unit.GetFeature<IUnitOfWorkTransactionLocks>().Returns(feature);
        unit.GetOrAdd(
                Arg.Any<IUnitOfWorkTransactionLocks>(),
                Arg.Any<Func<IUnitOfWork, IUnitOfWorkTransactionLocks, UnitOfWorkTransactionLocks>>()
            )
            .Returns(call =>
                call.Arg<Func<IUnitOfWork, IUnitOfWorkTransactionLocks, UnitOfWorkTransactionLocks>>()(unit, feature)
            );
        var timeout = TimeSpan.FromSeconds(3);

        // when
        var locks = unit.TransactionLocks;
        _ = locks.TryAcquireAsync("orders:1", timeout, AbortToken);

        // then — the binding forwards the handle it was created for and every argument unchanged
        locks.Should().NotBeNull();
        _ = feature.Received(1).TryAcquireAsync(unit, "orders:1", timeout, AbortToken);
    }

    [Fact]
    public void should_compare_handles_by_resource()
    {
        new TransactionLockHandle("orders:1").Should().Be(new TransactionLockHandle("orders:1"));
        new TransactionLockHandle("orders:1").Should().NotBe(new TransactionLockHandle("orders:2"));
    }
}
