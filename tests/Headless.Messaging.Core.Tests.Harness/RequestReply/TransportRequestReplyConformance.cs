// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Serialization;
using Microsoft.Extensions.DependencyInjection;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests.RequestReply;

/// <summary>
/// The request/reply outcomes every provider that declares the capability must show, and the startup rejection every
/// other provider must show. Each scenario runs a caller host and a responder host, standing for two services, against
/// one broker. The driver contributes only the broker wiring and a probe of the reply objects the broker holds.
/// </summary>
/// <remarks>
/// Every scenario names its contracts after a fresh run id, so scenarios sharing one broker never consume each other's
/// requests, and a destination a scenario leaves behind is never reused.
/// </remarks>
[PublicAPI]
public static class TransportRequestReplyConformance
{
    // Long enough for a request and its reply to cross any broker under test load; a call that should succeed never
    // gets near it.
    private static readonly RequestOptions _Patient = new() { Timeout = TimeSpan.FromSeconds(20) };

    // Long enough for a held responder to start before the caller gives up, and short enough to keep the scenario quick.
    private static readonly RequestOptions _Impatient = new() { Timeout = TimeSpan.FromSeconds(3) };

    /// <summary>A request reaches the responder once and its typed response completes the call.</summary>
    public static async Task AssertRoundTripAsync(
        TransportProviderConformanceDriver driver,
        CancellationToken cancellationToken
    )
    {
        _RequireSupport(driver);
        var contracts = new RequestReplyConformanceContracts();
        await using var responder = await RequestReplyConformanceHost.StartResponderAsync(
            driver,
            contracts,
            cancellationToken
        );
        await using var caller = await RequestReplyConformanceHost.StartCallerAsync(
            driver,
            contracts,
            cancellationToken
        );

        var quote = await caller.RequestAsync(new ConformanceQuoteRequest("sku-1"), _Patient, cancellationToken);

        quote.Should().Be(new ConformanceQuote("sku-1", TenantId: null));
        responder.Probe.Handled.Should().Equal("quote:sku-1");
    }

    /// <summary>
    /// Two caller processes calling one responder at once each get exactly their own responses, and neither ever
    /// receives a reply meant for the other.
    /// </summary>
    public static async Task AssertCallersReceiveOnlyTheirOwnRepliesAsync(
        TransportProviderConformanceDriver driver,
        CancellationToken cancellationToken
    )
    {
        _RequireSupport(driver);
        var contracts = new RequestReplyConformanceContracts();
        using var drops = new RequestReplyDropRecorder();
        await using var responder = await RequestReplyConformanceHost.StartResponderAsync(
            driver,
            contracts,
            cancellationToken
        );
        await using var first = await RequestReplyConformanceHost.StartCallerAsync(
            driver,
            contracts,
            cancellationToken
        );
        await using var second = await RequestReplyConformanceHost.StartCallerAsync(
            driver,
            contracts,
            cancellationToken
        );
        var firstSkus = Enumerable.Range(0, 5).Select(i => $"first-{i}").ToArray();
        var secondSkus = Enumerable.Range(0, 5).Select(i => $"second-{i}").ToArray();

        var firstCalls = Task.WhenAll(firstSkus.Select(sku => _QuoteAsync(first, sku, cancellationToken)));
        var secondCalls = Task.WhenAll(secondSkus.Select(sku => _QuoteAsync(second, sku, cancellationToken)));
        var firstQuotes = await firstCalls;
        var secondQuotes = await secondCalls;

        firstQuotes.Select(quote => quote.Sku).Should().Equal(firstSkus);
        secondQuotes.Select(quote => quote.Sku).Should().Equal(secondSkus);
        responder
            .Probe.Requests.Select(request => request.ReplyTo)
            .Distinct(StringComparer.Ordinal)
            .Should()
            .HaveCount(2, "each caller process listens on a reply address of its own");
        drops.Reasons.Should().NotContain(MessagingMetrics.DropReasonUnknown, "no caller saw another caller's reply");
    }

    /// <summary>A responder that fails terminally ends the call with a typed fault instead of a timeout.</summary>
    public static async Task AssertResponderFailureFaultsTheCallAsync(
        TransportProviderConformanceDriver driver,
        CancellationToken cancellationToken
    )
    {
        _RequireSupport(driver);
        var contracts = new RequestReplyConformanceContracts();
        await using var responder = await RequestReplyConformanceHost.StartResponderAsync(
            driver,
            contracts,
            cancellationToken
        );
        await using var caller = await RequestReplyConformanceHost.StartCallerAsync(
            driver,
            contracts,
            cancellationToken
        );

        var act = () => caller.RequestAsync(new ConformanceFailingRequest("sku-1"), _Patient, cancellationToken);

        // The default classifier treats ArgumentException as permanent, so the fault follows one attempt.
        var fault = (await act.Should().ThrowAsync<RequestFaultedException>()).Which;
        fault.Code.Should().Be(RequestFaultCodes.HandlerFailed);
        fault.RemoteExceptionType.Should().BeNull("the responder host did not opt in to exception details");
        responder.Probe.Handled.Should().Equal("failing:sku-1");
    }

