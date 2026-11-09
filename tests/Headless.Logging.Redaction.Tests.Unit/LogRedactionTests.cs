// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Hosting;
using Headless.Logging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed partial class LogRedactionTests : TestBase
{
    private const string _HostingCategory = "Microsoft.AspNetCore.Hosting.Diagnostics";

    [Fact]
    public void should_redact_a_classified_logger_message_parameter_in_text_and_properties()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink, services => services.AddHeadlessRedaction());
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("App");

        // when
        LogOrderPlaced(logger, "alice@example.com", 42);

        // then: the parameter is present with the redactor's output, the text is filled, and the raw value is nowhere
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Text.Should().Be("User  placed order 42");
        entry.Properties.Should().Contain(new KeyValuePair<string, object?>("Email", string.Empty));
        entry.Properties.Should().Contain(new KeyValuePair<string, object?>("OrderId", 42));
        entry.Properties.Select(p => p.Value?.ToString()).Should().NotContain("alice@example.com");
    }

    [Fact]
    public void should_pseudonymize_a_classified_parameter_when_the_redactor_does()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(
            sink,
            services =>
                services.AddHeadlessRedaction(setup =>
                    setup.ConfigureRedaction(redaction =>
                        redaction.SetRedactor<StarRedactor>(
                            new DataClassificationSet(HeadlessDataClassifications.Private)
                        )
                    )
                )
        );
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("App");

        // when
        LogOrderPlaced(logger, "alice@example.com", 42);

        // then
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Text.Should().Be("User ***** placed order 42");
        entry.Properties.Should().Contain(new KeyValuePair<string, object?>("Email", "*****"));
    }

    [Fact]
    public void should_keep_a_null_classified_parameter_null()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink, services => services.AddHeadlessRedaction());
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("App");

        // when
        LogOrderPlaced(logger, email: null, 7);

        // then
        sink.Entries.Should()
            .ContainSingle()
            .Which.Properties.Should()
            .Contain(new KeyValuePair<string, object?>("Email", null));
    }

    [Fact]
    public void should_pass_entries_through_untouched_when_no_redactor_is_registered()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink, _ => { });
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("App");

        // when
        logger.LogInformation("Plain {Value}", 1);

        // then
        sink.Entries.Should().ContainSingle().Which.Text.Should().Be("Plain 1");
    }

    [Fact]
    public void should_rewrite_a_declared_credential_path_in_the_hosting_scope_and_request_lines()
    {
        // given: the hosting layer names the path RequestPath in its scope and Path in its request lines
        using var sink = new CapturingProvider();
        using var provider = _Build(
            sink,
            services => services.AddSingleton<ILogPathRedactor>(new PrefixPathRedactor("/files"))
        );
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(_HostingCategory);

        // when
        using (logger.BeginScope(new LogValues(("RequestPath", "/files/secret-token"), ("RequestId", "0HN"))))
        {
            logger.Log(
                LogLevel.Information,
                default,
                new LogValues(("Path", "/files/secret-token")),
                exception: null,
                (_, _) => "Request starting /files/secret-token"
            );
        }

        // then
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Text.Should().Be("Request starting /files/***");
        entry.Properties.Should().Contain(new KeyValuePair<string, object?>("Path", "/files/***"));
        sink.Scopes.Should()
            .ContainSingle()
            .Which.Should()
            .Contain(new KeyValuePair<string, object?>("RequestPath", "/files/***"))
            .And.Contain(new KeyValuePair<string, object?>("RequestId", "0HN"));
    }

    [Fact]
    public void should_leave_other_categories_and_other_paths_untouched()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(
            sink,
            services => services.AddSingleton<ILogPathRedactor>(new PrefixPathRedactor("/files"))
        );
        var factory = provider.GetRequiredService<ILoggerFactory>();

        // when
        factory
            .CreateLogger(_HostingCategory)
            .Log(LogLevel.Information, default, new LogValues(("Path", "/orders/7")), null, (_, _) => "line");
        factory
            .CreateLogger("App")
            .Log(LogLevel.Information, default, new LogValues(("Path", "/files/token")), null, (_, _) => "app");

        // then
        sink.Entries.Select(e => e.Properties.Single().Value).Should().Equal("/orders/7", "/files/token");
    }

    [Fact]
    public void should_wrap_the_factory_once_when_registered_twice()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(
            sink,
            services =>
            {
                services.AddHeadlessRedaction();
                services.AddHeadlessRedaction();
            }
        );
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("App");

        // when
        LogOrderPlaced(logger, "alice@example.com", 1);

        // then: a double wrap would redact an already-copied state and still pass; the factory chain must stay one deep
        provider.GetRequiredService<ILoggerFactory>().GetType().Name.Should().Be("RedactingLoggerFactory");
        sink.Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task should_warn_at_startup_when_a_later_logger_factory_replaces_the_redacting_one()
    {
        // given: the factory is replaced after AddHeadlessRedaction, as a plain UseSerilog would
        using var sink = new CapturingProvider();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessRedaction();
        services.AddSingleton<ILoggerFactory>(_ => new LoggerFactory([sink]));
        await using var provider = services.BuildServiceProvider();

        // when
        await _RunStartupValidatorsAsync(provider);

        // then
        sink.Entries.Should()
            .ContainSingle(e => e.EventName == "LogRedactionFactoryReplaced")
            .Which.Text.Should()
            .Contain("AddHeadlessSerilog");
    }

    [Fact]
    public async Task should_not_warn_when_the_factory_is_wrapped_or_nothing_is_redacted()
    {
        // given
        using var wrappedSink = new CapturingProvider();
        await using var wrapped = _Build(wrappedSink, services => services.AddHeadlessRedaction());
        using var bareSink = new CapturingProvider();
        await using var bare = _Build(bareSink, _ => { });

        // when
        await _RunStartupValidatorsAsync(wrapped);
        await _RunStartupValidatorsAsync(bare);

        // then
        wrappedSink.Entries.Should().BeEmpty();
        bareSink.Entries.Should().BeEmpty();
    }

    [LoggerMessage(1, LogLevel.Information, "User {Email} placed order {OrderId}")]
    private static partial void LogOrderPlaced(ILogger logger, [PrivateData] string? email, int orderId);

    private static ServiceProvider _Build(CapturingProvider sink, Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders().AddProvider(sink));
        configure(services);
        services.AddHeadlessRedaction();

        return services.BuildServiceProvider();
    }

    private static async Task _RunStartupValidatorsAsync(IServiceProvider provider)
    {
        foreach (var validator in provider.GetServices<IHeadlessStartupValidator>())
        {
            await validator.ValidateAsync(AbortToken);
        }
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

    private sealed class PrefixPathRedactor(string prefix) : ILogPathRedactor
    {
        public bool TryRedact(string path, [NotNullWhen(true)] out string? redacted)
        {
            if (path.StartsWith(prefix + "/", StringComparison.Ordinal))
            {
                redacted = prefix + "/***";

                return true;
            }

            redacted = null;

            return false;
        }
    }

    private sealed class LogValues(params (string Key, object? Value)[] values)
        : IReadOnlyList<KeyValuePair<string, object?>>
    {
        private readonly KeyValuePair<string, object?>[] _values =
        [
            .. values.Select(v => KeyValuePair.Create(v.Key, v.Value)),
        ];

        public int Count => _values.Length;

        public KeyValuePair<string, object?> this[int index] => _values[index];

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
            ((IEnumerable<KeyValuePair<string, object?>>)_values).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed record Entry(
        string? EventName,
        string Text,
        IReadOnlyList<KeyValuePair<string, object?>> Properties
    );

    // Records what a structured provider keeps: the rendered text, and the state's values copied out during the call,
    // which is when the generator's thread-local state is still valid.
    private sealed class CapturingProvider : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider? _scopes;

        public List<Entry> Entries { get; } = [];

        public List<IReadOnlyList<KeyValuePair<string, object?>>> Scopes { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

        public void Dispose() { }

        private sealed class Logger(CapturingProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => owner._scopes?.Push(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                    ? (IReadOnlyList<KeyValuePair<string, object?>>)
                        [.. values.Where(v => !string.Equals(v.Key, "{OriginalFormat}", StringComparison.Ordinal))]
                    : [];

                owner._scopes?.ForEachScope(
                    static (scope, scopes) =>
                    {
                        if (scope is IEnumerable<KeyValuePair<string, object?>> values)
                        {
                            scopes.Add([.. values]);
                        }
                    },
                    owner.Scopes
                );

                owner.Entries.Add(new Entry(eventId.Name, formatter(state, exception), properties));
            }
        }
    }
}
