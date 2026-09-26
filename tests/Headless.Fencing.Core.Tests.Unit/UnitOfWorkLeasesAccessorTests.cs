// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Testing.Tests;
using Headless.UnitOfWork;

namespace Tests;

public sealed class UnitOfWorkLeasesAccessorTests : TestBase
{
    [Fact]
    public void should_name_the_setup_call_when_no_feature_is_registered()
    {
        // given
        var unit = Substitute.For<IUnitOfWork>();
        unit.GetFeature<IUnitOfWorkLeases>().Returns((IUnitOfWorkLeases?)null);

        // when
        var act = () => unit.Leases;

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*AddHeadlessFencing*UsePostgreSql*UseSqlServer*");
    }

    [Fact]
    public void should_refuse_a_null_unit()
    {
        var act = () => ((IUnitOfWork)null!).Leases;

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task should_return_one_binding_per_unit_that_forwards_the_unit_and_arguments()
    {
        // given
        var feature = Substitute.For<IUnitOfWorkLeases>();
        var unit = _UnitWithUnitLocalState(feature);
        var lease = new FencedLease(null, "job", "order-1", 3);
        var duration = TimeSpan.FromSeconds(10);

        // when
        var first = unit.Leases;
        var second = unit.Leases;
        await first.GrantAsync("job", "order-1", duration, AbortToken);
        await first.RenewAsync(lease, duration, AbortToken);
        await first.SettleAsync(lease, AbortToken);
        await first.ReleaseAsync(lease, AbortToken);
        await first.FenceAsync(lease, AbortToken);

        // then
        second.Should().BeSameAs(first);
        await feature.Received(1).GrantAsync(unit, "job", "order-1", duration, AbortToken);
        await feature.Received(1).RenewAsync(unit, lease, duration, AbortToken);
        await feature.Received(1).SettleAsync(unit, lease, AbortToken);
        await feature.Received(1).ReleaseAsync(unit, lease, AbortToken);
        await feature.Received(1).FenceAsync(unit, lease, AbortToken);
    }

    // Mimics the unit's GetOrAdd contract: the factory runs once and later reads return the stored state.
    private static IUnitOfWork _UnitWithUnitLocalState(IUnitOfWorkLeases feature)
    {
        var unit = Substitute.For<IUnitOfWork>();
        unit.GetFeature<IUnitOfWorkLeases>().Returns(feature);

        UnitOfWorkLeases? stored = null;
        unit.GetOrAdd(Arg.Any<IUnitOfWorkLeases>(), Arg.Any<Func<IUnitOfWork, IUnitOfWorkLeases, UnitOfWorkLeases>>())
            .Returns(call =>
                stored ??= call.Arg<Func<IUnitOfWork, IUnitOfWorkLeases, UnitOfWorkLeases>>()(unit, feature)
            );

        return unit;
    }
}
