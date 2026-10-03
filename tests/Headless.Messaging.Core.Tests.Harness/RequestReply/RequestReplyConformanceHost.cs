// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Registration;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Sdk;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests.RequestReply;

/// <summary>
/// One messaging host of a request/reply scenario, standing for one process: a caller that sends requests, or a
/// responder that answers them. Storage is in-memory on both, so only the driver's transport varies between providers.
/// </summary>
internal sealed class RequestReplyConformanceHost : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private RequestReplyConformanceHost(ServiceProvider services)
    {
        _services = services;
        Probe = services.GetRequiredService<RequestReplyConformanceProbe>();
    }

    public IServiceProvider Services => _services;

    public RequestReplyConformanceProbe Probe { get; }

    public static Task<RequestReplyConformanceHost> StartCallerAsync(
        TransportProviderConformanceDriver driver,
        RequestReplyConformanceContracts contracts,
        CancellationToken cancellationToken
    ) => _StartAsync(BuildCaller(driver, contracts), cancellationToken);

    public static Task<RequestReplyConformanceHost> StartResponderAsync(
        TransportProviderConformanceDriver driver,
        RequestReplyConformanceContracts contracts,
        CancellationToken cancellationToken
    ) => _StartAsync(BuildResponder(driver, contracts), cancellationToken);

    public static ServiceProvider BuildCaller(
        TransportProviderConformanceDriver driver,
        RequestReplyConformanceContracts contracts
    ) => _Build(driver, contracts, isCaller: true);

    public static ServiceProvider BuildResponder(
        TransportProviderConformanceDriver driver,
        RequestReplyConformanceContracts contracts
    ) => _Build(driver, contracts, isCaller: false);

    public Task<ConformanceQuote> RequestAsync<TRequest>(
        TRequest request,
        RequestOptions options,
        CancellationToken cancellationToken
    )
        where TRequest : class
    {
        return Services
            .GetRequiredService<IRequestClient>()
            .RequestAsync<TRequest, ConformanceQuote>(request, options, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return _services.DisposeAsync();
    }

    private static async Task<RequestReplyConformanceHost> _StartAsync(
        ServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await services.GetRequiredService<IBootstrapper>().BootstrapAsync(cancellationToken);
            return new RequestReplyConformanceHost(services);
        }
        catch
        {
            await services.DisposeAsync();
            throw;
        }
    }

    private static ServiceProvider _Build(
        TransportProviderConformanceDriver driver,
        RequestReplyConformanceContracts contracts,
        bool isCaller
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<RequestReplyConformanceProbe>();
        services.ConfigureMessaging(messaging =>
        {
            contracts.Name(messaging);

            if (!isCaller)
            {
                messaging.AddModule<RequestReplyConformanceModule>();
            }
        });
        services.AddHeadlessMessaging(setup =>
        {
            driver.ConfigureRequestReplyTransport(setup);
            setup.UseInMemoryStorage();
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;

            if (isCaller)
            {
                setup.AddRequestReply();
            }
        });
        driver.ConfigureRequestReplyServices(services);

        if (!isCaller)
        {
            _RecordReplySends(services);
        }

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Wraps the provider's reply transport so the scenario sees every address the responder sent a reply to. The
    /// provider's own registration moves under a private key, so the container still owns and disposes it.
    /// </summary>
    private static void _RecordReplySends(IServiceCollection services)
    {
        var registered = services.LastOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IReplyTransport) && !descriptor.IsKeyedService
        );

        if (registered is null)
        {
            return;
        }

        const string innerKey = "request-reply-conformance:provider";
        services.Remove(registered);
        services.Add(
            registered switch
            {
                { ImplementationInstance: { } instance } => new ServiceDescriptor(
                    typeof(IReplyTransport),
                    innerKey,
                    instance
                ),
                { ImplementationFactory: { } factory } => new ServiceDescriptor(
                    typeof(IReplyTransport),
                    innerKey,
                    (provider, _) => factory(provider),
                    registered.Lifetime
                ),
                _ => new ServiceDescriptor(
                    typeof(IReplyTransport),
                    innerKey,
                    registered.ImplementationType!,
                    registered.Lifetime
                ),
            }
        );
        services.AddSingleton<IReplyTransport>(provider => new RecordingReplyTransport(
            provider.GetRequiredKeyedService<IReplyTransport>(innerKey),
            provider.GetRequiredService<RequestReplyConformanceProbe>()
        ));
    }

    private sealed class RecordingReplyTransport(IReplyTransport inner, RequestReplyConformanceProbe probe)
        : IReplyTransport
    {
        public bool IsReplyAddress(string address)
        {
            return inner.IsReplyAddress(address);
        }

        public ValueTask<IReplyListener> OpenListenerAsync(
            Func<TransportMessage, CancellationToken, ValueTask> onReply,
            CancellationToken cancellationToken = default
        )
        {
            return inner.OpenListenerAsync(onReply, cancellationToken);
        }

        public async ValueTask SendAsync(
            string address,
            TransportMessage reply,
            CancellationToken cancellationToken = default
        )
        {
            try
            {
                await inner.SendAsync(address, reply, cancellationToken);
            }
            finally
            {
                probe.RepliesSentTo.Enqueue(address);
            }
        }
    }
}