    /// <summary>
    /// A request whose only consumer is a plain <see cref="IConsume{TMessage}"/> ends in a <c>no_responder</c> fault,
    /// and that consumer never runs.
    /// </summary>
    public static async Task AssertPlainConsumerFaultsWithNoResponderAsync(
        TransportProviderConformanceDriver driver,
        CancellationToken cancellationToken
    )
    {
        _RequireSupport(driver);
        var contracts = new RequestReplyConformanceContracts();
        await using var responder = await RequestReplyConformanceHost.StartResponderAsync(
            driver,
            contracts,
            cancellationToken
        );
        await using var caller = await RequestReplyConformanceHost.StartCallerAsync(
            driver,
            contracts,
            cancellationToken
        );

        var act = () => caller.RequestAsync(new ConformancePlainRequest("sku-1"), _Patient, cancellationToken);

        var fault = (await act.Should().ThrowAsync<RequestFaultedException>()).Which;
        fault.Code.Should().Be(RequestFaultCodes.NoResponder);
        responder.Probe.Handled.Should().BeEmpty("a no_responder fault means no work ran");
    }

    /// <summary>
    /// A call to a destination that exists but has no running responder ends in a timeout. The responder host starts
    /// once and stops first, so brokers that refuse a send to a destination nobody provisioned still accept this one.
    /// </summary>
    public static async Task AssertTimeoutWithoutRunningResponderAsync(
        TransportProviderConformanceDriver driver,
        CancellationToken cancellationToken
    )
    {
        _RequireSupport(driver);
        var contracts = new RequestReplyConformanceContracts();
        var responder = await RequestReplyConformanceHost.StartResponderAsync(driver, contracts, cancellationToken);
        await responder.DisposeAsync();
        await using var caller = await RequestReplyConformanceHost.StartCallerAsync(
            driver,
            contracts,
            cancellationToken
        );

        var act = () => caller.RequestAsync(new ConformanceQuoteRequest("sku-1"), _Impatient, cancellationToken);

        await act.Should().ThrowAsync<RequestTimeoutException>();
        responder.Probe.Handled.Should().BeEmpty();
    }

    /// <summary>A reply arriving after its call timed out completes nothing and is dropped as late.</summary>
    public static async Task AssertLateReplyIsDroppedAsync(
        TransportProviderConformanceDriver driver,
        CancellationToken cancellationToken
    )
    {
        _RequireSupport(driver);
        var contracts = new RequestReplyConformanceContracts();
        using var drops = new RequestReplyDropRecorder();
        await using var responder = await RequestReplyConformanceHost.StartResponderAsync(
            driver,
            contracts,
            cancellationToken
        );
        await using var caller = await RequestReplyConformanceHost.StartCallerAsync(
            driver,
            contracts,
            cancellationToken
        );

        var act = () => caller.RequestAsync(new ConformanceHeldRequest("sku-1"), _Impatient, cancellationToken);

        await act.Should().ThrowAsync<RequestTimeoutException>();
        await RequestReplyConformanceWait.ForAsync(
            responder.Probe.HeldStarted.Task,
            "the responder started the held request",
            cancellationToken
        );
        responder.Probe.HeldRelease.TrySetResult();
        await RequestReplyConformanceWait.UntilAsync(
            () => drops.Reasons.Contains(MessagingMetrics.DropReasonLate),
            "the caller dropped the reply as late",
            cancellationToken
        );
        responder.Probe.Handled.Should().Equal("held:sku-1");
    }

