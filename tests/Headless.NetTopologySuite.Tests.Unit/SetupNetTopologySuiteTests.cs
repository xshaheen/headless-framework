// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.NetTopologySuite;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace Tests;

public sealed class SetupNetTopologySuiteTests
{
    [Fact]
    public void should_install_the_framework_services_as_the_process_default_and_register_them()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessNetTopologySuite();
        using var provider = services.BuildServiceProvider();

        // then
        NtsGeometryServices.Instance.Should().BeSameAs(GeoServices.NtsGeometryServices);
        provider.GetRequiredService<NtsGeometryServices>().Should().BeSameAs(GeoServices.NtsGeometryServices);
        provider.GetRequiredService<GeometryFactory>().Should().BeSameAs(GeoServices.GeometryFactory);
        provider.GetRequiredService<GeometryFactory>().SRID.Should().Be(GeoConstants.GoogleMapsSrid);
    }
}
