// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Runtime.Loader;
using Headless.Testing.Tests;
using Microsoft.CodeAnalysis;

namespace Tests;

public sealed class DocsExamplesBootTests : TestBase
{
    public static TheoryData<string> Examples { get; } =
    [.. DocsExamples.All.Where(static e => e.Boots).Select(static e => e.Id)];

    [Fact]
    public void should_only_use_known_markers()
    {
        DocsExamples
            .All.Where(static e => e.Marker is not null && !e.IsFragment && !e.Boots)
            .Select(static e => $"{e.Id}: {e.Marker}")
            .Should()
            .BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public async Task should_start_host_of_example(string id)
    {
        var example = DocsExamples.All.Single(e => string.Equals(e.Id, id, StringComparison.Ordinal));
        var (errors, compilation) = DocsExampleCompiler.Compile(example, boot: true);

        string.Join('\n', errors).Should().BeEmpty("the example at docs/llms/{0} must compile", id);

        await using var image = new MemoryStream();
        var emit = compilation.Emit(image, cancellationToken: AbortToken);

        emit.Success.Should()
            .BeTrue(string.Join('\n', emit.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));

        image.Position = 0;
        var context = new AssemblyLoadContext($"docs-example:{id}", isCollectible: true);

        try
        {
            var entryPoint = context.LoadFromStream(image).EntryPoint!;
            // Invoke wraps the example's exception; unwrap it so the failure shows the example's own exception.
            // The entry point of async top-level statements is a synchronous wrapper that blocks until they finish.
            var start = async () =>
            {
                try
                {
                    if (entryPoint.Invoke(null, [Array.Empty<string>()]) is Task running)
                    {
                        await running;
                    }
                }
                catch (TargetInvocationException e) when (e.InnerException is not null)
                {
                    throw e.InnerException;
                }
            };

            await start.Should().NotThrowAsync("the example at docs/llms/{0} must start", id);
        }
        finally
        {
            context.Unload();
        }
    }
}
