// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Registration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.RequestReply;

internal sealed record ConformanceQuoteRequest(string Sku);

internal sealed record ConformanceFailingRequest(string Sku);

/// <summary>A request whose responder waits for the scenario's release before it answers.</summary>
internal sealed record ConformanceHeldRequest(string Sku);

/// <summary>A request whose only consumer is a plain consumer, not a responder.</summary>
internal sealed record ConformancePlainRequest(string Sku);

internal sealed record ConformanceAuditMessage(string Marker);

/// <summary>The response every conformance responder returns, carrying the tenant the responder saw.</summary>
internal sealed record ConformanceQuote(string Sku, string? TenantId);

internal sealed class ConformanceQuoteDesk(RequestReplyConformanceProbe probe)
    : IRespond<ConformanceQuoteRequest, ConformanceQuote>,
        IRespond<ConformanceFailingRequest, ConformanceQuote>,
        IRespond<ConformanceHeldRequest, ConformanceQuote>
{
    public ValueTask<ConformanceQuote> RespondAsync(
        ConsumeContext<ConformanceQuoteRequest> context,
        CancellationToken cancellationToken
    )
    {
        probe.Record("quote:" + context.Message.Sku, context);
        return ValueTask.FromResult(new ConformanceQuote(context.Message.Sku, context.TenantId));
    }

    public ValueTask<ConformanceQuote> RespondAsync(
        ConsumeContext<ConformanceFailingRequest> context,
        CancellationToken cancellationToken
    )
    {
        probe.Record("failing:" + context.Message.Sku, context);
        throw new ArgumentException("The pricing table has no row for this SKU.", nameof(context));
    }

    public async ValueTask<ConformanceQuote> RespondAsync(
        ConsumeContext<ConformanceHeldRequest> context,
        CancellationToken cancellationToken
    )
    {
        probe.Record("held:" + context.Message.Sku, context);
        probe.HeldStarted.TrySetResult();

        // Host shutdown cancels the token, so a scenario that fails before releasing never hangs disposal.
        await probe.HeldRelease.Task.WaitAsync(cancellationToken);
        return new ConformanceQuote(context.Message.Sku, context.TenantId);
    }
}

internal sealed class ConformancePlainConsumer(RequestReplyConformanceProbe probe) : IConsume<ConformancePlainRequest>
{
    public ValueTask ConsumeAsync(ConsumeContext<ConformancePlainRequest> context, CancellationToken cancellationToken)
    {
        probe.Record("plain:" + context.Message.Sku, context);
        return ValueTask.CompletedTask;
    }
}

internal sealed class ConformanceAuditConsumer(RequestReplyConformanceProbe probe) : IConsume<ConformanceAuditMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<ConformanceAuditMessage> context, CancellationToken cancellationToken)
    {
        probe.Audits.Enqueue(context.Message.Marker);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// What the source generator emits for the consumers above, written out because the harness assembly does not run the
/// generator.
/// </summary>
internal sealed class RequestReplyConformanceModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog)
    {
        catalog.AddQueueResponder<ConformanceQuoteDesk, ConformanceQuoteRequest, ConformanceQuote>(
            "conformance.request-reply.quote-desk",
            _Respond<ConformanceQuoteRequest>()
        );
        catalog.AddQueueResponder<ConformanceQuoteDesk, ConformanceFailingRequest, ConformanceQuote>(
            "conformance.request-reply.quote-desk",
            _Respond<ConformanceFailingRequest>()
        );
        catalog.AddQueueResponder<ConformanceQuoteDesk, ConformanceHeldRequest, ConformanceQuote>(
            "conformance.request-reply.quote-desk",
            _Respond<ConformanceHeldRequest>()
        );
        catalog.AddQueueConsumer<ConformancePlainConsumer, ConformancePlainRequest>(
            "conformance.request-reply.plain",
            static (services, context, cancellationToken) =>
                ActivatorUtilities
                    .CreateInstance<ConformancePlainConsumer>(services)
                    .ConsumeAsync((ConsumeContext<ConformancePlainRequest>)context, cancellationToken)
        );
        catalog.AddQueueConsumer<ConformanceAuditConsumer, ConformanceAuditMessage>(
            "conformance.request-reply.audit",
            static (services, context, cancellationToken) =>
                ActivatorUtilities
                    .CreateInstance<ConformanceAuditConsumer>(services)
                    .ConsumeAsync((ConsumeContext<ConformanceAuditMessage>)context, cancellationToken)
        );
    }

    private static MessageConsumerDispatch _Respond<TRequest>()
        where TRequest : class =>
        static async (services, context, cancellationToken) =>
        {
            var responder =
                (IRespond<TRequest, ConformanceQuote>)
                    (object)ActivatorUtilities.CreateInstance<ConformanceQuoteDesk>(services);
            var typed = (ConsumeContext<TRequest>)context;
            typed.RecordReply(await responder.RespondAsync(typed, cancellationToken).ConfigureAwait(false));
        };
}
