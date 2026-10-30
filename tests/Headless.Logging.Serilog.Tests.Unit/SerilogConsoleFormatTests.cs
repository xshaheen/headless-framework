// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Tests;

// These tests point the process console and the environment variables at test values, and both are shared by every
// test in the assembly, so they run alone.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleCollection
{
    public const string Name = "Process console";
}

[Collection(ConsoleCollection.Name)]
public sealed class SerilogConsoleFormatTests
{
    private const string _Message = "Order {OrderId} shipped";

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Test")]
    public void should_write_compact_json_when_reloadable_format_is_unset_outside_development(string environmentName)
    {
        var output = _CaptureReloadable(environmentName, new SerilogOptions { WriteToFiles = false });

        var line = JsonDocument.Parse(output.Trim()).RootElement;
        line.GetProperty("@mt").GetString().Should().Be(_Message);
        line.GetProperty("OrderId").GetInt32().Should().Be(42);
        line.GetProperty("Application").GetString().Should().Be("Serilog.Console.Tests");
    }

    [Fact]
    public void should_write_text_when_reloadable_format_is_unset_in_development()
    {
        var output = _CaptureReloadable(Environments.Development, new SerilogOptions { WriteToFiles = false });

        output.Should().Contain("INF").And.Contain("Order 42 shipped");
        output.TrimStart().Should().NotStartWith("{");
    }

    [Fact]
    public void should_write_text_when_reloadable_format_is_text_outside_development()
    {
        var options = new SerilogOptions { WriteToFiles = false, ConsoleFormat = SerilogConsoleFormat.Text };

        var output = _CaptureReloadable(Environments.Production, options);

        output.Should().Contain("Order 42 shipped");
        output.TrimStart().Should().NotStartWith("{");
    }

    [Fact]
    public void should_write_json_when_reloadable_format_is_json_in_development()
    {
        var options = new SerilogOptions { WriteToFiles = false, ConsoleFormat = SerilogConsoleFormat.Json };

        var output = _CaptureReloadable(Environments.Development, options);

        JsonDocument.Parse(output.Trim()).RootElement.GetProperty("@mt").GetString().Should().Be(_Message);
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("Production", null, true)]
    [InlineData(null, "Production", true)]
    [InlineData("Development", null, false)]
    [InlineData(null, "Development", false)]
    [InlineData("Production", "Development", true)]
    [InlineData("Development", "Production", false)]
    public void should_pick_bootstrap_format_from_environment_variables_when_format_is_unset(
        string? aspNetCoreEnvironment,
        string? dotnetEnvironment,
        bool expectJson
    )
    {
        var output = _CaptureBootstrap(aspNetCoreEnvironment, dotnetEnvironment, new SerilogOptions());

        if (expectJson)
        {
            JsonDocument.Parse(output.Trim()).RootElement.GetProperty("@mt").GetString().Should().Be(_Message);
        }
        else
        {
            output.Should().Contain("Order 42 shipped");
            output.TrimStart().Should().NotStartWith("{");
        }
    }

    [Fact]
    public void should_use_bootstrap_format_option_over_environment_variables()
    {
        var output = _CaptureBootstrap(
            aspNetCoreEnvironment: "Production",
            dotnetEnvironment: null,
            new SerilogOptions { ConsoleFormat = SerilogConsoleFormat.Text }
        );

        output.Should().Contain("Order 42 shipped");
        output.TrimStart().Should().NotStartWith("{");
    }

    private static string _CaptureReloadable(string environmentName, SerilogOptions options)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.ApplicationName.Returns("Serilog.Console.Tests");
        environment.EnvironmentName.Returns(environmentName);

        return _Capture(() =>
            new LoggerConfiguration().ConfigureReloadableLoggerConfiguration(
                services: null,
                new ConfigurationBuilder().Build(),
                environment,
                options
            )
        );
    }

    private static string _CaptureBootstrap(
        string? aspNetCoreEnvironment,
        string? dotnetEnvironment,
        SerilogOptions options
    )
    {
        var originalAspNetCore = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var originalDotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", aspNetCoreEnvironment);
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", dotnetEnvironment);

        try
        {
            var bootstrapOptions = new SerilogOptions
            {
                ConsoleFormat = options.ConsoleFormat,
                LogDirectory = Path.Combine(Path.GetTempPath(), "headless-serilog-tests", Guid.NewGuid().ToString("N")),
            };

            return _Capture(() => new LoggerConfiguration().ConfigureBootstrapLoggerConfiguration(bootstrapOptions));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", originalAspNetCore);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", originalDotnet);
        }
    }

    // The console sink writes through Console.Out, so the writer is swapped before the logger is built and the
    // logger is disposed before the output is read, which flushes it.
    private static string _Capture(Func<LoggerConfiguration> configure)
    {
        var originalOut = Console.Out;
        using var stdout = new StringWriter();
        Console.SetOut(stdout);

        try
        {
            using (var logger = configure().CreateLogger())
            {
                logger.Information(_Message, 42);
            }

            return stdout.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
