// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// The fence reader decides which blocks get compiled, so a parsing mistake would drop examples from the gate without
/// failing anything. These tests pin what it reads, what it skips, and what it rejects.
/// </summary>
public sealed class DocExampleTests : TestBase
{
    private const string _Document = "docs/llms/guide.md";

    private readonly List<string> _paths = [];

    [Fact]
    public async Task should_read_csharp_fences_with_their_document_line_and_skip_other_languages()
    {
        // given
        var path = await _WriteGuideAsync(
            """
            # Guide

            ```bash
            make build
            ```

            ```csharp
            var x = 1;
            ```
            """
        );

        // when
        var examples = DocExample.Read(path, _Document);

        // then
        examples.Should().ContainSingle();
        examples[0].Line.Should().Be(7);
        examples[0].Id.Should().Be($"{_Document}:7");
        examples[0].Code.Should().Be("var x = 1;");
        examples[0].Compiles.Should().BeTrue();
    }

    [Fact]
    public async Task should_strip_the_indentation_of_a_fence_nested_in_a_list()
    {
        // given
        var path = await _WriteGuideAsync(
            """
            - Register the context:

              ```csharp
              services.AddSingleton<Clock>();
                  // nested further
              ```
            """
        );

        // when
        var example = DocExample.Read(path, _Document).Single();

        // then
        example.Code.Should().Be("services.AddSingleton<Clock>();\n    // nested further");
    }

    [Fact]
    public async Task should_mark_a_no_compile_fence_as_not_compiling()
    {
        // given
        var path = await _WriteGuideAsync(
            """
            ```csharp no-compile
            pseudo code
            ```
            """
        );

        // when
        var example = DocExample.Read(path, _Document).Single();

        // then
        example.Compiles.Should().BeFalse();
    }

    [Fact]
    public async Task should_reject_an_unknown_fence_word_naming_its_line()
    {
        // given - a misspelled marker must fail instead of silently opting the block out
        var path = await _WriteGuideAsync(
            """
            text

            ```csharp nocompile
            var x = 1;
            ```
            """
        );

        // when
        var read = () => DocExample.Read(path, _Document);

        // then
        read.Should().Throw<InvalidOperationException>().WithMessage($"{_Document}:3:*'nocompile'*");
    }

    [Fact]
    public async Task should_reject_a_fence_that_is_never_closed()
    {
        // given
        var path = await _WriteGuideAsync(
            """
            ```csharp
            var x = 1;
            """
        );

        // when
        var read = () => DocExample.Read(path, _Document);

        // then
        read.Should().Throw<InvalidOperationException>().WithMessage($"{_Document}:1:*never closed*");
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var path in _paths)
        {
            File.Delete(path);
        }

        await base.DisposeAsyncCore();
    }

    private async Task<string> _WriteGuideAsync(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"doc-example-{Guid.NewGuid():N}.md");
        _paths.Add(path);
        await File.WriteAllTextAsync(path, content, AbortToken);
        return path;
    }
}