    /// <summary>
    /// A request naming another Queue destination as its reply address gets its work done but writes nothing there: the
    /// responder refuses the address before the reply transport sees it.
    /// </summary>
    public static async Task AssertForeignReplyAddressIsNeverWrittenAsync(
        TransportProviderConformanceDriver driver,
        CancellationToken cancellationToken
    )
    {
        _RequireSupport(driver);
        var contracts = new RequestReplyConformanceContracts();
        using var drops = new RequestReplyDropRecorder();
        await using var responder = await RequestReplyConformanceHost.StartResponderAsync(
            driver,
            contracts,
            cancellationToken
        );
        await using var caller = await RequestReplyConformanceHost.StartCallerAsync(
            driver,
            contracts,
            cancellationToken
        );

        // A genuine request first, so the forged one carries every header this provider and host put on a request.
        await caller.RequestAsync(new ConformanceQuoteRequest("genuine"), _Patient, cancellationToken);
        var forged = await _ForgeRequestAsync(
            caller,
            responder.Probe.Requests.Single().Headers,
            replyTo: contracts.Audit,
            new ConformanceQuoteRequest("forged"),
            cancellationToken
        );

        var sent = await caller.Services.GetRequiredService<IQueueTransport>().SendAsync(forged, cancellationToken);

        sent.Succeeded.Should().BeTrue();
        await RequestReplyConformanceWait.UntilAsync(
            () => drops.Reasons.Contains(MessagingMetrics.DropReasonInvalidReplyAddress),
            "the responder refused the forged reply address",
            cancellationToken
        );
        responder.Probe.Handled.Should().Equal("quote:genuine", "quote:forged");

        // The destination's consumer receives a control message, so its silence about the forged reply means something.
        await caller
            .Services.GetRequiredService<IQueue>()
            .EnqueueAsync(new ConformanceAuditMessage("control"), cancellationToken);
        await RequestReplyConformanceWait.UntilAsync(
            () => responder.Probe.Audits.Contains("control"),
            "the audit destination received the control message",
            cancellationToken
        );
        responder.Probe.Audits.Should().Equal(["control"], "the forged reply address received no write");
        responder.Probe.RepliesSentTo.Should().NotContain(contracts.Audit);
    }

    /// <summary>
    /// A caller that restarts listens on a new reply address, the old one is gone, and a reply sent to the old one
    /// never reaches the new process.
    /// </summary>
    public static async Task AssertRestartedCallerNeverReceivesOldRepliesAsync(
        TransportProviderConformanceDriver driver,
        CancellationToken cancellationToken
    )
    {
        _RequireSupport(driver);
        var contracts = new RequestReplyConformanceContracts();
        using var drops = new RequestReplyDropRecorder();
        await using var responder = await RequestReplyConformanceHost.StartResponderAsync(
            driver,
            contracts,
            cancellationToken
        );

        var previous = await RequestReplyConformanceHost.StartCallerAsync(driver, contracts, cancellationToken);
        Task<ConformanceQuote> pending;
        try
        {
            pending = previous.RequestAsync(new ConformanceHeldRequest("before-restart"), _Patient, cancellationToken);
            await RequestReplyConformanceWait.ForAsync(
                responder.Probe.HeldStarted.Task,
                "the responder started the held request",
                cancellationToken
            );
        }
        finally
        {
            await previous.DisposeAsync();
        }

        var previousAddress = responder.Probe.Requests.Single().ReplyTo;
        var abandoned = () => pending;
        await abandoned.Should().ThrowAsync<RequestAbortedException>("the requester stopped while the call waited");
        await using var restarted = await RequestReplyConformanceHost.StartCallerAsync(
            driver,
            contracts,
            cancellationToken
        );
        await _WaitForNoReplyObjectsAsync(driver, previousAddress, cancellationToken);

        responder.Probe.HeldRelease.TrySetResult();
        await RequestReplyConformanceWait.UntilAsync(
            () => responder.Probe.RepliesSentTo.Contains(previousAddress),
            "the responder sent the held reply to the previous caller's address",
            cancellationToken
        );
        var quote = await _QuoteAsync(restarted, "after-restart", cancellationToken);

        quote.Sku.Should().Be("after-restart");
        responder.Probe.Requests.Last().ReplyTo.Should().NotBe(previousAddress, "a restarted caller listens anew");
        drops
            .Reasons.Should()
            .NotContain(MessagingMetrics.DropReasonUnknown, "the restarted caller never saw the reply");
    }

    /// <summary>
    /// The caller's tenant reaches the responder, and the reply carries it back; a call without a tenant stays without
    /// one on both sides.
    /// </summary>
    public static async Task AssertTenantFlowsBothWaysAsync(
        TransportProviderConformanceDriver driver,
        CancellationToken cancellationToken
    )
    {
        _RequireSupport(driver);
        var contracts = new RequestReplyConformanceContracts();
        await using var responder = await RequestReplyConformanceHost.StartResponderAsync(
            driver,
            contracts,
            cancellationToken
        );
        await using var caller = await RequestReplyConformanceHost.StartCallerAsync(
            driver,
            contracts,
            cancellationToken
        );

        // The caller drops a reply whose tenant differs from its call's, so a completed call also proves the reply's.
        var tenanted = await caller.RequestAsync(
            new ConformanceQuoteRequest("tenanted"),
            _Patient with
            {
                TenantId = "tenant-a",
            },
            cancellationToken
        );
        var untenanted = await caller.RequestAsync(
            new ConformanceQuoteRequest("untenanted"),
            _Patient,
            cancellationToken
        );

        tenanted.TenantId.Should().Be("tenant-a", "the responder sees the caller's tenant");
        untenanted.TenantId.Should().BeNull();
        responder.Probe.Requests.Select(request => request.TenantId).Should().Equal("tenant-a", null);
    }

