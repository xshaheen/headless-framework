---
domain: Messaging
packages:
---

# Messaging request/reply

> Part of [Messaging](../messaging.md). Send a typed request on the Queue lane and await one typed response, with deadlines, faults, and provider limits.

## Request/reply

A caller sends a typed request on the Queue lane and awaits one typed response. `IRequestClient.RequestAsync<TRequest, TResponse>` sends the request, the request's one Queue consumer answers through `IRespond<TRequest, TResponse>`, and the reply returns over a reply channel that belongs to the calling process. Use it for latency-tolerant commands whose caller cannot continue without the answer, such as a quote, a validation result, or a reservation.

It is not a durable workflow. The pending call lives only in the calling process, so durable multi-step coordination stays with [Jobs](../jobs.md). Only InMemory, RabbitMQ, NATS, and Redis support it; see [Provider support](#provider-support).

## Request/reply versus callbacks

| | Request/reply | Bus callback |
| --- | --- | --- |
| Who waits | The caller awaits `RequestAsync` | Nobody; the response is a new Bus message |
| Declared by | A `[QueueConsumer]` class that implements `IRespond<TRequest, TResponse>` | `CallbackName` on the publish options, and `SetResponse` in an `IConsume<T>` consumer |
| Response path | A reply channel of the calling process; never stored, never a lane message | A durable Bus publish |
| Delivery | At most once: a lost reply ends the call in a timeout | At least once: response consumers must be idempotent |
| Answers per request | One, from the request's one Queue consumer | One per consumer; a Bus request fans out |
| Providers | InMemory, RabbitMQ, NATS, Redis | Every provider with a Bus lane |

Use a callback when nothing waits for the answer or the answer must survive a restart. Use request/reply when the caller needs the answer to continue and can handle a timeout.

A callback is set with `CallbackName` on `PublishOptions` or `QueueOptions`. The response always publishes through the durable Bus path, including for a Queue-originated request, and exactly one Bus response is produced per Queue delivery. `SetResponse<TResponse>` keeps the declared response contract and the concrete payload type.

## Setup

The responding service declares the responder. It needs no request/reply registration of its own, only a transport that supports request/reply:

```csharp
// Contracts both services share: the request and the response.
public sealed record GetQuote(string Sku, int Quantity);

public sealed record Quote(string Sku, decimal UnitPrice, DateTimeOffset ValidUntil);

public interface IPriceBook
{
    ValueTask<decimal> GetUnitPriceAsync(string sku, CancellationToken cancellationToken);
}

// Pricing service: the responder is the request's one Queue consumer.
[QueueConsumer("pricing.get-quote")]
public sealed class GetQuoteResponder(IPriceBook prices, TimeProvider clock) : IRespond<GetQuote, Quote>
{
    public async ValueTask<Quote> RespondAsync(ConsumeContext<GetQuote> context, CancellationToken cancellationToken)
    {
        var unitPrice = await prices.GetUnitPriceAsync(context.Message.Sku, cancellationToken);

        return new Quote(context.Message.Sku, unitPrice, clock.GetUtcNow().AddMinutes(5));
    }
}

builder.Services.AddHeadlessMessaging(setup =>
{
    setup.UseRabbitMq(options =>
    {
        options.HostName = "localhost";
        options.UserName = builder.Configuration["RabbitMq:UserName"]!;
        options.Password = builder.Configuration["RabbitMq:Password"]!;
    });
    setup.UsePostgreSqlEntityFramework<AppDbContext>();
    setup.AddModule<Pricing.MessagingModule>();
});

builder.Services.ConfigureMessaging(messaging =>
{
    messaging.Message<GetQuote>("pricing.get-quote");
    messaging.Message<Quote>("pricing.quote");
});
```

The calling service enables requests with `AddRequestReply` and injects `IRequestClient`:

```csharp
builder.Services.AddHeadlessMessaging(setup =>
{
    setup.UseRabbitMq(options =>
    {
        options.HostName = "localhost";
        options.UserName = builder.Configuration["RabbitMq:UserName"]!;
        options.Password = builder.Configuration["RabbitMq:Password"]!;
    });
    setup.UsePostgreSqlEntityFramework<AppDbContext>();

    // Registers IRequestClient and opens this host's reply channel when messaging starts.
    setup.AddRequestReply(requests => requests.DefaultTimeout = TimeSpan.FromSeconds(10));
});

builder.Services.ConfigureMessaging(messaging =>
{
    messaging.Message<GetQuote>("pricing.get-quote");
    messaging.Message<Quote>("pricing.quote");
});

public sealed class CheckoutPricing(IRequestClient requests)
{
    public Task<Quote> QuoteAsync(string sku, int quantity, CancellationToken cancellationToken) =>
        requests.RequestAsync<GetQuote, Quote>(
            new GetQuote(sku, quantity),
            new RequestOptions { Timeout = TimeSpan.FromSeconds(5) },
            cancellationToken
        );
}

public sealed record GetQuote(string Sku, int Quantity);

public sealed record Quote(string Sku, decimal UnitPrice, DateTimeOffset ValidUntil);
```

**Verify:** a host on a transport without request/reply fails startup with `MessagingConfigurationException` naming the transport. On a supported transport, the RabbitMQ, NATS, and Redis listeners log `ReplyListenerReady` (EventId 4117, Debug) with the reply address once the channel opens. In a test, `RequestAsync` against an in-memory host returns the responder's value.

- **`AddRequestReply` is for callers only.** It registers `IRequestClient` and a reply listener that opens when messaging starts, before any consumer, and closes when it stops. A host that never calls it has no `IRequestClient` registration. Calling it twice is harmless.
- **Both services must resolve the response type to one contract.** The responder stamps the response's contract name and version from its registry, and the caller compares them with its own. Declare the request and the response with `Message<T>` in a module both services add. The response contract needs no consumer.
- **Options.** `RequestReplyOptions` lives at `setup.Options.RequestReply`:
  - `DefaultTimeout` (default 30 seconds, greater than zero, at most 10 minutes) applies when a call sets no `RequestOptions.Timeout`. A per-call `Timeout` above `RequestOptions.MaxTimeout` (10 minutes) throws `ArgumentOutOfRangeException`.
  - `IncludeExceptionDetailsInFaults` (default `false`) is read by the responding host and applies there without `AddRequestReply`.
  - `MaxPendingRequests` (default `null`, no limit; greater than zero when set) caps the calls waiting at once on this host. See [Sending requests](#sending-requests).
- **Startup checks.** A host that calls `AddRequestReply`, or declares a responder, on a transport without support fails startup with `MessagingConfigurationException`. A message name that starts with `headless.reply.` after `MessagingOptions.MessageNamePrefix` fails startup on either lane, because that prefix is reserved for reply addresses.

## Responders

- **Declaration.** A responder is a `[QueueConsumer]` class that implements `IRespond<TRequest, TResponse>`. It is the request's one Queue consumer: a second Queue consumer or responder for the same request fails the build ([HM004](../messaging.md#hm004)) or startup. `[BusConsumer]` on a responder is HM011. `IConsume<T>` and `IRespond<T, TResponse>` on one class for the same `T` is HM012. Two response types for one request is HM013. A `RequestAsync` or `GetReplyAsync` call whose `TResponse` differs from what a visible responder answers is HM014, a warning. A caller that sees no responder, the usual case for another service, is not checked. See [Diagnostics](../messaging.md#messaging-source-generator-diagnostics).
- **Cancellation.** The `RespondAsync` token is canceled when the host stops. It is not tied to the request deadline or the caller's cancellation, because accepted work continues after the caller stops waiting.
- **Never return null.** A `null` result ends the request at once, with no retry, and the caller gets a `null_response` fault.
- **No callbacks.** In a responder, `SetResponse`, `SetResponseCallbackName`, and `SetResponseDestination` throw `InvalidOperationException`. The return value is the only answer. A `CallbackName` the request carries is ignored.
- **A plain enqueue still runs it.** `IQueue.EnqueueAsync` of the request type reaches the responder like any Queue consumer. It runs and its result is discarded.
- **Only a Queue message is a request.** A Bus message that carries the request headers is consumed as an ordinary Bus message, with no deadline check and no reply.
- **A request never reaches a plain consumer.** When a request arrives at a host whose consumer for that message is a plain `IConsume<T>`, the host commits and skips it before inbox admission (receive outcome `no_responder`, no storage row) and answers with a `no_responder` fault, so that code always means no work ran.
- **Normal consume pipeline.** A request is admitted to the inbox, deduplicated, and runs Queue-lane receive and consume middleware, the circuit breaker, and the inbox transaction like any Queue message. `ConsumeOnly` filters a responder like any competing consumer.
- **The reply leaves only after the outcome is durable.** Under the `Transactional` inbox guarantee the reply is sent after the attempt's transaction commits. A rolled-back or indeterminate commit sends nothing. Under a weaker guarantee it is sent after the success write. Only the attempt whose state write won replies. A crash between the durable outcome and the reply leaves the caller with a timeout. Sending a reply is best effort: a failure is logged and never retried. Each send is bounded by `MessagingOptions.TransportPublishTimeout`.
- **Tenant.** With messaging tenant propagation registered (`AddHeadlessTenancy(t => t.Messaging(m => m.PropagateTenant()))`), every Queue consumer, responder included, runs inside the envelope tenant's scope. The reply carries the request's tenant verbatim.

## Model expected outcomes in the response

Return an expected business outcome, such as "not found", "rejected", or "out of stock", as part of `TResponse`. Throw only for failures. A thrown exception is retried inline and then reaches the caller as a `handler_failed` fault with no detail by default, so the caller cannot tell "not found" from a crash.

```csharp
public sealed record FindCustomer(Guid CustomerId);

// "Not found" is an expected answer, so the response carries it instead of the responder throwing.
public sealed record CustomerLookup(CustomerSummary? Customer)
{
    public static CustomerLookup NotFound { get; } = new(Customer: null);
}

public sealed record CustomerSummary(Guid Id, string DisplayName);

public interface ICustomerDirectory
{
    ValueTask<CustomerSummary?> FindAsync(Guid customerId, CancellationToken cancellationToken);
}

[QueueConsumer("customers.find-customer")]
public sealed class FindCustomerResponder(ICustomerDirectory directory) : IRespond<FindCustomer, CustomerLookup>
{
    public async ValueTask<CustomerLookup> RespondAsync(
        ConsumeContext<FindCustomer> context,
        CancellationToken cancellationToken
    )
    {
        var customer = await directory.FindAsync(context.Message.CustomerId, cancellationToken);

        return customer is null ? CustomerLookup.NotFound : new CustomerLookup(customer);
    }
}

public sealed class CustomerNames(IRequestClient requests)
{
    public async Task<string?> GetDisplayNameAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var lookup = await requests.RequestAsync<FindCustomer, CustomerLookup>(
            new FindCustomer(customerId),
            cancellationToken: cancellationToken
        );

        return lookup.Customer?.DisplayName;
    }
}
```

## Reply headers

A responder can return metadata beside the response, such as where an answer came from, without widening the response contract. It sets the header with `ConsumeContext.SetResponseHeader`, and the caller reads it from `Reply<TResponse>.Headers` through `GetReplyAsync`:

```csharp
public sealed record GetPrice(string Sku);

public sealed record Price(decimal Amount);

public interface IPriceBook
{
    ValueTask<(Price Price, bool FromCache)> FindAsync(string sku, CancellationToken cancellationToken);
}

[QueueConsumer("pricing.get-price")]
public sealed class GetPriceResponder(IPriceBook prices) : IRespond<GetPrice, Price>
{
    public async ValueTask<Price> RespondAsync(ConsumeContext<GetPrice> context, CancellationToken cancellationToken)
    {
        var (price, fromCache) = await prices.FindAsync(context.Message.Sku, cancellationToken);

        // Metadata about the answer travels beside it instead of widening the response contract.
        context.SetResponseHeader("x-price-source", fromCache ? "cache" : "live");
        return price;
    }
}

public sealed class PriceReader(IRequestClient requests)
{
    public async Task<(Price Price, string? Source)> GetAsync(string sku, CancellationToken cancellationToken)
    {
        var reply = await requests.GetReplyAsync<GetPrice, Price>(
            new GetPrice(sku),
            cancellationToken: cancellationToken
        );
        reply.Headers.TryGetValue("x-price-source", out var source);

        return (reply.Message, source);
    }
}
```

- `SetResponseHeader` throws at once, because the reply is sent after the outcome is durable and cannot fail then:
  - `ArgumentException` for a name the reply protocol or trace propagation owns (any `headless-` name, `traceparent`, `tracestate`, `baggage`, case-insensitive), a name outside the header-name rule (1 to 255 printable ASCII characters other than `:`), a name that differs only in case from one already set, a value with a control character, or a header that brings the UTF-8 bytes of all reply header names and values past 16 KiB.
  - `ArgumentNullException` for a `null` value.
- Only an ok reply carries the headers. A fault reply does not.
- `Reply<TResponse>` carries `Message`, `Headers`, `MessageId` (the reply's own id), and `CorrelationId` (the request's correlation id, or its message id when it had none).
- The caller removes every `headless-*` and trace propagation header and drops headers without a value. A header a transport or broker plugin adds on the way arrives too. The values come from another service, so validate them before you act on them.
- When the response cannot be serialized, or the transport refuses the reply (for example a body over NATS `max_payload`), the caller gets a `response_not_sent` fault instead of a timeout.

## Sending requests

`RequestOptions` carries `Timeout`, `TenantId`, `CorrelationId`, and `Headers`, and nothing else.

- **The framework owns the request.** It assigns the message id and a `headless-request-id` (a UUIDv7) to each call, so inbox deduplication never swallows a legitimate retry. `RequestOptions.Headers` rejects reserved messaging headers, invalid names, and control characters in values with `InvalidOperationException`.
- **A request is always sent directly.** It is never stored in the outbox, delayed, scheduled, or given a callback name. `WithDeliveryMode` and `MessagingOptions.DefaultDeliveryMode` do not apply, and `RequestOptions` has no `DeliverWithin`.
- **Middleware and tracing.** A request passes through Queue-lane publish middleware. Middleware that suppresses it ends the call with `RequestNotSentException`. A `DeliverWithin` that middleware sets on a request is ignored. The request span injects trace context, and the reply carries the responder span's `traceparent`. No Bus publish or consume middleware runs on a reply.
- **Not inside a transactional inbox unit.** `RequestAsync` called while a consumer attempt runs inside an inbox transaction (its `ConsumeContext.UnitOfWork` is not null, which is every consumer under the `Transactional` guarantee) throws `InvalidOperationException`, because waiting would hold the database transaction and inbox lease for the whole timeout. Send the request after the unit completes, or from a consumer under a weaker guarantee. An application unit of work from `IUnitOfWorkFactory` does not block a request, but the request never joins it: it is sent at once, and a rollback does not recall it.
- **Tenant.** The request carries `RequestOptions.TenantId`, or else the ambient `ICurrentTenant.Id` the tenant publish middleware stamps when propagation is registered. Set `TenantId` to override the ambient tenant. The caller drops a reply whose tenant differs from the request's (no tenant matches only no tenant), and the call times out.
- **Pending limit.** With `RequestReply.MaxPendingRequests` set, a call made while that many wait throws `RequestNotSentException` at once, publishes nothing, and counts as `not_sent`. A slot frees the moment a waiting call ends. The count is per host process. Size the limit from the host's peak request rate times its timeout.
- **Shutdown.** When the calling host stops, new calls throw `RequestNotSentException` and pending calls fail with `RequestAbortedException`. When the caller is itself a consumer, that failure during its host's shutdown counts as shutdown: the attempt writes no failed state and is picked up again after restart.

## Outcomes and exceptions

A call that passes argument validation ends in exactly one outcome below and never returns `null`. Every failure except the caller's own cancellation and an unreadable response body derives from `RequestReplyException`, which exposes `RequestId`, so one `catch` covers them.

| Outcome | Surfaces as | Did the responder run? | Safe to retry? |
| --- | --- | --- | --- |
| Response | The `TResponse` value | Yes, and its outcome is durable | Not needed |
| Timeout | `RequestTimeoutException` (`Timeout`) | Unknown: it may have finished, or its reply was lost | Only when the responder is idempotent |
| Fault | `RequestFaultedException` (`Code`, plus `RemoteExceptionType` and `Detail` when the responder host opts in) | Depends on `Code` | Depends on `Code` |
| Contract mismatch | `ResponseContractMismatchException` (expected and actual name and version) | Yes, it completed | No: deploy the two services with one response contract |
| Not sent | `RequestNotSentException` | No when it never left the caller; almost never when the transport reported the send as failed | Yes when it never left; otherwise only if the responder is idempotent |
| Aborted | `RequestAbortedException` | Unknown: the request was sent and the caller stopped | Only when the responder is idempotent |
| Caller canceled | `OperationCanceledException` | Unknown when already sent; canceling ends only the wait | Only when the responder is idempotent |
| Unreadable response body | `MessageDeserializationException` | Yes, it completed | No: fix the response contract |

`RequestNotSentException` covers publish middleware that suppressed the request, a send the transport reported as failed (`InnerException` is the transport's exception, such as `PublisherSentFailedException`), a stopping host, a host at its `MaxPendingRequests` limit, and a reply listener that did not become ready within the call's timeout, for example while the broker is unreachable. A connection lost after the broker stored the request surfaces the same way, so it does not guarantee that no responder runs. A send that outlasts `MessagingOptions.TransportPublishTimeout` is not "not sent": the call keeps waiting and ends with the response or `RequestTimeoutException`. `RequestAsync` also throws `ArgumentNullException` for a null request and `InvalidOperationException` for the transactional-unit and reserved-header cases.

Fault codes are stable wire values in `RequestFaultCodes`:

| Code | Meaning | Did work run? |
| --- | --- | --- |
| `handler_failed` | The responder failed terminally before the deadline: immediate retries ran out or the deadline cut them short, or its failure policy ended the failure at once | It ran and failed; effects may be partial |
| `no_responder` | The request reached a consumer that does not respond, or a host with no consumer for it | No. Safe to retry once a responder is deployed |
| `request_rejected` | The responding host rejected the request on arrival: a contract version mismatch, an undeserializable body, or receive middleware that rejected it | No |
| `null_response` | The responder returned `null` | It ran to completion |
| `response_not_sent` | The responder completed, but its response could not be serialized or the transport refused the reply | Yes; its effects stand |

A fault body is never empty, and an unreadable one surfaces as `handler_failed`. `RemoteExceptionType` and `Detail` stay `null` unless the responding host sets `RequestReply.IncludeExceptionDetailsInFaults`, because exception messages can carry internal details. When `AddHeadlessRedaction()` on the responding host registers a text redactor, `Detail` is masked before it is sent.

```csharp
public sealed class QuoteGateway(IRequestClient requests)
{
    public async Task<Quote?> TryQuoteAsync(GetQuote request, CancellationToken cancellationToken)
    {
        try
        {
            return await requests.RequestAsync<GetQuote, Quote>(request, cancellationToken: cancellationToken);
        }
        catch (RequestNotSentException)
        {
            // Nothing reached a responder, so a retry cannot repeat work.
            return null;
        }
        catch (RequestFaultedException e) when (e.Code == RequestFaultCodes.NoResponder)
        {
            // The request reached a consumer that does not respond, so no work ran.
            return null;
        }
        catch (RequestTimeoutException)
        {
            // Ambiguous: the responder may have finished. Retry only when GetQuote is idempotent.
            return null;
        }
    }
}

public sealed record GetQuote(string Sku, int Quantity);

public sealed record Quote(string Sku, decimal UnitPrice);
```

## Deadlines, timeouts, and clock skew

- **Every request has a deadline:** the send time plus the call's timeout, carried as an absolute UTC instant in `headless-request-deadline`. The caller's own timer stays authoritative for the call.
- **The responder checks the deadline on its own clock.** A request that arrives expired is committed and dropped before inbox admission: receive outcome `expired`, no storage row, no reply, no `OnTerminal`, no circuit-breaker failure. The check repeats at the start of every attempt, including retries, persisted pickups, and crash recovery. A request whose deadline header is missing or unreadable has no deadline.
- **A started attempt is not interrupted.** The handler's token is not tied to the deadline. A late reply is dropped by the caller as `late`.
- **A force-reprocessed request runs as a plain Queue message.** The replayed generation drops `headless-reply-to` and `headless-request-deadline` and keeps `headless-request-id`. It runs under the full [failure policy](retries.md#consumer-failure-policy), delayed retries included, sends no reply or fault, and logs EventId 4116 (`ReplayedRequestRunsWithoutCaller`) once per dispatch.
- **A nested request inherits the inbound deadline.** When a consumer answering a request sends its own request, the nested timeout is the smaller of its own timeout and what is left of the inbound deadline on this host's clock. When the inbound deadline has passed, the nested call throws `RequestNotSentException`, publishes nothing, and counts as `not_sent`. A consumer answering a plain message inherits nothing. Inheritance is read from `ConsumeContext.Headers` and follows the consume's async context: a task started inside the handler inherits it, a unit-of-work completion callback does not.
- **Clock skew moves the window.** A responder clock that runs ahead expires requests early. One that runs behind starts work after the caller gave up. Keep host clocks synchronized and timeouts much longer than the expected skew.
- **A timeout is ambiguous.** The responder may have completed the work. Make responders idempotent, for example by deduplicating on a business key the request carries, before a caller retries after a timeout.
- **The caller's continuation is at most once.** A caller that restarts loses its pending calls, and its new listener has a new address, so a reply to the previous run never reaches it. There is no persisted caller state.

## Retries

A request uses the responder's [failure policy](retries.md#consumer-failure-policy) and fail rules, but only its immediate retries. It never takes a delayed retry, because its caller waits only until the deadline.

- **Immediate retries run while the deadline allows.** When they are spent, or the deadline passes, the request ends as exhausted even when the policy declares delayed retries. The caller gets `handler_failed`, then `RetryPolicy.OnTerminal` fires with `RetriesExhausted`. The reply is sent before the callback runs.
- **A permanent failure ends at once** with `handler_failed`, and `OnTerminal` fires with `PermanentFailure`.
- **A null response** fires `OnTerminal` with `PermanentFailure`. **An expired request** ends with no reply and fires `OnTerminal` with `DeliveryDeadlineExpired`.
- **A failure before the handler runs** always counts against the budget for a request, whatever `RetryPolicy.MaxPreEntryFailures` says.
- **A request rejected on arrival** gets a `request_rejected` fault, and `OnTerminal` fires with `PermanentFailure`.
- **A replayed request** follows the full failure policy, including delayed retries, and never sends a fault.
- Receive middleware that skips a request (`context.Skip(...)`) sends no reply, so its caller times out.

## Telemetry

| Instrument | Kind | Dimension and values |
| --- | --- | --- |
| `headless.messaging.request_reply.requests` | Counter | `headless.messaging.request_reply.outcome`: `replied`, `faulted`, `contract_mismatch`, `timed_out`, `canceled`, `aborted`, `not_sent`, `failed` |
| `headless.messaging.request_reply.duration` | Histogram (s) | `headless.messaging.request_reply.outcome` |
| `headless.messaging.request_reply.dropped_replies` | Counter | `headless.messaging.request_reply.drop_reason`: `late` (the call already ended), `duplicate` (the call already had its reply), `unknown` (no call of this process sent it), `tenant_mismatch`, `invalid_reply_address` (counted on the responding host, which wrote nothing) |
| `headless.messaging.receive.outcomes` | Counter | `headless.messaging.receive.outcome`: `expired` for a request dropped on arrival after its deadline; `no_responder` for a request that reached a plain consumer; `skipped` only for a receive-middleware skip |

No request id, correlation id, or instance id is a metric dimension. See [Instrument reference](observability.md#instrument-reference) for the rest.

## Provider support

| Provider | Request/reply | Reply channel |
| --- | --- | --- |
| InMemory | Supported | An in-process channel per listener |
| RabbitMQ | Supported | An exclusive, non-durable queue `headless.reply.{32 hex}`, consumed with automatic acknowledgement on the listener's own connection. Replies go through the default exchange and never touch the lane exchanges |
| NATS | Supported | A core NATS subscription, outside JetStream, on the subject `headless.reply.{32 hex}`, on a pooled connection |
| Redis | Supported | A pub/sub subscription to the channel `headless.reply.{32 hex}`. `PUBLISH` writes no key |
| Azure Service Bus | Not yet: startup fails | |
| Kafka | No: startup fails | Kafka has no per-process address short of a partition per instance |
| AWS SNS/SQS | No: startup fails | AWS has no .NET temporary-queue client, and per-process queues are billed |

Every supported provider removes its reply objects when the caller stops or dies. A reply sent to an address nobody listens on is discarded without an error.

### Connection loss and startup

- **Every provider backs off before it reconnects.** After a lost channel or a failed attempt, the listener waits a jittered delay of about 1 second, doubling to 30 seconds, and starts over at about 1 second once a channel opens. The wait runs on the system clock, so a test host with a `FakeTimeProvider` still reconnects in real time.
- **Logs**, under the reply transport's category with the broker in `{Transport}`: `ReplyListenerReady` (EventId 4117, Debug) with `{ReplyAddress}` when a channel opens; `ReplyListenerLost` (EventId 4118, Warning) with `{Reason}` and `{RetryDelay}` when an open channel ends; `ReplyListenerFailed` (EventId 4119, Error) with the exception and `{RetryDelay}` when opening or serving a channel throws.
- **RabbitMQ.** The listener's connection does not recover on its own. After a loss the listener opens a new connection and queue under a new address, so calls sent with the old address time out. It connects in the background, so an unreachable broker or wrong credentials never fail bootstrap: each call waits for the address inside its timeout and then throws `RequestNotSentException`.
- **NATS.** The address stays the same across reconnects. While the connection is down the address is withheld, so new calls wait inside their timeout. A call already waiting completes if its reply arrives after the reconnect. A reply published during the outage is lost. An unreachable server never fails bootstrap. A reply that arrives while the subscription's pending channel (`NatsOpts.SubPendingChannelCapacity`) is full is dropped with a warning.
- **Redis.** The address stays the same across reconnects. While the subscription connection is down the address is withheld and replies published then are lost. Bootstrap never fails.

### Operator rules

- **NATS streams.** Keep JetStream streams on the lane prefixes (`headless.bus.>`, `headless.queue.>`). A stream that covers `headless.reply.>`, such as `headless.>` or `>`, stores every reply. When a caller's listener subscribes, it checks once and logs a warning naming such streams (EventId 13, `NatsReplySubjectCapturedByStreams`). A server without JetStream or a slow API produces a debug entry instead (EventId 14, `NatsReplySubjectStreamCheckSkipped`), and a clean check logs EventId 15 (`NatsReplySubjectNotCapturedByStreams`, Debug). With `StreamProvisioning` set to `Disabled` and no stream for the request's message, a request fails at once with `RequestNotSentException` instead of timing out. Under `Verify` or `Reconcile` the caller provisions the request's stream before the first send.
- **Redis channel prefix.** Every host that exchanges requests must use the same StackExchange.Redis `ConfigurationOptions.ChannelPrefix`. Pub/sub ignores the database number, so hosts on different databases of one server share the reply namespace.
- **Redis Cluster.** Replies use classic `PUBLISH`, which the cluster forwards to every node, so a reply reaches its caller whichever node either side uses. This path has no conformance run against a real cluster.
- **RabbitMQ.** A reply is published without the mandatory flag, so a reply to a queue that is gone is discarded silently.

### Broker permissions

Reply confidentiality and integrity depend on the broker's access control over the reply namespace. A principal that can publish to a reply address can forge a reply: the caller accepts one only when its `headless-in-reply-to` matches a pending call, its tenant matches, and its contract matches, but request ids travel in request headers any reader of the request queue sees. A principal that can subscribe to the reply namespace can read replies. Grant these in addition to lane permissions:

| Provider | Caller (sends requests) | Responder (sends replies) | Notes |
| --- | --- | --- | --- |
| RabbitMQ | `configure` and `read` on queues matching `^headless\.reply\.` | `write` on the default exchange (`amq.default`) | The exclusive queue refuses consumers on other connections. `write` on the default exchange reaches any queue by name, so the broker cannot confine a responder to reply queues |
| NATS | `subscribe` on `headless.reply.>` | `publish` on `headless.reply.>` | Deny `subscribe` on `headless.reply.>` to every other principal; a subscriber there sees every caller's replies |
| Redis | `+subscribe` and `+unsubscribe` on channels `&headless.reply.*` | `+publish` on channels `&headless.reply.*` | Include the `ChannelPrefix` in the channel pattern. Grant `subscribe` only to callers |
