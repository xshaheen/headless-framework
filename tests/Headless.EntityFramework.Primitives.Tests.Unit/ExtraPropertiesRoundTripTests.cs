// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Primitives;
using Headless.Testing.Tests;

namespace Tests;

public sealed class ExtraPropertiesRoundTripTests : TestBase
{
    [Fact]
    public void should_read_back_string_that_looks_like_a_date_after_round_trip()
    {
        var entity = _RoundTrip(new Entity().SetProperty("code", "2024-01-01"));

        entity.GetProperty<string>("code").Should().Be("2024-01-01");
        entity.GetProperty("code").Should().Be("2024-01-01");
    }

    [Fact]
    public void should_read_back_utc_date_time_after_round_trip()
    {
        var value = new DateTime(2024, 5, 6, 7, 8, 9, 123, DateTimeKind.Utc);

        var result = _RoundTrip(new Entity().SetProperty("at", value)).GetProperty<DateTime>("at");

        result.Should().Be(value);
        result.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void should_read_back_unspecified_date_time_without_shifting_it_after_round_trip()
    {
        var value = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Unspecified);

        var result = _RoundTrip(new Entity().SetProperty("at", value)).GetProperty<DateTime>("at");

        result.Should().Be(value);
        result.Kind.Should().Be(DateTimeKind.Unspecified);
    }

    [Fact]
    public void should_read_back_date_time_offset_with_its_offset_after_round_trip()
    {
        var value = new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.FromHours(3));

        var result = _RoundTrip(new Entity().SetProperty("at", value)).GetProperty<DateTimeOffset>("at");

        result.EqualsExact(value).Should().BeTrue();
    }

    [Fact]
    public void should_read_back_nullable_date_time_offset_after_round_trip()
    {
        var value = new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero);

        var result = _RoundTrip(new Entity().SetProperty("at", value)).GetProperty<DateTimeOffset?>("at");

        result.Should().Be(value);
    }

    [Fact]
    public void should_read_back_time_span_after_round_trip()
    {
        var value = new TimeSpan(1, 2, 3, 4, 5);

        _RoundTrip(new Entity().SetProperty("duration", value)).GetProperty<TimeSpan>("duration").Should().Be(value);
    }

    [Fact]
    public void should_read_back_decimal_without_losing_precision_after_round_trip()
    {
        const decimal value = 12345678901234567.89m;

        _RoundTrip(new Entity().SetProperty("amount", value)).GetProperty<decimal>("amount").Should().Be(value);
    }

    [Fact]
    public void should_read_back_double_after_round_trip()
    {
        const double value = 0.30000000000000004;

        _RoundTrip(new Entity().SetProperty("ratio", value)).GetProperty<double>("ratio").Should().Be(value);
    }

    [Fact]
    public void should_read_back_guid_after_round_trip()
    {
        var value = Guid.NewGuid();

        _RoundTrip(new Entity().SetProperty("id", value)).GetProperty<Guid>("id").Should().Be(value);
    }

    [Fact]
    public void should_read_back_enum_after_round_trip()
    {
        _RoundTrip(new Entity().SetProperty("status", Status.InProgress))
            .GetProperty<Status>("status")
            .Should()
            .Be(Status.InProgress);
    }

    [Fact]
    public void should_read_back_integers_and_booleans_after_round_trip()
    {
        var entity = _RoundTrip(new Entity().SetProperty("count", 42).SetProperty("enabled", true));

        entity.GetProperty<int>("count").Should().Be(42);
        entity.GetProperty<long>("count").Should().Be(42L);
        entity.GetProperty<bool>("enabled").Should().BeTrue();
    }

    [Fact]
    public void should_return_default_value_when_property_is_missing()
    {
        new Entity().GetProperty("missing", 7).Should().Be(7);
    }

    [Fact]
    public void should_format_non_string_values_with_the_invariant_culture_when_reading_a_string()
    {
        var entity = _RoundTrip(new Entity().SetProperty("count", 42).SetProperty("amount", 1.5m));

        entity.GetProperty<string>("count").Should().Be("42");
        entity.GetProperty<string>("amount").Should().Be("1.5");
    }

    [Fact]
    public void should_convert_an_in_memory_date_time_to_date_time_offset()
    {
        var value = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);

        new Entity().SetProperty("at", value).GetProperty<DateTimeOffset>("at").Should().Be(new DateTimeOffset(value));
    }

    [Fact]
    public void should_throw_when_reading_a_non_primitive_type()
    {
        var entity = new Entity().SetProperty("tags", new[] { "a" });

        var act = () => entity.GetProperty<string[]>("tags");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_convert_json_elements_from_a_bag_deserialized_without_the_inferring_converter()
    {
        // A bag deserialized by plain System.Text.Json (for example from an API request body) holds JsonElement values.
        var bag = JsonSerializer.Deserialize<ExtraProperties>(
            """{"name":"alpha","count":3,"amount":1.25,"enabled":true,"off":false,"at":"2024-01-01T00:00:00Z","nothing":null,"obj":{"a":1}}"""
        )!;
        var entity = new Entity(bag);

        entity.GetProperty<string>("name").Should().Be("alpha");
        entity.GetProperty<int>("count").Should().Be(3);
        entity.GetProperty<decimal>("amount").Should().Be(1.25m);
        entity.GetProperty<bool>("enabled").Should().BeTrue();
        entity.GetProperty<DateTime>("at").Should().Be(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        entity.GetProperty<string>("nothing", "fallback").Should().Be("fallback");
        entity.GetProperty<bool>("off").Should().BeFalse();
        entity.GetProperty<string>("obj").Should().Be("""{"a":1}""");
    }

    private static Entity _RoundTrip(Entity entity)
    {
        var converter = new ExtraPropertiesValueConverter();
        var stored = converter.ConvertToProviderExpression.Compile()(entity.ExtraProperties);

        return new Entity(converter.ConvertFromProviderExpression.Compile()(stored));
    }

    private enum Status
    {
        Pending,
        InProgress,
    }

    private sealed class Entity(ExtraProperties? extraProperties = null) : IHasExtraProperties
    {
        public ExtraProperties ExtraProperties { get; } = extraProperties ?? [];
    }
}
