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

    [Fact(Skip = ".NET 9 has issue with deserializing list without setter")]
    public void web_options_deserializer_should_populate_list_without_setter()
    {
        var model2 = JsonSerializer.Deserialize<TestModel2>(_Json, _options);
        model2.Should().BeEquivalentTo(_model2);
    }

    private sealed class TestModel1(string name)
    {
        public string Name { get; init; } = name;

        public string? Color { get; init; }

        public List<string> Tags { get; } = [];
    }

    private sealed class TestModel2
    {
        public required string Name { get; init; }

        public string? Color { get; init; }

        public required List<string> Tags { get; init; }
    }
}
