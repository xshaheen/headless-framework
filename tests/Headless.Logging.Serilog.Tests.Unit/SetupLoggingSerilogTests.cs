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
    private const string _NationalId = "29001011234567";

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
    public void should_keep_each_hosts_events_in_its_own_sinks_by_default()
    {
        // given: two hosts side by side in one process, as test hosts are
        var firstSink = new CollectingSink();
        var secondSink = new CollectingSink();
        using var firstConfiguration = new ConfigurationManager();
        using var secondConfiguration = new ConfigurationManager();
        var first = _BuildHost(firstConfiguration, firstSink);
        using var second = _BuildHost(secondConfiguration, secondSink);
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
    public void should_make_the_host_logger_the_static_logger_when_not_preserving_it()
    {
        // given
        var sink = new CollectingSink();
        using var configuration = new ConfigurationManager();
        var builder = _CreateBuilder(configuration);
        builder.AddHeadlessSerilog(
            new SerilogOptions { WriteToFiles = false },
            (_, loggerConfiguration) => loggerConfiguration.WriteTo.Sink(sink),
            preserveStaticLogger: false
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

    [Fact]
    public void should_mask_events_written_through_serilogs_own_logger()
    {
        // given
        var sink = new CollectingSink();
        using var configuration = new ConfigurationManager();
        using var provider = _BuildMaskingHost(configuration, sink);
        var logger = provider.GetRequiredService<Serilog.ILogger>().ForContext("Citizen", _NationalId);
        Exception thrown;

        try
        {
            throw new InvalidOperationException("lookup failed", new FormatException($"no citizen {_NationalId}"));
        }
        catch (InvalidOperationException e)
        {
            thrown = e;
        }

        // when: an interpolated message, a structured value, a context property, and an exception
#pragma warning disable CA2254, Serilog004 // The test logs an interpolated message on purpose: it is the shape only text masking reaches.
        logger.Information($"lookup failed for {_NationalId}");
#pragma warning restore CA2254, Serilog004
        logger.Error(thrown, "Order {Order} for {Id} failed", 7, 29001011234567L);

        // then: every place the id could appear is masked, and the exception keeps its type and frames
        sink.Events.Should().HaveCount(2);
        sink.Events[0].RenderMessage().Should().Be("lookup failed for ***");
        sink.Events[0].Properties["Citizen"].Should().BeEquivalentTo(new ScalarValue("***"));
        var failure = sink.Events[1];
        failure.Properties["Id"].Should().BeEquivalentTo(new ScalarValue("***"));
        failure.Properties["Order"].Should().BeEquivalentTo(new ScalarValue(7));
        var exception = failure.Exception.Should().BeOfType<InvalidOperationException>().Subject;
        exception.Should().NotBeSameAs(thrown);
        exception.InnerException!.Message.Should().Be("no citizen ***");
        exception.StackTrace.Should().Be(thrown.StackTrace);
    }

    [Fact]
    public void should_pass_an_event_logged_through_the_logger_factory_without_a_second_copy()
    {
        // given
        var sink = new CollectingSink();
        using var configuration = new ConfigurationManager();
        using var provider = _BuildMaskingHost(configuration, sink);
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("App");

        // when
        LogLookup(logger, _NationalId);

        // then: the redacting factory already masked it on its way to Serilog
        sink.Events.Should().ContainSingle().Which.RenderMessage().Should().Be("Lookup for \"***\"");
    }

    [Fact]
    public void should_leave_serilogs_own_logger_unwrapped_without_a_text_redactor()
    {
        // given
        var sink = new CollectingSink();
        using var configuration = new ConfigurationManager();
        using var provider = _BuildHost(configuration, sink);

        // when
        provider.GetRequiredService<Serilog.ILogger>().Information("Citizen {Id}", _NationalId);

        // then
        provider.GetRequiredService<Serilog.ILogger>().GetType().Name.Should().NotBe("MaskingSerilogLogger");
        sink.Events.Should()
            .ContainSingle()
            .Which.Properties["Id"]
            .Should()
            .BeEquivalentTo(new ScalarValue(_NationalId));
    }

    [LoggerMessage(2, LogLevel.Information, "Lookup for {Id}")]
    private static partial void LogLookup(ILogger logger, string id);

    private static ServiceProvider _BuildMaskingHost(IConfigurationManager configuration, CollectingSink sink)
    {
        var builder = _CreateBuilder(configuration);
        builder.Services.AddHeadlessRedaction(static setup => setup.MaskText(@"\b\d{14}\b"));
        builder.AddHeadlessSerilog(
            new SerilogOptions { WriteToFiles = false },
            (_, loggerConfiguration) => loggerConfiguration.WriteTo.Sink(sink)
        );

        return builder.Services.BuildServiceProvider();
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

    private static ServiceProvider _BuildHost(IConfigurationManager configuration, CollectingSink sink)
    {
        var builder = _CreateBuilder(configuration);
        builder.AddHeadlessSerilog(
            new SerilogOptions { WriteToFiles = false },
            (_, loggerConfiguration) => loggerConfiguration.WriteTo.Sink(sink)
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