/// <summary>
/// The contract names of one scenario run. Every run gets fresh names, so runs sharing a broker never meet, and the
/// caller and responder hosts of one run agree on them.
/// </summary>
internal sealed class RequestReplyConformanceContracts
{
    private readonly string _run = Guid.NewGuid().ToString("N");

    /// <summary>The Queue destination a forged request names as its reply address.</summary>
    public string Audit => _Name("audit");

    public void Name(MessagingContributionBuilder messaging)
    {
        messaging.Message<ConformanceQuoteRequest>(_Name("quote"));
        messaging.Message<ConformanceFailingRequest>(_Name("failing"));
        messaging.Message<ConformanceHeldRequest>(_Name("held"));
        messaging.Message<ConformancePlainRequest>(_Name("plain"));
        messaging.Message<ConformanceAuditMessage>(Audit);
        messaging.Message<ConformanceQuote>(_Name("response"));
    }

    private string _Name(string suffix)
    {
        return $"conformance.rr.{_run}.{suffix}";
    }
}

/// <summary>What a responder host observed: the requests its handlers ran, and the replies it sent.</summary>
internal sealed class RequestReplyConformanceProbe
{
    public ConcurrentQueue<string> Handled { get; } = new();

    public ConcurrentQueue<ConformanceReceivedRequest> Requests { get; } = new();

    public ConcurrentQueue<string> Audits { get; } = new();

    public ConcurrentQueue<string> RepliesSentTo { get; } = new();

    public TaskCompletionSource HeldStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource HeldRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Record(string entry, ConsumeContext context)
    {
        Requests.Enqueue(new ConformanceReceivedRequest(context.Headers, context.TenantId));
        Handled.Enqueue(entry);
    }
}

/// <summary>A request as the responder received it.</summary>
internal sealed record ConformanceReceivedRequest(MessageHeader Headers, string? TenantId)
{
    public string ReplyTo =>
        Headers.TryGetValue(MessagingHeaders.ReplyTo, out var replyTo) && replyTo is not null
            ? replyTo
            : throw new InvalidOperationException("The request carried no reply address.");
}

/// <summary>Records the reasons the request/reply instruments give for dropping a reply, in this process.</summary>
internal sealed class RequestReplyDropRecorder : IDisposable
{
    private readonly MeterListener _listener = new();

    public RequestReplyDropRecorder()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (
                string.Equals(instrument.Meter.Name, MessagingDiagnostics.SourceName, StringComparison.Ordinal)
                && string.Equals(
                    instrument.Name,
                    MessagingMetrics.RequestReplyDroppedRepliesName,
                    StringComparison.Ordinal
                )
            )
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>(
            (_, _, tags, _) =>
            {
                foreach (var tag in tags)
                {
                    if (
                        string.Equals(tag.Key, MessagingMetrics.TagRequestReplyDropReason, StringComparison.Ordinal)
                        && tag.Value is string reason
                    )
                    {
                        Reasons.Enqueue(reason);
                    }
                }
            }
        );
        _listener.Start();
    }

    public ConcurrentQueue<string> Reasons { get; } = new();

    public void Dispose()
    {
        _listener.Dispose();
    }
}

/// <summary>Bounded waits for outcomes another host produces asynchronously.</summary>
internal static class RequestReplyConformanceWait
{
    // Generous enough for a broker under parallel test load; a scenario that holds reaches its condition in far less.
    private static readonly TimeSpan _Bound = TimeSpan.FromSeconds(20);

    public static Task UntilAsync(Func<bool> condition, string what, CancellationToken cancellationToken)
    {
        return UntilAsync(_ => ValueTask.FromResult(condition()), what, cancellationToken);
    }

    public static async Task UntilAsync(
        Func<CancellationToken, ValueTask<bool>> condition,
        string what,
        CancellationToken cancellationToken
    )
    {
        using var bound = _Bound.ToCancellationTokenSource(cancellationToken);

        try
        {
            while (!await condition(bound.Token))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), bound.Token);
            }
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw new XunitException($"Gave up after {_Bound} waiting until {what}.", e);
        }
    }

    public static async Task ForAsync(Task task, string what, CancellationToken cancellationToken)
    {
        try
        {
            await task.WaitAsync(_Bound, cancellationToken);
        }
        catch (TimeoutException e)
        {
            throw new XunitException($"Gave up after {_Bound} waiting until {what}.", e);
        }
    }
}
