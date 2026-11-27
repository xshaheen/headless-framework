// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Logging;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Buffering;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>
/// A Debug entry an app keeps in Microsoft's global log buffer, to emit only when something goes wrong, reaches the
/// provider when the buffer is flushed, under the Headless logging profile, exactly as it would without it.
/// </summary>
public sealed class LogBufferingTests : TestBase
{
    private const string _Message = "buffered debug detail";

    [Fact]
    public void should_deliver_a_buffered_debug_entry_on_flush_when_the_provider_itself_starts_at_information()
    {
        // given: the filter lets Debug through to the buffer, and the provider enables Information and above only
        using var sink = new LevelSink(LogLevel.Information);
        using var host = _Build(sink, filterMinimum: LogLevel.Debug);
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Tests.Buffer");

        // when
        logger.LogDebug(_Message);
        sink.Messages.Should().BeEmpty("a buffered entry waits for the flush");
        host.Services.GetRequiredService<GlobalLogBuffer>().Flush();

        // then
        sink.Messages.Should().ContainSingle().Which.Should().Be(_Message);
    }

    [Fact]
    public void should_keep_the_tenant_once_on_a_buffered_entry_whose_template_has_a_tenant_hole()
    {
        // given
        using var sink = new LevelSink(LogLevel.Information);
        using var host = _Build(sink, filterMinimum: LogLevel.Debug);
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Tests.Buffer");

        // when: enrichment runs when the entry is buffered, so the duplicate check must see it then
        using (host.Services.GetRequiredService<ICurrentTenant>().Change("acme"))
        {
#pragma warning disable CA1848, CA2254 // The message-template overload is what the test exercises.
            logger.LogDebug("Switching to tenant '{TenantId}'", "globex");
#pragma warning restore CA1848, CA2254
        }

        host.Services.GetRequiredService<GlobalLogBuffer>().Flush();

        // then
        var keys = sink.Keys.Should().ContainSingle().Subject;
        keys.Count(k => string.Equals(k, "TenantId", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public void should_deliver_no_debug_entry_when_the_logging_filter_drops_debug_before_the_buffer()
    {
        // given: the filter's minimum is Information, so Debug never reaches the buffer, with or without the profile
        using var sink = new LevelSink(LogLevel.Trace);
        using var host = _Build(sink, filterMinimum: LogLevel.Information);
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Tests.Buffer");

        // when
        logger.LogDebug(_Message);
        host.Services.GetRequiredService<GlobalLogBuffer>().Flush();

        // then
        sink.Messages.Should().BeEmpty();
    }

    private static IHost _Build(LevelSink sink, LogLevel filterMinimum)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.AddHeadlessLogging();
        builder.Logging.ClearProviders().AddProvider(sink).SetMinimumLevel(filterMinimum);
        builder.Logging.AddGlobalBuffer(LogLevel.Debug);
        builder.Services.AddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
        builder.Services.AddSingleton<ICurrentTenant, CurrentTenant>();

        return builder.Build();
    }

    /// <summary>A provider that enables only levels at or above its own minimum, as a sink with its own threshold does.</summary>
    private sealed class LevelSink(LogLevel minimum) : ILoggerProvider, ILogger
    {
        private readonly ConcurrentQueue<(string Message, IReadOnlyList<string> Keys)> _entries = new();

        public IReadOnlyList<string> Messages => [.. _entries.Select(static e => e.Message)];

        public IReadOnlyList<IReadOnlyList<string>> Keys => [.. _entries.Select(static e => e.Keys)];

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimum;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            List<string> keys = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? [.. pairs.Select(static p => p.Key)]
                : [];
            _entries.Enqueue((formatter(state, exception), keys));
        }

        public void Dispose() { }
    }
}
