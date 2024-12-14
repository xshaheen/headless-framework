// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.BuildingBlocks;
using Framework.Serializer.Json.Converters;
using NetTopologySuite.IO.Converters;

namespace Tests.Constants;

public sealed class FrameworkJsonConstantsTests
{
    [Fact]
    public void create_web_json_options_should_include_default_converters()
    {
        // when
        var options = FrameworkJsonConstants.CreateWebJsonOptions();

        // then
        options.Converters.Should().Contain(c => c is GeoJsonConverterFactory);
        options.Converters.Should().Contain(c => c is IpAddressJsonConverter);
    }
}
