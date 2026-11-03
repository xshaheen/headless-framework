using Headless.Serializer;

namespace Tests;

public sealed class JsonConstantsTests
{
    private readonly JsonSerializerOptions _options = JsonConstants.CreateWebJsonOptions();
    private const string _Json = """{"name":"Name","color":"Red","tags":["C#","JS"]}""";
    private readonly TestModel1 _model1 = new("Name") { Color = "Red", Tags = { "C#", "JS" } };

    private readonly TestModel2 _model2 = new()
    {
        Name = "Name",
        Color = "Red",
        Tags = ["C#", "JS"],
    };

    [Fact]
    public void web_options_serializer()
    {
        JsonSerializer.Serialize(_model1, _options).Should().Be(_Json);
        JsonSerializer.Serialize(_model2, _options).Should().Be(_Json);
    }

    [Fact]
    public void web_options_deserializer()
    {
        var model1 = JsonSerializer.Deserialize<TestModel2>(_Json, _options);

        model1.Should().BeEquivalentTo(_model1);
    }

    [Fact]
    public void should_populate_list_without_setter_when_web_options_deserializer()
    {
        var model2 = JsonSerializer.Deserialize<TestModel2>(_Json, _options);
        model2.Should().BeEquivalentTo(_model2);
    }

    [Fact]
    public void should_be_read_only_when_shared_presets()
    {
        JsonConstants.DefaultWebJsonOptions.IsReadOnly.Should().BeTrue();
        JsonConstants.DefaultInternalJsonOptions.IsReadOnly.Should().BeTrue();
        JsonConstants.DefaultStorageJsonOptions.IsReadOnly.Should().BeTrue();
        JsonConstants.DefaultPrettyJsonOptions.IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public void should_throw_when_mutating_a_shared_preset()
    {
        var mutate = () => JsonConstants.DefaultWebJsonOptions.WriteIndented = true;

        mutate.Should().ThrowExactly<InvalidOperationException>();
    }

    [Fact]
    public void should_return_mutable_instances_when_create_factories()
    {
        JsonConstants.CreateWebJsonOptions().IsReadOnly.Should().BeFalse();
        JsonConstants.CreateInternalJsonOptions().IsReadOnly.Should().BeFalse();
        JsonConstants.CreateStorageJsonOptions().IsReadOnly.Should().BeFalse();
        JsonConstants.CreatePrettyJsonOptions().IsReadOnly.Should().BeFalse();
    }

    [Fact]
    public void should_apply_naming_policy_to_members_and_enums_when_internal_options_have_one()
    {
        var options = JsonConstants.CreateInternalJsonOptions(JsonNamingPolicy.SnakeCaseLower);
        var order = new WireOrder
        {
            OrderId = 7,
            Status = WireOrderStatus.PendingPayment,
            Metadata = new(StringComparer.Ordinal) { ["KeepMe"] = "1" },
        };

        var json = JsonSerializer.Serialize(order, options);

        json.Should().Be("""{"order_id":7,"status":"pending_payment","metadata":{"KeepMe":"1"}}""");
        JsonSerializer.Deserialize<WireOrder>(json, options).Should().BeEquivalentTo(order);
    }

    [Fact]
    public void should_keep_clr_names_and_camel_case_enums_when_internal_options_have_no_naming_policy()
    {
        var json = JsonSerializer.Serialize(
            new WireOrder { OrderId = 7, Status = WireOrderStatus.PendingPayment },
            JsonConstants.DefaultInternalJsonOptions
        );

        json.Should().Be("""{"OrderId":7,"Status":"pendingPayment","Metadata":null}""");
    }

    [Fact]
    public void should_apply_enum_naming_to_enum_dictionary_keys_when_internal_options_have_naming_policy()
    {
        var options = JsonConstants.CreateInternalJsonOptions(JsonNamingPolicy.SnakeCaseLower);
        var counts = new Dictionary<WireOrderStatus, int> { [WireOrderStatus.PendingPayment] = 1 };

        var json = JsonSerializer.Serialize(counts, options);

        json.Should().Be("""{"pending_payment":1}""");
        JsonSerializer.Deserialize<Dictionary<WireOrderStatus, int>>(json, options).Should().Equal(counts);
    }

    [Theory]
    [InlineData("""{"order_id":7,"status":"paid","extra":1}""")]
    [InlineData("""{"orderId":7,"status":"paid"}""")]
    [InlineData("""{"order_id":"7","status":"paid"}""")]
    [InlineData("""{"order_id":7,"status":1}""")]
    [InlineData("""{"order_id":7,"order_id":8,"status":"paid"}""")]
    [InlineData("""{"order_id":7,"status":"paid",}""")]
    [InlineData("""{"order_id":7,/* c */"status":"paid"}""")]
    public void should_reject_payload_when_internal_snake_case_contract_is_violated(string json)
    {
        var options = JsonConstants.CreateInternalJsonOptions(JsonNamingPolicy.SnakeCaseLower);

        var act = () => JsonSerializer.Deserialize<WireOrder>(json, options);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void should_read_type_discriminator_in_any_position_when_internal_options()
    {
        // PostgreSQL jsonb stores keys in its own order, which can put the discriminator last.
        var shape = JsonSerializer.Deserialize<WireShape>(
            """{"Radius":2,"$type":"circle"}""",
            JsonConstants.DefaultInternalJsonOptions
        );

        shape.Should().BeOfType<WireCircle>().Which.Radius.Should().Be(2);
    }

    [Fact]
    public void should_use_source_generated_metadata_when_internal_options_configured_with_context_resolver()
    {
        var options = JsonConstants.ConfigureInternalJsonOptions(
            new JsonSerializerOptions { TypeInfoResolver = WireJsonContext.Default },
            JsonNamingPolicy.SnakeCaseLower
        );

        JsonSerializer
            .Serialize(new WireOrder { OrderId = 7, Status = WireOrderStatus.Paid }, options)
            .Should()
            .Be("""{"order_id":7,"status":"paid","metadata":null}""");

        var act = () => JsonSerializer.Deserialize<WireOrder>("""{"order_id":7,"status":"paid","extra":1}""", options);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void should_reject_repeated_property_when_web_options()
    {
        var act = () =>
            JsonSerializer.Deserialize<TestModel2>(
                """{"name":"First","name":"Second","tags":[]}""",
                JsonConstants.DefaultWebJsonOptions
            );

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void should_read_type_discriminator_in_any_position_when_web_options()
    {
        var shape = JsonSerializer.Deserialize<WireShape>(
            """{"radius":2,"$type":"circle"}""",
            JsonConstants.DefaultWebJsonOptions
        );

        shape.Should().BeOfType<WireCircle>().Which.Radius.Should().Be(2);
    }

    [Fact]
    public void should_write_dictionary_keys_as_given_when_web_options()
    {
        var json = JsonSerializer.Serialize(
            new Dictionary<string, int>(StringComparer.Ordinal) { ["Admin"] = 1, ["TENANT-A"] = 2 },
            JsonConstants.DefaultWebJsonOptions
        );

        json.Should().Be("""{"Admin":1,"TENANT-A":2}""");
    }

    [Fact]
    public void should_throw_when_web_options_write_a_reference_cycle()
    {
        var node = new CycleNode();
        node.Next = node;

        var act = () => JsonSerializer.Serialize(node, JsonConstants.DefaultWebJsonOptions);

        act.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("web")]
    [InlineData("internal")]
    public void should_read_object_values_as_json_element_when_web_or_internal_options(string preset)
    {
        var options = string.Equals(preset, "web", StringComparison.Ordinal)
            ? JsonConstants.DefaultWebJsonOptions
            : JsonConstants.DefaultInternalJsonOptions;

        var value = JsonSerializer.Deserialize<Dictionary<string, object?>>("""{"Count":1,"Tags":["a"]}""", options);

        value.Should().NotBeNull();
        value!["Count"].Should().BeOfType<JsonElement>();
        value["Tags"].Should().BeOfType<JsonElement>();
    }

    [Fact]
    public void should_skip_unknown_members_when_storage_options()
    {
        // A newer node wrote "Added"; an older node that does not know it must still read the value.
        var order = JsonSerializer.Deserialize<WireOrder>(
            """{"OrderId":7,"Status":"paid","Added":true}""",
            JsonConstants.DefaultStorageJsonOptions
        );

        order.Should().NotBeNull();
        order!.OrderId.Should().Be(7);
    }

    [Fact]
    public void should_reject_unknown_members_when_internal_options()
    {
        var act = () =>
            JsonSerializer.Deserialize<WireOrder>(
                """{"OrderId":7,"Status":"paid","Added":true}""",
                JsonConstants.DefaultInternalJsonOptions
            );

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void should_read_object_values_as_inferred_primitives_when_storage_options()
    {
        var value = JsonSerializer.Deserialize<Dictionary<string, object?>>(
            """{"Count":1,"Name":"a","On":true,"Tags":["a"]}""",
            JsonConstants.DefaultStorageJsonOptions
        );

        value.Should().NotBeNull();
        value!["Count"].Should().Be(1L);
        value["Name"].Should().Be("a");
        value["On"].Should().Be(true);
        value["Tags"].Should().BeOfType<JsonElement>();
    }

    [Fact]
    public void should_return_stored_shape_when_round_tripping_through_storage_options()
    {
        var declared = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Count"] = 1,
            ["Status"] = WireOrderStatus.PendingPayment,
        };

        var stored = JsonConstants.DefaultStorageJsonOptions.RoundTrip(declared);

        stored.Should().NotBeNull();
        stored!["Count"].Should().Be(1L);
        stored["Status"].Should().Be("pendingPayment");
    }

    [Theory]
    [InlineData("storage")]
    [InlineData("internal")]
    public void should_round_trip_a_null_constructor_parameter(string preset)
    {
        // A cache item built through its constructor: dropping the null on write would fail the read, since every
        // constructor parameter is required.
        var options = string.Equals(preset, "storage", StringComparison.Ordinal)
            ? JsonConstants.DefaultStorageJsonOptions
            : JsonConstants.DefaultInternalJsonOptions;
        var json = JsonSerializer.Serialize(new CachedValue(null), options);

        var result = JsonSerializer.Deserialize<CachedValue>(json, options);

        json.Should().Be("""{"Value":null}""");
        result.Should().NotBeNull();
        result!.Value.Should().BeNull();
    }

    private sealed class CachedValue(string? value)
    {
        public string? Value { get; } = value;
    }

    private sealed class CycleNode
    {
        public CycleNode? Next { get; set; }
    }

    private sealed class TestModel1(string name)
    {
        public string Name { get; init; } = name;

        public string? Color { get; init; }

        // ReSharper disable once CollectionNeverQueried.Local
        public List<string> Tags { get; } = [];
    }

    private sealed class TestModel2
    {
        public required string Name { get; init; }

        public string? Color { get; init; }

        public required List<string> Tags { get; init; }
    }
}

public sealed class WireOrder
{
    public int OrderId { get; init; }

    public WireOrderStatus Status { get; init; }

    public Dictionary<string, string>? Metadata { get; init; }
}

public enum WireOrderStatus
{
    Paid = 0,
    PendingPayment = 1,
}

[JsonPolymorphic]
[JsonDerivedType(typeof(WireCircle), "circle")]
public abstract class WireShape;

public sealed class WireCircle : WireShape
{
    public int Radius { get; init; }
}

[JsonSerializable(typeof(WireOrder))]
internal sealed partial class WireJsonContext : JsonSerializerContext;
