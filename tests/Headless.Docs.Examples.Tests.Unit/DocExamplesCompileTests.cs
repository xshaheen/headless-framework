// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// Every <c>csharp</c> block in the consumer guides must compile against the current packages, so a guide cannot
/// drift from the API it documents. A block that cannot stand alone opts out with <c>```csharp no-compile</c>.
/// </summary>
public sealed class DocExamplesCompileTests : TestBase
{
    // Guide (relative to the repository root) and the prelude files its examples compile against.
    private static readonly IReadOnlyDictionary<string, string[]> _Guides = new Dictionary<string, string[]>(
        StringComparer.Ordinal
    )
    {
        ["docs/llms/messaging.md"] = ["Common.cs", "Messaging.cs"],
        ["docs/llms/jobs.md"] = ["Common.cs", "Jobs.cs"],
    };

    private static readonly Lazy<IReadOnlyDictionary<string, DocExample>> _Examples = new(() =>
        _Guides
            .Keys.SelectMany(guide => DocExample.Read(_GuidePath(guide), guide))
            .ToDictionary(example => example.Id, StringComparer.Ordinal)
    );

    public static IEnumerable<TheoryDataRow<string>> Examples =>
        _Examples.Value.Values.Select(example => new TheoryDataRow<string>(example.Id)
        {
            Skip = example.Compiles ? null : $"marked {DocExample.NoCompileMarker}",
        });

    [Theory]
    [MemberData(nameof(Examples))]
    public async Task example_should_compile(string example)
    {
        var block = _Examples.Value[example];
        var preludes = new List<(string Path, string Source)>();

        foreach (var file in _Guides[block.Document])
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Preludes", file);
            preludes.Add((path, await File.ReadAllTextAsync(path, AbortToken)));
        }

        var failures = ExampleCompiler.Compile(block, preludes, AbortToken);

        // One diagnostic per line, each already pointing at the guide's file and line.
        string.Join(Environment.NewLine, failures).Should().BeEmpty($"the example at {block.Id} must compile");
    }

    [Fact]
    public void every_guide_should_have_compiled_examples()
    {
        foreach (var guide in _Guides.Keys)
        {
            _Examples.Value.Values.Should().Contain(example => example.Document == guide && example.Compiles, guide);
        }
    }

    private static string _GuidePath(string guide) =>
        Path.Combine(AppContext.BaseDirectory, "Docs", Path.GetFileName(guide));
}
