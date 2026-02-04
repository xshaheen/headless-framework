// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.Generator.Primitives;

namespace Tests.Extensions;

public sealed class JsonInternalConvertersTests
{
    [Fact]
    public void should_convert_dateonly_json()
    {
        // given
        var converter = JsonInternalConverters.DateOnlyConverter;
        var options = new JsonSerializerOptions();
        options.Converters.Add(converter);
        var dateOnly = new DateOnly(2024, 1, 15);

        // when
        var json = JsonSerializer.Serialize(dateOnly, options);
        var result = JsonSerializer.Deserialize<DateOnly>(json, options);

        // then
        result.Should().Be(dateOnly);
    }

    [Fact]
    public void should_convert_timeonly_json()
    {
        // given
        var converter = JsonInternalConverters.TimeOnlyConverter;
        var options = new JsonSerializerOptions();
        options.Converters.Add(converter);
        var timeOnly = new TimeOnly(14, 30, 45);

        // when
        var json = JsonSerializer.Serialize(timeOnly, options);
        var result = JsonSerializer.Deserialize<TimeOnly>(json, options);

        // then
        result.Should().Be(timeOnly);
    }
}
