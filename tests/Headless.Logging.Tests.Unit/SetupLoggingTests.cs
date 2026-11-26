// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Context;
using Headless.Logging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Enrichment;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed partial class SetupLoggingTests : TestBase
{
    [Fact]
    public void should_add_process_application_and_host_fields_to_every_entry()
    {
        // given
        using var sink = new CapturingProvider();
        using var host = _Build(sink);

        // when
        _Logger(host).LogInformation("hello");

        // then
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry
            .Properties.Keys.Should()
            .Contain([
                "process.pid",
                "thread.id",
                "service.name",
                "deployment.environment",
                "MachineName",
                "ProcessName",
            ]);
        entry.Properties["deployment.environment"].Should().Be("Production");
        entry.Properties["MachineName"].Should().Be(Environment.MachineName);
    }

    [Theory]
    [InlineData("Production", null, ConsoleFormatterNames.Json)]
    [InlineData("Development", null, ConsoleFormatterNames.Simple)]
    [InlineData("Development", HeadlessConsoleFormat.Json, ConsoleFormatterNames.Json)]
    [InlineData("Production", HeadlessConsoleFormat.Text, ConsoleFormatterNames.Simple)]
    public void should_choose_the_console_format_by_environment_unless_pinned(
        string environment,
        HeadlessConsoleFormat? pinned,
        string expectedFormatter
    )
    {
        // given, when
        using var host = _Build(sink: null, environment, new HeadlessLoggingOptions { ConsoleFormat = pinned });

        // then
        host.Services.GetRequiredService<IOptions<ConsoleLoggerOptions>>()
            .Value.FormatterName.Should()
            .Be(expectedFormatter);
    }

    [Fact]
    public void should_write_utc_timestamps_on_both_console_formats()
    {
        // given, when
        using var json = _Build(sink: null, "Production");
        using var text = _Build(sink: null, "Development");

        // then
        json.Services.GetRequiredService<IOptionsMonitor<JsonConsoleFormatterOptions>>()
            .CurrentValue.UseUtcTimestamp.Should()
            .BeTrue();
        text.Services.GetRequiredService<IOptionsMonitor<SimpleConsoleFormatterOptions>>()
            .CurrentValue.UseUtcTimestamp.Should()
            .BeTrue();
    }

    [Fact]
    public void should_carry_the_current_trace_and_span_ids()
    {
        // given
        using var sink = new CapturingProvider();
        using var host = _Build(sink);
        using var activity = new Activity("op");
        activity.SetIdFormat(ActivityIdFormat.W3C).Start();

        // when
        _Logger(host).LogInformation("inside a span");

        // then
        sink.Scopes.SelectMany(s => s)
            .Should()
            .Contain(new KeyValuePair<string, object?>("TraceId", activity.TraceId.ToHexString()))
            .And.Contain(new KeyValuePair<string, object?>("SpanId", activity.SpanId.ToHexString()));
    }

    [Fact]
    public void should_apply_a_changed_log_level_without_a_restart()
    {
        // given
        using var sink = new CapturingProvider();
        using var host = _Build(
            sink,
            configure: builder => builder.Configuration["Logging:LogLevel:Default"] = "Warning"
        );
        var logger = _Logger(host);
        logger.LogInformation("filtered");

        // when
        var configuration = (IConfigurationRoot)host.Services.GetRequiredService<IConfiguration>();
        configuration["Logging:LogLevel:Default"] = "Information";
        configuration.Reload();
        logger.LogInformation("written");

        // then
        sink.Entries.Select(e => e.Message).Should().Equal("written");
    }

    [Fact]
    public void should_resolve_the_redacting_factory_and_still_redact_classified_parameters()
    {
        // given
        using var sink = new CapturingProvider();
        using var host = _Build(sink);

        // when
        LogSignup(_Logger(host), "jane@example.test");

        // then
        host.Services.GetRequiredService<ILoggerFactory>().GetType().Name.Should().Be("RedactingLoggerFactory");
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Properties["Email"].Should().Be(string.Empty);
        entry.Message.Should().NotContain("jane@example.test");
    }

    [Fact]
    public void should_redact_an_enrichment_value_whose_name_is_classified()
    {
        // given
        using var sink = new CapturingProvider();
        using var host = _Build(
            sink,
            configure: builder =>
                builder.Services.AddHeadlessRedaction(setup =>
                    setup
                        .ClassifyProperty("MachineName", HeadlessDataClassifications.Identifier)
                        .ClassifyProperty("service.name", HeadlessDataClassifications.Private)
                )
        );

        // when
        _Logger(host).LogInformation("hello");

        // then
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Properties["MachineName"].Should().Be(string.Empty);
        entry.Properties["service.name"].Should().Be(string.Empty);
        entry.Properties["ProcessName"].Should().NotBe(string.Empty);
    }

    [Fact]
    public void should_redact_a_consumer_enricher_registered_after_the_profile()
    {
        // given
        using var sink = new CapturingProvider();
        using var host = _Build(
            sink,
            configure: builder =>
            {
                builder.Services.AddLogEnricher<EmailEnricher>();
                builder.Services.AddHeadlessRedaction(setup =>
                    setup.ClassifyProperty("Email", HeadlessDataClassifications.Private)
                );
            }
        );

        // when
        _Logger(host).LogInformation("hello");

        // then
        sink.Entries.Should().ContainSingle().Which.Properties["Email"].Should().Be(string.Empty);
    }

    [Fact]
    public void should_keep_redaction_when_microsoft_enrichment_or_redaction_is_enabled_later()
    {
        // given
        using var sink = new CapturingProvider();
        using var host = _Build(
            sink,
            configure: builder =>
            {
                builder.Logging.EnableEnrichment();
                builder.Logging.EnableRedaction();
            }
        );

        // when
        LogSignup(_Logger(host), "jane@example.test");

        // then
        host.Services.GetRequiredService<ILoggerFactory>().GetType().Name.Should().Be("RedactingLoggerFactory");
        sink.Entries.Should().ContainSingle().Which.Message.Should().NotContain("jane@example.test");
    }

    [Fact]
    public void should_create_a_bootstrap_logger_that_logs_and_disposes()
    {
        // given
        using var factory = HeadlessBootstrapLogger.Create(
            new HeadlessLoggingOptions { ConsoleFormat = HeadlessConsoleFormat.Json }
        );
        var logger = factory.CreateLogger("Startup");

        // when
        var act = () => logger.LogCritical(new InvalidOperationException("boom"), "The host terminated");

        // then
        act.Should().NotThrow();
        logger.IsEnabled(LogLevel.Information).Should().BeTrue();
        logger.IsEnabled(LogLevel.Debug).Should().BeFalse();
    }

    [Fact]
    public void should_reject_a_non_positive_header_length()
    {
        // when
        var act = () => new HeadlessLoggingOptions { MaxHeaderLength = 0 };

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_add_the_causation_trace_id_to_entries_written_inside_a_traced_causal_scope_only()
    {
        // given
        using var sink = new CapturingProvider();
        using var host = _Build(sink);
        var logger = _Logger(host);
        var traceId = ActivityTraceId.CreateRandom();

        // when: a consume or job run opens its causal scope with the cause's stored trace id
        logger.LogInformation("before");
        using (CausalScope.Begin("order-7", "run-1", traceId))
        {
            logger.LogInformation("inside");
        }

        using (CausalScope.Begin("order-8", "run-2"))
        {
            logger.LogInformation("untraced");
        }

        // then
        sink.Entries.Should().HaveCount(3);
        sink.Entries[0].Properties.Should().NotContainKey("CausationTraceId");
        sink.Entries[1].Properties["CausationTraceId"].Should().Be(traceId.ToHexString());
        sink.Entries[2].Properties.Should().NotContainKey("CausationTraceId");
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Signup {Email}")]
    private static partial void LogSignup(ILogger logger, [PrivateData] string email);

    private static ILogger _Logger(IHost host) =>
        host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Tests.App");

    private static IHost _Build(
        CapturingProvider? sink,
        string environment = "Production",
        HeadlessLoggingOptions? options = null,
        Action<HostApplicationBuilder>? configure = null
    )
    {
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { DisableDefaults = true, EnvironmentName = environment }
        );
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.AddHeadlessLogging(options);

        if (sink is not null)
        {
            builder.Logging.AddProvider(sink);
        }

        configure?.Invoke(builder);

        return builder.Build();
    }

    private sealed class EmailEnricher : ILogEnricher
    {
        public void Enrich(IEnrichmentTagCollector collector) => collector.Add("Email", "jane@example.test");
    }
}
