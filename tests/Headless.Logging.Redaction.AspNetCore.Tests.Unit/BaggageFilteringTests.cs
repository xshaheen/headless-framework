// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Headless.Hosting;
using Headless.Logging;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Trace;

namespace Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PropagatorStaticsCollection
{
    public const string Name = "Propagator statics";
}

// Every test installs the filter on the process-wide propagators, which it saves and restores. The set of classified
// keys is process-wide and cannot be cleared, so each test classifies a key of its own.
[Collection(PropagatorStaticsCollection.Name)]
public sealed class BaggageFilteringTests : TestBase
{
    // Reading the default before the SDK's static constructor runs would save the no-op propagator it later replaces.
    private static readonly TextMapPropagator _SdkDefault = _RunSdkStaticConstructor();

    private readonly DistributedContextPropagator _runtime = DistributedContextPropagator.Current;
    private readonly TextMapPropagator _textMap = Propagators.DefaultTextMapPropagator;
    private readonly string _key = "user.email." + Guid.NewGuid().ToString("N");

    protected override ValueTask DisposeAsyncCore()
    {
        DistributedContextPropagator.Current = _runtime;
        Sdk.SetDefaultTextMapPropagator(_textMap);

        return base.DisposeAsyncCore();
    }

    [Fact]
    public void should_drop_a_classified_key_from_activity_baggage_injected_by_the_runtime_propagator()
    {
        // given
        using var provider = _Classify(_key);
        using var activity = _StartWithBaggage(("leak.marker", "m-1"), (_key.ToUpperInvariant(), "jane@example.test"));

        // when
        var carrier = _InjectRuntime(activity);

        // then: the trace context and the unclassified entry still travel; the key matched ignoring case is gone
        carrier.Should().ContainKey("traceparent");
        _Entries(carrier["baggage"]).Should().Equal("leak.marker=m-1");
    }

    [Fact]
    public void should_omit_the_baggage_header_when_every_entry_is_classified()
    {
        // given
        using var provider = _Classify(_key);
        using var activity = _StartWithBaggage((_key, "jane@example.test"));

        // when
        var carrier = _InjectRuntime(activity);

        // then
        carrier.Should().ContainKey("traceparent").And.NotContainKey("baggage");
    }

    [Fact]
    public void should_drop_a_classified_key_from_opentelemetry_baggage_and_keep_a_custom_propagator_field()
    {
        // given: the app set its own propagator before the tracer that installs the filter is built
        Sdk.SetDefaultTextMapPropagator(
            new CompositeTextMapPropagator([
                new TraceContextPropagator(),
                new BaggagePropagator(),
                new TenantPropagator(),
            ])
        );
        using var provider = _Classify(_key);
        _BuildTracer();
        var baggage = Baggage.Create().SetBaggage("leak.marker", "m-2").SetBaggage(_key, "jane@example.test");

        // when
        var carrier = _InjectTextMap(baggage);

        // then
        carrier.Should().ContainKey("traceparent").And.Contain("x-tenant", "t-1");
        carrier["baggage"].Should().Be("leak.marker=m-2");
    }

    [Fact]
    public void should_extract_incoming_baggage_unchanged()
    {
        // given
        Sdk.SetDefaultTextMapPropagator(_SdkDefault);
        using var provider = _Classify(_key);
        _BuildTracer();
        var carrier = new Dictionary<string, string>(StringComparer.Ordinal) { ["baggage"] = $"{_key}=jane" };

        // when
        var context = Propagators.DefaultTextMapPropagator.Extract(
            default,
            carrier,
            static (headers, name) => headers.TryGetValue(name, out var value) ? [value] : []
        );

        // then
        context.Baggage.GetBaggage(_key).Should().Be("jane");
    }

    [Fact]
    public void should_drop_the_keys_both_hosts_classified()
    {
        // given
        var otherKey = _key + ".other";
        using var provider = _Classify(_key);
        using var other = _Classify(otherKey);
        using var activity = _StartWithBaggage(("leak.marker", "m-3"), (_key, "a"), (otherKey, "b"));

        // when
        var carrier = _InjectRuntime(activity);

        // then
        _Entries(carrier["baggage"]).Should().Equal("leak.marker=m-3");
    }

    [Fact]
    public async Task should_send_no_classified_baggage_from_an_http_client_created_after_registration()
    {
        // given: a host that echoes what it received
        await using var provider = _Classify(_key);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var app = builder.Build();
        app.MapGet(
            "/",
            static (HttpRequest request) =>
                $"traceparent={request.Headers.TraceParent};baggage={request.Headers.Baggage}"
        );
        await app.StartAsync(AbortToken);

        using var listener = _ListenToEverySource();
        using var handler = new SocketsHttpHandler();
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri(app.Urls.Single()) };
        using var activity = _StartWithBaggage(("leak.marker", "m-4"), (_key, "jane@example.test"));