    /// <summary>
    /// A stopped caller leaves no reply object behind on the broker. The driver's probe must first see the running
    /// caller's object, or its later silence would prove nothing.
    /// </summary>
    public static async Task AssertStoppedCallerLeavesNoReplyObjectsAsync(
        TransportProviderConformanceDriver driver,
        CancellationToken cancellationToken
    )
    {
        _RequireSupport(driver);
        var contracts = new RequestReplyConformanceContracts();
        await using var responder = await RequestReplyConformanceHost.StartResponderAsync(
            driver,
            contracts,
            cancellationToken
        );
        await using var caller = await RequestReplyConformanceHost.StartCallerAsync(
            driver,
            contracts,
            cancellationToken
        );
        await _QuoteAsync(caller, "sku-1", cancellationToken);
        var address = responder.Probe.Requests.Single().ReplyTo;

        (await driver.HasReplyObjectsAsync(address, cancellationToken))
            .Should()
            .BeTrue("the probe must see the reply objects of a running caller");

        await caller.DisposeAsync();

        await _WaitForNoReplyObjectsAsync(driver, address, cancellationToken);
    }

    /// <summary>
    /// On a provider that does not declare request/reply, a host that sends requests and a host that declares a
    /// responder both fail at startup, naming the provider, before they report readiness or run any handler.
    /// </summary>
    public static async Task AssertRejectedAtStartupAsync(
        TransportProviderConformanceDriver driver,
        CancellationToken cancellationToken
    )
    {
        if (driver.SupportsRequestReply)
        {
            throw new NotSupportedException(
                $"{driver.ProviderName} declares request/reply, so it must pass the shared suite instead."
            );
        }

        var contracts = new RequestReplyConformanceContracts();
        await using var caller = RequestReplyConformanceHost.BuildCaller(driver, contracts);
        await using var responder = RequestReplyConformanceHost.BuildResponder(driver, contracts);

        await _AssertStartupRejectedAsync(caller, cancellationToken);
        await _AssertStartupRejectedAsync(responder, cancellationToken);
        responder.GetRequiredService<RequestReplyConformanceProbe>().Handled.Should().BeEmpty();
    }

    private static async Task _AssertStartupRejectedAsync(IServiceProvider host, CancellationToken cancellationToken)
    {
        var bootstrapper = host.GetRequiredService<IBootstrapper>();
        var provider = host.GetServices<MessagingProviderCapabilities>()
            .Single(capabilities => capabilities.Role == MessagingProviderRole.Transport)
            .Provider;

        var act = () => bootstrapper.BootstrapAsync(cancellationToken);

        (await act.Should().ThrowAsync<MessagingConfigurationException>()).WithMessage($"*'{provider}'*");
        bootstrapper.IsStarted.Should().BeFalse("a rejected host never reports readiness");
    }

    private static Task<ConformanceQuote> _QuoteAsync(
        RequestReplyConformanceHost caller,
        string sku,
        CancellationToken cancellationToken
    )
    {
        return caller.RequestAsync(new ConformanceQuoteRequest(sku), _Patient, cancellationToken);
    }

    private static async Task<TransportMessage> _ForgeRequestAsync(
        RequestReplyConformanceHost caller,
        IReadOnlyDictionary<string, string?> genuineHeaders,
        string replyTo,
        object request,
        CancellationToken cancellationToken
    )
    {
        // Fresh ids keep inbox duplicate detection and reply matching from treating it as the genuine request.
        var headers = new Dictionary<string, string?>(genuineHeaders, StringComparer.Ordinal)
        {
            [MessagingHeaders.MessageId] = Guid.NewGuid().ToString("D"),
            [MessagingHeaders.RequestId] = Guid.NewGuid().ToString("D"),
            [MessagingHeaders.ReplyTo] = replyTo,
            [MessagingHeaders.RequestDeadline] = DateTimeOffset
                .UtcNow.Add(_Patient.Timeout!.Value)
                .ToString("O", CultureInfo.InvariantCulture),
        };

        return await caller
            .Services.GetRequiredService<ISerializer>()
            .SerializeToTransportMessageAsync(new Message(headers, request), cancellationToken);
    }

    private static Task _WaitForNoReplyObjectsAsync(
        TransportProviderConformanceDriver driver,
        string address,
        CancellationToken cancellationToken
    )
    {
        return RequestReplyConformanceWait.UntilAsync(
            async token => !await driver.HasReplyObjectsAsync(address, token),
            $"the provider holds no reply objects for '{address}'",
            cancellationToken
        );
    }

    private static void _RequireSupport(TransportProviderConformanceDriver driver)
    {
        if (!driver.SupportsRequestReply)
        {
            throw new NotSupportedException($"{driver.ProviderName} does not declare request/reply.");
        }
    }
}
