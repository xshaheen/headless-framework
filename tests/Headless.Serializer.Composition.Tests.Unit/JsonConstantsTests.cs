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

        json.Should().Be("""{"OrderId":7,"Status":"pendingPayment"}""");
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
            .Be("""{"order_id":7,"status":"paid"}""");

        var act = () => JsonSerializer.Deserialize<WireOrder>("""{"order_id":7,"status":"paid","extra":1}""", options);

        act.Should().Throw<JsonException>();
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
