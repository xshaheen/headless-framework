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
