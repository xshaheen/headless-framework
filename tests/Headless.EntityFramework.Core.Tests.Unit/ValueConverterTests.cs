// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;
using Headless.EntityFramework.Configurations;
using Headless.Primitives;
using Headless.Testing.Tests;

namespace Tests;

public sealed class ValueConverterTests : TestBase
{
    [Fact]
    public void should_round_trip_string_backed_identifiers()
    {
        var accountConverter = new AccountIdValueConverter();
        var userConverter = new UserIdValueConverter();

        var account = _RoundTrip(accountConverter, new AccountId("account-1"));
        var user = _RoundTrip(userConverter, new UserId("user-1"));

        account.Should().Be(new AccountId("account-1"));
        user.Should().Be(new UserId("user-1"));
    }

    [Fact]
    public void should_round_trip_numeric_primitives_without_losing_precision()
    {
        var amount = _RoundTrip(new MoneyAmountValueConverter(), new MoneyAmount(123.4567890123m));
        var month = _RoundTrip(new MonthValueConverter(), new Month(8));

        amount.Should().Be(new MoneyAmount(123.4567890123m));
        month.Should().Be(new Month(8));
    }

    [Fact]
    public void should_round_trip_reflection_based_json_values()
    {
        var converter = new JsonValueConverter<Payload>();
        var payload = new Payload("alpha", [1, 2, 3]);

        var result = _RoundTrip(converter, payload);

        result.Should().BeEquivalentTo(payload);
    }

    [Fact]
    public void should_round_trip_source_generated_json_values()
    {
        var converter = new JsonValueConverter<Payload, PayloadJsonContext>(PayloadJsonContext.Default.Payload);
        var payload = new Payload("alpha", [1, 2, 3]);

        var result = _RoundTrip(converter, payload);

        result.Should().BeEquivalentTo(payload);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    public void should_read_empty_locale_storage_as_null(string? storedValue)
    {
        var fromProvider = new LocalesValueConverter().ConvertFromProviderExpression.Compile();

        fromProvider(storedValue).Should().BeNull();
    }

    [Fact]
    public void should_round_trip_nested_locales()
    {
        Locales locales = new()
        {
            ["en"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = "Book" },
            ["ar"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = "كتاب" },
        };

        var result = _RoundTrip(new LocalesValueConverter(), locales);

        result.Should().BeEquivalentTo(locales);
    }

    [Fact]
    public void locale_comparer_should_use_deep_order_independent_equality_and_snapshot_the_outer_dictionary()
    {
        var comparer = new LocalesValueComparer();
        Locales first = new()
        {
            ["en"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "Book",
                ["description"] = "Description",
            },
        };
        Locales same = new()
        {
            ["en"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["description"] = "Description",
                ["name"] = "Book",
            },
        };
        var equals = comparer.EqualsExpression.Compile();
        var snapshot = comparer.SnapshotExpression.Compile()(first);

        equals(first, same).Should().BeTrue();
        equals(first, null).Should().BeFalse();
        snapshot.Should().NotBeSameAs(first);
        snapshot!["fr"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = "Livre" };
        first.Should().NotContainKey("fr");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    public void should_read_empty_extra_properties_storage_as_an_empty_bag(string? storedValue)
    {
        var fromProvider = new ExtraPropertiesValueConverter().ConvertFromProviderExpression.Compile();

        fromProvider(storedValue!).Should().BeEmpty();
    }

    [Fact]
    public void should_round_trip_extra_properties_with_inferred_value_types()
    {
        ExtraProperties properties = new() { ["enabled"] = true, ["name"] = "alpha" };

        var result = _RoundTrip(new ExtraPropertiesValueConverter(), properties);

        result.Should().BeEquivalentTo(properties);
        result["enabled"].Should().BeOfType<bool>();
    }

    [Fact]
    public void extra_properties_comparer_should_detect_value_changes_and_create_an_independent_snapshot()
    {
        var comparer = new ExtraPropertiesValueComparer();
        ExtraProperties first = new() { ["name"] = "alpha" };
        ExtraProperties same = new() { ["name"] = "alpha" };
        ExtraProperties changed = new() { ["name"] = "beta" };
        var equals = comparer.EqualsExpression.Compile();
        var snapshot = comparer.SnapshotExpression.Compile()(first);

        equals(first, same).Should().BeTrue();
        equals(first, changed).Should().BeFalse();
        snapshot.Should().NotBeSameAs(first);
        snapshot["name"] = "changed";
        first["name"].Should().Be("alpha");
    }

    private static TModel _RoundTrip<TModel, TProvider>(
        Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<TModel, TProvider> converter,
        TModel value
    )
    {
        var stored = converter.ConvertToProviderExpression.Compile()(value);
        return converter.ConvertFromProviderExpression.Compile()(stored);
    }

    public sealed record Payload(string Name, int[] Values);
}

[JsonSerializable(typeof(ValueConverterTests.Payload))]
internal sealed partial class PayloadJsonContext : JsonSerializerContext;
