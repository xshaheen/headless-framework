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
        ["docs/llms/dashboards.md"] = ["Common.cs", "Dashboards.cs"],
        ["docs/llms/encryption.md"] = ["Common.cs", "Encryption.cs"],
    };

    // Every compiled document: each entry guide plus the topic files under docs/llms/<domain>/, which share its preludes.
    private static readonly Lazy<IReadOnlyDictionary<string, string[]>> _Documents = new(() =>
    {
        var documents = new Dictionary<string, string[]>(_Guides, StringComparer.Ordinal);

        foreach (var (guide, preludes) in _Guides)
        {
            var domain = Path.GetFileNameWithoutExtension(guide);
            var folder = Path.Combine(AppContext.BaseDirectory, "Docs", domain);

            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var topic in Directory.EnumerateFiles(folder, "*.md").Order(StringComparer.Ordinal))
            {
                documents[$"docs/llms/{domain}/{Path.GetFileName(topic)}"] = preludes;
            }
        }

        return documents;
    });

    private static readonly Lazy<IReadOnlyDictionary<string, DocExample>> _Examples = new(() =>
        _Documents
            .Value.Keys.SelectMany(document => DocExample.Read(_DocumentPath(document), document))
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

        foreach (var file in _Documents.Value[block.Document])
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
        // A split guide may keep its examples in topic files, so a domain passes when any of its documents compiles one.
        foreach (var guide in _Guides.Keys)
        {
            var topicPrefix = $"docs/llms/{Path.GetFileNameWithoutExtension(guide)}/";

            _Examples
                .Value.Values.Should()
                .Contain(
                    example =>
                        (
                            example.Document == guide
                            || example.Document.StartsWith(topicPrefix, StringComparison.Ordinal)
                        ) && example.Compiles,
                    guide
                );
        }
    }

    // A document id is its path under the repository root; the build copies docs/llms/** to Docs/**.
    private static string _DocumentPath(string document) =>
        Path.Combine(AppContext.BaseDirectory, "Docs", document["docs/llms/".Length..]);
}
