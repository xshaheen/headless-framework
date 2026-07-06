// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace Tests;

public sealed class SerilogFactoryTests
{
    [Fact]
    public void SerilogOptions_should_use_documented_defaults()
    {
        var options = new SerilogOptions();

        options.WriteToFiles.Should().BeTrue();
        options.LogDirectory.Should().Be("Logs");
        options.Buffered.Should().BeTrue();
        options.FlushToDiskInterval.Should().Be(TimeSpan.FromSeconds(1));
        options.RollingInterval.Should().Be(RollingInterval.Day);
        options.RetainedFileCountLimit.Should().Be(5);
        options.MaxHeaderLength.Should().Be(512);
    }

    [Fact]
    public void ConfigureBootstrapLoggerConfiguration_should_return_same_configuration_instance()
    {
        var loggerConfiguration = new LoggerConfiguration();
        var options = new SerilogOptions { LogDirectory = _CreateTempLogDirectory() };

        var result = loggerConfiguration.ConfigureBootstrapLoggerConfiguration(options);

        result.Should().BeSameAs(loggerConfiguration);
        using var logger = loggerConfiguration.CreateLogger();
        logger.Should().NotBeNull();
    }

    [Fact]
    public void ConfigureReloadableLoggerConfiguration_should_return_same_configuration_instance()
    {
        var loggerConfiguration = new LoggerConfiguration();
        var configuration = new ConfigurationBuilder().Build();
        var environment = Substitute.For<IHostEnvironment>();
        environment.ApplicationName.Returns("Headless.Logging.Serilog.Tests.Unit");
        environment.EnvironmentName.Returns(Environments.Production);

        var result = loggerConfiguration.ConfigureReloadableLoggerConfiguration(
            services: null,
            configuration,
            environment,
            new SerilogOptions { WriteToFiles = false }
        );

        result.Should().BeSameAs(loggerConfiguration);
        using var logger = loggerConfiguration.CreateLogger();
        logger.Should().NotBeNull();
    }

    private static string _CreateTempLogDirectory() =>
        Path.Combine(Path.GetTempPath(), "headless-serilog-tests", Guid.NewGuid().ToString("N"));
}
