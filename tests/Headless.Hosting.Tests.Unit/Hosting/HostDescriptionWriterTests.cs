// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Hosting;

[Collection(HostConsoleCollection.Name)]
public sealed class HostDescriptionWriterTests : TestBase
{
    [Fact]
    public async Task should_write_sections_sorted_ordinally_under_the_schema_version()
    {
        // given
        var run = await HostConsoleRun.DescribeAsync(
            builder =>
            {
                builder.Services.AddStubContributor(new StubContributor("messaging"));
                builder.Services.AddStubContributor(new StubContributor("jobs"));
            },
            AbortToken
        );

        // then
        run.ExitCode.Should().Be(0);
        run.StandardOutput.TrimEnd()
            .Should()
            .Be("""{"schemaVersion":1,"sections":{"jobs":{"name":"jobs"},"messaging":{"name":"messaging"}}}""");
    }

    [Fact]
    public async Task should_write_an_empty_sections_object_when_no_feature_contributes()
    {
        // when
        var run = await HostConsoleRun.DescribeAsync(_ => { }, AbortToken);

        // then
        run.ExitCode.Should().Be(0);
        run.StandardOutput.TrimEnd().Should().Be("""{"schemaVersion":1,"sections":{}}""");
        run.StandardError.Should().BeEmpty();
    }

    [Fact]
    public async Task should_leave_stdout_empty_and_fail_when_a_contributor_throws()
    {
        // given
        var after = new StubContributor("messaging");

        // when
        var run = await HostConsoleRun.DescribeAsync(
            builder =>
            {
                builder.Services.AddStubContributor(
                    new StubContributor(
                        "jobs",
                        writer =>
                        {
                            writer.WriteStartObject();
                            throw new InvalidOperationException("The job registry is not frozen.");
                        }
                    )
                );
                builder.Services.AddStubContributor(after);
            },
            AbortToken
        );

        // then
        run.ExitCode.Should().Be(1);
        run.StandardOutput.Should().BeEmpty();
        run.StandardError.Should().Contain("jobs").And.Contain("The job registry is not frozen.");
        run.StandardError.Should().NotContain("   at ", "stderr carries the message, not a stack trace");
    }

    [Fact]
    public async Task should_fail_when_two_contributors_claim_the_same_section()
    {
        // when
        var run = await HostConsoleRun.DescribeAsync(
            builder =>
            {
                builder.Services.AddStubContributor(new StubContributor("jobs"));
                builder.Services.AddStubContributor(new StubContributor("jobs"));
            },
            AbortToken
        );

        // then
        run.ExitCode.Should().Be(1);
        run.StandardOutput.Should().BeEmpty();
        run.StandardError.Should().Contain("jobs");
    }
}
