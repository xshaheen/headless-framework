// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sequences;
using Headless.Testing.Tests;
using Headless.UnitOfWork;

namespace Tests;

public sealed class UnitOfWorkSequencesAccessorTests : TestBase
{
    [Fact]
    public void should_name_the_setup_call_when_no_feature_is_registered()
    {
        // given
        var unit = Substitute.For<IUnitOfWork>();
        unit.GetFeature<IUnitOfWorkSequences>().Returns((IUnitOfWorkSequences?)null);

        // when
        var act = () => unit.Sequences;

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*AddHeadlessSequences*UsePostgreSql*UseSqlServer*");
    }

    [Fact]
    public void should_refuse_a_null_unit()
    {
        var act = () => ((IUnitOfWork)null!).Sequences;

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task should_return_one_binding_per_unit_that_forwards_the_unit_and_arguments()
    {
        // given
        var feature = Substitute.For<IUnitOfWorkSequences>();
        var unit = _UnitWithUnitLocalState(feature);

        // when
        var first = unit.Sequences;
        var second = unit.Sequences;
        await first.NextAsync("invoice", "2026", AbortToken);

        // then
        second.Should().BeSameAs(first);
        await feature.Received(1).NextAsync(unit, "invoice", "2026", AbortToken);
    }

    // Mimics the unit's GetOrAdd contract: the factory runs once and later reads return the stored state.
    private static IUnitOfWork _UnitWithUnitLocalState(IUnitOfWorkSequences feature)
    {
        var unit = Substitute.For<IUnitOfWork>();
        unit.GetFeature<IUnitOfWorkSequences>().Returns(feature);

        UnitOfWorkSequences? stored = null;
        unit.GetOrAdd(
                Arg.Any<IUnitOfWorkSequences>(),
                Arg.Any<Func<IUnitOfWork, IUnitOfWorkSequences, UnitOfWorkSequences>>()
            )
            .Returns(call =>
                stored ??= call.Arg<Func<IUnitOfWork, IUnitOfWorkSequences, UnitOfWorkSequences>>()(unit, feature)
            );

        return unit;
    }
}
