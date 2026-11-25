// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Logging.Benchmarks;

/// <summary>
/// Cost of one log entry through the redacting logger factory against the plain one. The provider renders every
/// message, as a console or OTLP exporter would, so the cost of re-rendering a redacted message is counted.
/// </summary>
[MemoryDiagnoser]
public partial class RedactingLoggerBenchmarks
{
    private readonly Order _order = new(42, "open");
    private readonly Customer _customer = new("jane@example.test", "gold");

    private ServiceProvider _plainServices = null!;
    private ServiceProvider _redactingServices = null!;
    private ILogger _plain = null!;
    private ILogger _redacting = null!;

    [GlobalSetup]
    public void Setup()
    {
        _plainServices = _Build(redact: false);
        _redactingServices = _Build(redact: true);
        _plain = _plainServices.GetRequiredService<ILoggerFactory>().CreateLogger("App");
        _redacting = _redactingServices.GetRequiredService<ILoggerFactory>().CreateLogger("App");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _plainServices.Dispose();
        _redactingServices.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void ScalarGeneratedPlain() => LogPlaced(_plain, 42, "open");

    [Benchmark]
    public void ScalarGeneratedRedacting() => LogPlaced(_redacting, 42, "open");

#pragma warning disable CA1848, CA1873 // The extension-method call is measured on purpose: most app code logs through it.
    [Benchmark]
    public void ScalarTemplatePlain() => _plain.LogInformation("Placed {Id} as {State}", 42, "open");

    [Benchmark]
    public void ScalarTemplateRedacting() => _redacting.LogInformation("Placed {Id} as {State}", 42, "open");

    [Benchmark]
    public void SafeObjectPlain() => _plain.LogInformation("Placed {@Order}", _order);

    [Benchmark]
    public void SafeObjectRedacting() => _redacting.LogInformation("Placed {@Order}", _order);

    [Benchmark]
    public void ClassifiedObjectPlain() => _plain.LogInformation("Saw {@Customer}", _customer);

    [Benchmark]
    public void ClassifiedObjectRedacting() => _redacting.LogInformation("Saw {@Customer}", _customer);
#pragma warning restore CA1848, CA1873

    [Benchmark]
    public void SafePropertiesPlain() => LogOrder(_plain, _order);

    [Benchmark]
    public void SafePropertiesRedacting() => LogOrder(_redacting, _order);

    [Benchmark]
    public void ClassifiedPropertiesPlain() => LogCustomer(_plain, _customer);

    [Benchmark]
    public void ClassifiedPropertiesRedacting() => LogCustomer(_redacting, _customer);

    [LoggerMessage(1, LogLevel.Information, "Placed {Id} as {State}")]
    private static partial void LogPlaced(ILogger logger, int id, string state);

    [LoggerMessage(2, LogLevel.Information, "Placed order")]
    private static partial void LogOrder(ILogger logger, [LogProperties] Order order);

    [LoggerMessage(3, LogLevel.Information, "Saw customer")]
    private static partial void LogCustomer(ILogger logger, [LogProperties] Customer customer);

    private static ServiceProvider _Build(bool redact)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders().AddProvider(new RenderingProvider()));

        if (redact)
        {
            services.AddHeadlessRedaction();
        }

        return services.BuildServiceProvider();
    }

    public sealed record Order(int Id, string State);

    public sealed record Customer([property: PrivateData] string Email, string Tier);

    /// <summary>Renders each message and keeps only its length, so the work cannot be optimized away.</summary>
    private sealed class RenderingProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new RenderingLogger();

        public void Dispose() { }

        private sealed class RenderingLogger : ILogger
        {
            public int Rendered { get; private set; }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            ) => Rendered += formatter(state, exception).Length;
        }
    }
}