        // when
        var echoed = await client.GetStringAsync("/", AbortToken);

        // then
        echoed.Should().Contain("traceparent=00-");
        _Entries(echoed[(echoed.IndexOf(";baggage=", StringComparison.Ordinal) + 9)..])
            .Should()
            .Equal("leak.marker=m-4");
    }

    [Fact]
    public async Task should_warn_at_startup_when_the_opentelemetry_propagator_was_replaced_after_installation()
    {
        // given
        await using var provider = _Classify(_key);
        _BuildTracer();
        Sdk.SetDefaultTextMapPropagator(new TraceContextPropagator());

        // when
        await _RunStartupValidatorsAsync(provider);

        // then
        _Sink(provider)
            .Messages.Should()
            .ContainSingle(m => m.Contains("Propagators.DefaultTextMapPropagator", StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_not_warn_while_both_propagators_filter()
    {
        // given
        await using var provider = _Classify(_key);
        _BuildTracer();

        // when
        await _RunStartupValidatorsAsync(provider);

        // then
        _Sink(provider).Messages.Should().NotContain(m => m.Contains("Classified baggage", StringComparison.Ordinal));
    }

    private static TextMapPropagator _RunSdkStaticConstructor()
    {
        RuntimeHelpers.RunClassConstructor(typeof(Sdk).TypeHandle);

        return Propagators.DefaultTextMapPropagator;
    }

    // The runtime propagator writes "key = value"; entries are compared without that spacing.
    private static IEnumerable<string> _Entries(string header) =>
        header
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static e => e.Replace(" ", "", StringComparison.Ordinal));

    private static ServiceProvider _Classify(string key)
    {
        var services = new ServiceCollection();
        services.AddSingleton<MessageSink>();
        services.AddLogging(logging =>
            logging.ClearProviders().Services.AddSingleton<ILoggerProvider>(p => p.GetRequiredService<MessageSink>())
        );
        services.AddHeadlessRedaction(setup => setup.ClassifyProperty(key, HeadlessDataClassifications.Private));

        return services.BuildServiceProvider();
    }

    private static MessageSink _Sink(IServiceProvider provider) => provider.GetRequiredService<MessageSink>();

    private static void _BuildTracer()
    {
        // Building the tracer is what installs the filter on OpenTelemetry's default propagator.
        using var tracer = Sdk.CreateTracerProviderBuilder()
            .ConfigureServices(static services => services.AddHeadlessRedaction())
            .AddHeadlessTextRedaction()
            .Build();
    }

    private static async Task _RunStartupValidatorsAsync(IServiceProvider provider)
    {
        foreach (var validator in provider.GetServices<IHeadlessStartupValidator>())
        {
            await validator.ValidateAsync(AbortToken);
        }
    }

#pragma warning disable CA2000 // False positive: the activity is returned to the caller, which disposes it.
    private static Activity _StartWithBaggage(params (string Key, string Value)[] items)
    {
        var activity = new Activity("outgoing");
        activity.SetIdFormat(ActivityIdFormat.W3C);

        foreach (var (key, value) in items)
        {
            activity.AddBaggage(key, value);
        }

        return activity.Start();
    }
#pragma warning restore CA2000

    private static Dictionary<string, string> _InjectRuntime(Activity activity)
    {
        var carrier = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        DistributedContextPropagator.Current.Inject(
            activity,
            carrier,
            static (target, name, value) => ((Dictionary<string, string>)target!)[name] = value
        );

        return carrier;
    }

    private static Dictionary<string, string> _InjectTextMap(Baggage baggage)
    {
        using var activity = _StartWithBaggage();
        var carrier = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Propagators.DefaultTextMapPropagator.Inject(
            new PropagationContext(activity.Context, baggage),
            carrier,
            static (headers, name, value) => headers[name] = value
        );

        return carrier;
    }

    private static ActivityListener _ListenToEverySource()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = static _ => true,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        return listener;
    }

    private sealed class TenantPropagator : TextMapPropagator
    {
        public override ISet<string> Fields { get; } = new HashSet<string>(StringComparer.Ordinal) { "x-tenant" };

        public override void Inject<T>(PropagationContext context, T carrier, Action<T, string, string> setter) =>
            setter(carrier, "x-tenant", "t-1");

        public override PropagationContext Extract<T>(
            PropagationContext context,
            T carrier,
            Func<T, string, IEnumerable<string>?> getter
        ) => context;
    }

    private sealed class MessageSink : ILoggerProvider
    {
        private readonly Lock _lock = new();
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_lock)
                {
                    return [.. _messages];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose() { }

        private sealed class Logger(MessageSink owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                lock (owner._lock)
                {
                    owner._messages.Add(formatter(state, exception));
                }
            }
        }
    }
}
