// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features;
using Headless.Testing.Tests;
using Headless.UnitOfWork;

namespace Tests.Values;

public sealed class UnitOfWorkFeaturesTests : TestBase
{
    private readonly IUnitOfWorkFeatures _feature = Substitute.For<IUnitOfWorkFeatures>();
    private readonly IUnitOfWork _unit = Substitute.For<IUnitOfWork>();

    public UnitOfWorkFeaturesTests()
    {
        _unit
            .GetOrAdd(
                Arg.Any<IUnitOfWorkFeatures>(),
                Arg.Any<Func<IUnitOfWork, IUnitOfWorkFeatures, UnitOfWorkFeatures>>()
            )
            .Returns(call =>
                call.Arg<Func<IUnitOfWork, IUnitOfWorkFeatures, UnitOfWorkFeatures>>()(
                    _unit,
                    call.Arg<IUnitOfWorkFeatures>()
                )
            );
    }

    [Fact]
    public async Task should_bind_every_write_to_the_unit_it_was_read_from()
    {
        // given
        _unit.GetFeature<IUnitOfWorkFeatures>().Returns(_feature);
        var features = _unit.Features;
        var batch = new Dictionary<string, string?>(StringComparer.Ordinal) { ["Exports"] = "false" };

        // when
        await features.SetAsync("Reports", "10", "Edition", "pro", cancellationToken: AbortToken);
        await features.SetAsync(batch, "Tenant", "t1", forceToSet: true, AbortToken);
        await features.SetForTenantAsync("Seats", "5", "t1", cancellationToken: AbortToken);
        await features.GrantToTenantAsync("Api", "t1", AbortToken);
        await features.RevokeFromTenantAsync("Sso", "t1", AbortToken);
        await features.SetForEditionAsync("Seats", "50", "pro", cancellationToken: AbortToken);
        await features.DeleteAsync("Tenant", "t1", AbortToken);

        // then
        await _feature.Received(1).SetAsync(_unit, _One("Reports", "10"), "Edition", "pro", false, AbortToken);
        await _feature.Received(1).SetAsync(_unit, batch, "Tenant", "t1", true, AbortToken);
        await _feature
            .Received(1)
            .SetAsync(_unit, _One("Seats", "5"), FeatureValueProviderNames.Tenant, "t1", false, AbortToken);
        await _feature
            .Received(1)
            .SetAsync(_unit, _One("Api", "true"), FeatureValueProviderNames.Tenant, "t1", true, AbortToken);
        await _feature
            .Received(1)
            .SetAsync(_unit, _One("Sso", "false"), FeatureValueProviderNames.Tenant, "t1", true, AbortToken);
        await _feature
            .Received(1)
            .SetAsync(_unit, _One("Seats", "50"), FeatureValueProviderNames.Edition, "pro", false, AbortToken);
        await _feature.Received(1).DeleteAsync(_unit, "Tenant", "t1", AbortToken);
    }

    [Fact]
    public void should_name_the_registration_when_features_are_not_registered()
    {
        // given
        _unit.GetFeature<IUnitOfWorkFeatures>().Returns((IUnitOfWorkFeatures?)null);

        // when
        var act = () => _unit.Features;

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*AddHeadlessFeatures*");
    }

    private static IReadOnlyDictionary<string, string?> _One(string name, string value)
    {
        return Arg.Is<IReadOnlyDictionary<string, string?>>(x =>
            x.Count == 1 && x.ContainsKey(name) && x[name] == value
        );
    }
}
