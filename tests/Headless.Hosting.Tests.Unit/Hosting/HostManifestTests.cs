// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Tests.Hosting;

[Collection(HostConsoleCollection.Name)]
public sealed class HostManifestTests : TestBase
{
    [Fact]
    public async Task should_write_sections_sorted_ordinally_under_the_schema_version()
    {
        // given
        var run = await HostConsoleRun.DescribeAsync(
            builder =>
            {
                builder.Services.AddStubSection(new StubSection("messaging"));
                builder.Services.AddStubSection(new StubSection("jobs"));
            },
            AbortToken
        );

        // then
        run.ExitCode.Should().Be(0);
        using var document = run.ParseStandardOutput();
        document.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(HostManifest.SchemaVersion);
        _SectionNames(document).Should().Equal("healthChecks", "host", "jobs", "messaging");
        document
            .RootElement.GetProperty("sections")
            .GetProperty("jobs")
            .GetRawText()
            .Should()
            .Be("""{"name":"jobs"}""");
    }

    [Fact]
    public async Task should_write_only_the_built_in_sections_when_no_feature_registers_one()
    {
        // when
        var run = await HostConsoleRun.DescribeAsync(_ => { }, AbortToken);

        // then
        run.ExitCode.Should().Be(0);
        using var document = run.ParseStandardOutput();
        _SectionNames(document).Should().Equal("healthChecks", "host");
        run.StandardError.Should().BeEmpty();
    }

    [Fact]
    public async Task should_describe_the_application_and_runtime_in_the_host_section()
    {
        // when
        var run = await HostConsoleRun.DescribeAsync(
            builder => builder.Environment.EnvironmentName = "Staging",
            AbortToken
        );

        // then
        using var document = run.ParseStandardOutput();
        var host = document.RootElement.GetProperty("sections").GetProperty("host");
        host.GetProperty("environment").GetString().Should().Be("Staging");
        host.GetProperty("applicationName").GetString().Should().NotBeNullOrEmpty();
        host.GetProperty("headlessVersion").GetString().Should().NotBeNullOrEmpty();
        host.GetProperty("runtime").GetString().Should().StartWith(".NET");
    }

    [Fact]
    public async Task should_list_each_registered_health_check_with_its_tags_failure_status_and_timeout()
    {
        // when
        var run = await HostConsoleRun.DescribeAsync(
            builder =>
            {
                builder.Services.AddHeadlessHealthCheck(
                    "search-elastic",
                    static (_, _) => Task.CompletedTask,
                    "search"
                );
                builder.Services.ConfigureHeadlessHealthCheck(
                    "search-elastic",
                    options =>
                    {
                        options.FailureStatus = HealthStatus.Degraded;
                        options.Timeout = TimeSpan.FromSeconds(2);
                    }
                );
            },
            AbortToken
        );

        // then
        using var document = run.ParseStandardOutput();
        var check = document.RootElement.GetProperty("sections").GetProperty("healthChecks")[0];
        check.GetProperty("name").GetString().Should().Be("search-elastic");
        check
            .GetProperty("tags")
            .EnumerateArray()
            .Select(static x => x.GetString())
            .Should()
            .Equal(HeadlessHealthCheckTags.Headless, HeadlessHealthCheckTags.Ready, "search");
        check.GetProperty("failureStatus").GetString().Should().Be("degraded");
        check.GetProperty("timeoutSeconds").GetDouble().Should().Be(2);
    }

    private static IEnumerable<string> _SectionNames(JsonDocument document) =>
        document.RootElement.GetProperty("sections").EnumerateObject().Select(static x => x.Name);

    [Fact]
    public async Task should_leave_stdout_empty_and_fail_when_a_section_throws()
    {
        // given
        var after = new StubSection("messaging");

        // when
        var run = await HostConsoleRun.DescribeAsync(
            builder =>
            {
                builder.Services.AddStubSection(
                    new StubSection(
                        "jobs",
                        writer =>
                        {
                            writer.WriteStartObject();
                            throw new InvalidOperationException("The job registry is not frozen.");
                        }
                    )
                );
                builder.Services.AddStubSection(after);
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
    public async Task should_fail_when_two_sections_share_a_name()
    {
        // when
        var run = await HostConsoleRun.DescribeAsync(
            builder =>
            {
                builder.Services.AddStubSection(new StubSection("jobs"));
                builder.Services.AddStubSection(new StubSection("jobs"));
            },
            AbortToken
        );

        // then
        run.ExitCode.Should().Be(1);
        run.StandardOutput.Should().BeEmpty();
        run.StandardError.Should().Contain("jobs");
    }
}
