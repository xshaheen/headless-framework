// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Logging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;

namespace Tests;

public sealed partial class SetupLoggingSerilogTests : TestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void should_redact_a_classified_parameter_whichever_side_of_add_headless_redaction_it_is_called(
        bool redactionFirst
    )
    {
        // given: a library registers redaction before or after the host adds Serilog
        var sink = new CollectingSink();
        using var configuration = new ConfigurationManager();
        var builder = _CreateBuilder(configuration);

        if (redactionFirst)
        {
            builder.Services.AddHeadlessRedaction(_StarPrivateData);
        }

        builder.AddHeadlessSerilog(
            new SerilogOptions { WriteToFiles = false },
            (_, configuration) => configuration.WriteTo.Sink(sink)
        );

        if (!redactionFirst)
        {
            builder.Services.AddHeadlessRedaction(_StarPrivateData);
        }

        using var provider = builder.Services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("App");

        // when
        LogOrderPlaced(logger, "alice@example.com", 42);

        // then: Serilog receives the redacted value as a property and in the rendered text
        var logEvent = sink.Events.Should().ContainSingle().Subject;
        logEvent.Properties["Email"].Should().BeEquivalentTo(new ScalarValue("*****"));
        logEvent.RenderMessage().Should().Contain("*****").And.NotContain("alice");
    }

    [Fact]
    public void should_keep_each_hosts_events_in_its_own_sinks_when_preserving_the_static_logger()
    {
        // given: two hosts side by side in one process, as test hosts are
        var firstSink = new CollectingSink();
        var secondSink = new CollectingSink();
        using var firstConfiguration = new ConfigurationManager();
        using var secondConfiguration = new ConfigurationManager();
        var first = _BuildPreservingHost(firstConfiguration, firstSink);
        using var second = _BuildPreservingHost(secondConfiguration, secondSink);
        var firstFactory = first.GetRequiredService<ILoggerFactory>();
        var secondFactory = second.GetRequiredService<ILoggerFactory>();

        // when: loggers are created after both hosts started, as a request creates them
        firstFactory.CreateLogger("First").LogInformation("from first");
        secondFactory.CreateLogger("Second").LogInformation("from second");
        first.Dispose();
        secondFactory.CreateLogger("Second").LogInformation("after first disposed");

        // then: no host hears the other, and disposing one leaves the other logging
        firstSink.Events.Select(e => e.RenderMessage()).Should().Equal("from first");
        secondSink.Events.Select(e => e.RenderMessage()).Should().Equal("from second", "after first disposed");
    }

    [Fact]
    public void should_make_the_host_logger_the_static_logger_by_default()
    {
        // given
        var sink = new CollectingSink();
        using var configuration = new ConfigurationManager();
        var builder = _CreateBuilder(configuration);
        builder.AddHeadlessSerilog(
            new SerilogOptions { WriteToFiles = false },
            (_, loggerConfiguration) => loggerConfiguration.WriteTo.Sink(sink)
        );

        try
        {
            using var provider = builder.Services.BuildServiceProvider();
            _ = provider.GetRequiredService<ILoggerFactory>();

            // when
            Serilog.Log.Information("through the static logger");

            // then
            sink.Events.Select(e => e.RenderMessage()).Should().Equal("through the static logger");
        }
        finally
        {
            Serilog.Log.Logger = Serilog.Core.Logger.None;
        }
    }

    [Fact]
    public void should_throw_when_the_builder_is_null()
    {
        var call = () => ((IHostApplicationBuilder)null!).AddHeadlessSerilog();

        call.Should().Throw<ArgumentNullException>();
    }

    [LoggerMessage(1, LogLevel.Information, "User {Email} placed order {OrderId}")]
    private static partial void LogOrderPlaced(ILogger logger, [PrivateData] string email, int orderId);

    private static void _StarPrivateData(HeadlessRedactionSetupBuilder setup) =>
        setup.ConfigureRedaction(redaction =>
            redaction.SetRedactor<StarRedactor>(new DataClassificationSet(HeadlessDataClassifications.Private))
        );

    private static IHostApplicationBuilder _CreateBuilder(IConfigurationManager configuration)
    {
        var builder = Substitute.For<IHostApplicationBuilder>();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        environment.ApplicationName.Returns("Tests");
        builder.Services.Returns(new ServiceCollection().AddLogging());
        builder.Configuration.Returns(configuration);
        builder.Environment.Returns(environment);

        return builder;
    }

    private static ServiceProvider _BuildPreservingHost(IConfigurationManager configuration, CollectingSink sink)
    {
        var builder = _CreateBuilder(configuration);
        builder.AddHeadlessSerilog(
            new SerilogOptions { WriteToFiles = false },
            (_, loggerConfiguration) => loggerConfiguration.WriteTo.Sink(sink),
            preserveStaticLogger: true
        );

        return builder.Services.BuildServiceProvider();
    }

    private sealed class StarRedactor : Redactor
    {
        public override int GetRedactedLength(ReadOnlySpan<char> input) => 5;

        public override int Redact(ReadOnlySpan<char> source, Span<char> destination)
        {
            "*****".AsSpan().CopyTo(destination);

            return 5;
        }
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
