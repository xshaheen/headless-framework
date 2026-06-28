// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Messages;
using Headless.Messaging.OpenTelemetry;
using Headless.Messaging.OpenTelemetry.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using DiagnosticListener = Headless.Messaging.OpenTelemetry.DiagnosticListener;

namespace Tests;

public sealed class DiagnosticListenerTests : TestBase
{
    [Fact]
    public void should_have_correct_source_name()
    {
        // then
        DiagnosticListener.SourceName.Should().Be("Headless.Messaging");
    }

    [Fact]
    public void should_create_diagnostic_listener_without_metrics()
    {
        // given/when
        var listener = new DiagnosticListener([]);

        // then - no exception
        listener.Should().NotBeNull();
    }

    [Fact]
    public void should_create_diagnostic_listener_with_metrics()
    {
        // given
        using var metrics = new MessagingMetrics();

        // when
        var listener = new DiagnosticListener([], metrics: metrics);

        // then
        listener.Should().NotBeNull();
    }

    [Fact]
    public void should_not_throw_on_completed()
    {
        // given
        var listener = new DiagnosticListener([]);

        // when/then
        var act = listener.OnCompleted;
        act.Should().NotThrow();
    }

    [Fact]
    public void should_not_throw_on_error()
    {
        // given
        var listener = new DiagnosticListener([]);

        // when/then
        var act = () => listener.OnError(new InvalidOperationException("Test"));
        act.Should().NotThrow();
    }

    [Fact]
    public void should_handle_unknown_event_gracefully()
    {
        // given
        var listener = new DiagnosticListener([]);

        // when
        var act = () => listener.OnNext(new KeyValuePair<string, object?>("UnknownEvent", new { Data = "test" }));

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_handle_null_event_value_gracefully()
    {
        // given
        var listener = new DiagnosticListener([]);

        // when
        var act = () => listener.OnNext(new KeyValuePair<string, object?>("UnknownEvent", null));

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_not_call_enrichers_when_event_key_is_unknown()
    {
        // given
        var enricher = Substitute.For<IActivityTagEnricher>();
        var listener = new DiagnosticListener([enricher]);

        // when
        listener.OnNext(new KeyValuePair<string, object?>("UnknownEvent", null));

        // then
        _ = enricher
            .DidNotReceive()
            .Enrich(Arg.Any<Activity>(), Arg.Any<MessagingEnrichmentContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void should_call_enrichers_when_persist_event_is_received()
    {
        // given
        var enricher = Substitute.For<IActivityTagEnricher>();
        var listener = new DiagnosticListener([enricher]);
        using var activityListener = _CreateActivityListener();
        var eventData = _CreatePubStoreEventData("order.created");

        // when
        listener.OnNext(
            new KeyValuePair<string, object?>(MessageDiagnosticListenerNames.BeforePublishMessageStore, eventData)
        );

        // then
        _ = enricher
            .Received()
            .Enrich(
                Arg.Any<Activity>(),
                Arg.Is<MessagingEnrichmentContext>(c =>
                    c.Kind == MessagingEventKind.Persist
                    && c.MessageName == "order.created"
                    && c.IntentType == IntentType.Bus
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public void should_call_enrichers_with_queue_intent_when_persist_event_is_received()
    {
        // given
        var enricher = Substitute.For<IActivityTagEnricher>();
        var listener = new DiagnosticListener([enricher]);
        using var activityListener = _CreateActivityListener();
        var eventData = _CreatePubStoreEventData("order.created");
        eventData.IntentType = IntentType.Queue;

        // when
        listener.OnNext(
            new KeyValuePair<string, object?>(MessageDiagnosticListenerNames.BeforePublishMessageStore, eventData)
        );

        // then
        _ = enricher
            .Received()
            .Enrich(
                Arg.Any<Activity>(),
                Arg.Is<MessagingEnrichmentContext>(c =>
                    c.Kind == MessagingEventKind.Persist
                    && c.MessageName == "order.created"
                    && c.IntentType == IntentType.Queue
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public void should_continue_calling_enrichers_when_one_throws_and_logger_is_null()
    {
        // given - null logger is the production path: OTel AddInstrumentation cannot inject ILogger
        var failing = Substitute.For<IActivityTagEnricher>();
        failing
            .When(e =>
            {
                _ = e.Enrich(Arg.Any<Activity>(), Arg.Any<MessagingEnrichmentContext>(), Arg.Any<CancellationToken>());
            })
            .Throw(new InvalidOperationException("enricher failure"));

        var succeeding = Substitute.For<IActivityTagEnricher>();
        var listener = new DiagnosticListener([failing, succeeding]); // no logger
        using var activityListener = _CreateActivityListener();
        var eventData = _CreatePubStoreEventData("order.created");

        // when
        var act = () =>
            listener.OnNext(
                new KeyValuePair<string, object?>(MessageDiagnosticListenerNames.BeforePublishMessageStore, eventData)
            );

        // then - exception is swallowed and the second enricher still runs
        act.Should().NotThrow();
        _ = succeeding
            .Received()
            .Enrich(Arg.Any<Activity>(), Arg.Any<MessagingEnrichmentContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void should_not_propagate_enricher_exception_when_logger_is_null()
    {
        // given - the critical production path: logger is null because OTel AddInstrumentation
        // cannot accept Func<IServiceProvider, T>, so the enricher exception must be swallowed
        var failing = Substitute.For<IActivityTagEnricher>();
        failing
            .When(e =>
            {
                _ = e.Enrich(Arg.Any<Activity>(), Arg.Any<MessagingEnrichmentContext>(), Arg.Any<CancellationToken>());
            })
            .Throw(new InvalidOperationException("enricher failure"));

        var listener = new DiagnosticListener([failing]); // no logger
        using var activityListener = _CreateActivityListener();
        var eventData = _CreatePubStoreEventData("order.created");

        // when
        var act = () =>
            listener.OnNext(
                new KeyValuePair<string, object?>(MessageDiagnosticListenerNames.BeforePublishMessageStore, eventData)
            );

        // then - must not propagate; if it did, the diagnostic subscriber would crash and kill all observability
        act.Should().NotThrow();
    }

    [Fact]
    public void should_call_enrichers_when_publish_event_is_received()
    {
        // given
        var enricher = Substitute.For<IActivityTagEnricher>();
        var listener = new DiagnosticListener([enricher]);
        using var activityListener = _CreateActivityListener();
        var eventData = _CreatePubSendEventData("order.created");

        // when
        listener.OnNext(new KeyValuePair<string, object?>(MessageDiagnosticListenerNames.BeforePublish, eventData));

        // then
        _ = enricher
            .Received()
            .Enrich(
                Arg.Any<Activity>(),
                Arg.Is<MessagingEnrichmentContext>(c =>
                    c.Kind == MessagingEventKind.Publish
                    && c.MessageName == "order.created"
                    && c.IntentType == IntentType.Bus
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(IntentType.Bus, "bus", "topic")]
    [InlineData(IntentType.Queue, "queue", "queue")]
    public void should_emit_intent_tags_when_publish_event_is_received(
        IntentType intentType,
        string expectedIntent,
        string expectedDestinationKind
    )
    {
        // given
        var listener = new DiagnosticListener([new IntentTagEnricher()]);
        using var activityListener = _CreateActivityListener();
        var eventData = _CreatePubSendEventData("order.created");
        eventData.IntentType = intentType;

        // when
        listener.OnNext(new KeyValuePair<string, object?>(MessageDiagnosticListenerNames.BeforePublish, eventData));

        // then
        Activity.Current?.GetTagItem(MessagingTags.Intent).Should().Be(expectedIntent);
        Activity.Current?.GetTagItem(MessagingTags.DestinationKind).Should().Be(expectedDestinationKind);
    }

    [Fact]
    public void should_call_enrichers_when_consume_event_is_received()
    {
        // given
        var enricher = Substitute.For<IActivityTagEnricher>();
        var listener = new DiagnosticListener([enricher]);
        using var activityListener = _CreateActivityListener();
        var eventData = _CreateSubStoreEventData("order.created");

        // when
        listener.OnNext(new KeyValuePair<string, object?>(MessageDiagnosticListenerNames.BeforeConsume, eventData));

        // then
        _ = enricher
            .Received()
            .Enrich(
                Arg.Any<Activity>(),
                Arg.Is<MessagingEnrichmentContext>(c =>
                    c.Kind == MessagingEventKind.Consume
                    && c.MessageName == "order.created"
                    && c.IntentType == IntentType.Bus
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(IntentType.Bus, "bus", "topic")]
    [InlineData(IntentType.Queue, "queue", "queue")]
    public void should_emit_intent_tags_when_consume_event_is_received(
        IntentType intentType,
        string expectedIntent,
        string expectedDestinationKind
    )
    {
        // given
        var listener = new DiagnosticListener([new IntentTagEnricher()]);
        using var activityListener = _CreateActivityListener();
        var eventData = _CreateSubStoreEventData("order.created");
        eventData.IntentType = intentType;

        // when
        listener.OnNext(new KeyValuePair<string, object?>(MessageDiagnosticListenerNames.BeforeConsume, eventData));

        // then
        Activity.Current?.GetTagItem(MessagingTags.Intent).Should().Be(expectedIntent);
        Activity.Current?.GetTagItem(MessagingTags.DestinationKind).Should().Be(expectedDestinationKind);
    }

    [Fact]
    public void should_call_enrichers_when_subscriber_invoke_event_is_received()
    {
        // given
        var enricher = Substitute.For<IActivityTagEnricher>();
        var listener = new DiagnosticListener([enricher]);
        using var activityListener = _CreateActivityListener();
        var eventData = _CreateSubExecuteEventData("order.created", retryCount: 0);

        // when
        listener.OnNext(
            new KeyValuePair<string, object?>(MessageDiagnosticListenerNames.BeforeSubscriberInvoke, eventData)
        );

        // then
        _ = enricher
            .Received()
            .Enrich(
                Arg.Any<Activity>(),
                Arg.Is<MessagingEnrichmentContext>(c =>
                    c.Kind == MessagingEventKind.SubscriberInvoke
                    && c.MessageName == "order.created"
                    && c.IntentType == IntentType.Bus
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public void should_call_enrichers_with_queue_intent_when_subscriber_invoke_event_is_received()
    {
        // given
        var enricher = Substitute.For<IActivityTagEnricher>();
        var listener = new DiagnosticListener([enricher]);
        using var activityListener = _CreateActivityListener();
        var eventData = _CreateSubExecuteEventData("order.created", retryCount: 0);
        eventData.IntentType = IntentType.Queue;

        // when
        listener.OnNext(
            new KeyValuePair<string, object?>(MessageDiagnosticListenerNames.BeforeSubscriberInvoke, eventData)
        );

        // then
        _ = enricher
            .Received()
            .Enrich(
                Arg.Any<Activity>(),
                Arg.Is<MessagingEnrichmentContext>(c =>
                    c.Kind == MessagingEventKind.SubscriberInvoke
                    && c.MessageName == "order.created"
                    && c.IntentType == IntentType.Queue
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public void should_log_warning_when_enricher_throws_and_logger_is_wired()
    {
        // given - use a concrete capturing logger because DiagnosticListener is internal,
        // and NSubstitute cannot proxy ILogger<internal-type> without InternalsVisibleTo.
        var logger = new CapturingLogger();
        var failing = Substitute.For<IActivityTagEnricher>();
        failing
            .When(e =>
            {
                _ = e.Enrich(Arg.Any<Activity>(), Arg.Any<MessagingEnrichmentContext>(), Arg.Any<CancellationToken>());
            })
            .Throw(new InvalidOperationException("enricher failure"));

        var listener = new DiagnosticListener([failing], logger);
        using var activityListener = _CreateActivityListener();
        var eventData = _CreatePubStoreEventData("order.created");

        // when
        listener.OnNext(
            new KeyValuePair<string, object?>(MessageDiagnosticListenerNames.BeforePublishMessageStore, eventData)
        );

        // then - a Warning was logged via the LoggerMessage source generator
        logger.Entries.Should().ContainSingle();
        logger.Entries[0].Level.Should().Be(LogLevel.Warning);
        logger.Entries[0].Exception.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task should_swallow_async_enricher_exception_when_logger_is_wired()
    {
        // given - an enricher whose ValueTask completes asynchronously and then throws.
        // The DiagnosticListener fast-path observes the async tail via fire-and-forget; the
        // exception must be swallowed and logged, never propagated.
        var logger = new CapturingLogger();
        var listener = new DiagnosticListener([new AsyncThrowingEnricher()], logger);
        using var activityListener = _CreateActivityListener();
        var eventData = _CreatePubStoreEventData("order.created");

        // when
        listener.OnNext(
            new KeyValuePair<string, object?>(MessageDiagnosticListenerNames.BeforePublishMessageStore, eventData)
        );

        // The async tail runs on a continuation; poll briefly for the captured log entry.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (logger.Entries.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        // then - a Warning was logged for the async enricher exception
        logger.Entries.Should().ContainSingle();
        logger.Entries[0].Level.Should().Be(LogLevel.Warning);
        logger.Entries[0].Exception.Should().BeOfType<InvalidOperationException>();
    }

    private sealed class AsyncThrowingEnricher : IActivityTagEnricher
    {
        public ValueTask Enrich(
            Activity activity,
            in MessagingEnrichmentContext context,
            CancellationToken cancellationToken = default
        )
        {
            // Force the ValueTask to NOT complete synchronously so the listener takes the
            // fire-and-forget async-tail observer path. We cannot mark this method `async`
            // (the `in` parameter forbids it), so we wrap an async local in a Task and return
            // a non-completed ValueTask.
            return new ValueTask(throwAfterYieldAsync());

            static async Task throwAfterYieldAsync()
            {
                await Task.Yield();
                throw new InvalidOperationException("async enricher failure");
            }
        }
    }

    private sealed class CapturingLogger : ILogger<DiagnosticListener>
    {
        public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add((logLevel, exception, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose() { }
        }
    }

    [Fact]
    public void should_populate_tenant_id_and_correlation_id_in_enrichment_context_when_headers_present()
    {
        // given - construct event data with headers that include the tenant-id and correlation-id
        // wire headers; the listener should map them onto the MessagingEnrichmentContext fields
        // exposed to enrichers (the TenantId/CorrelationId positive branches in _BuildEnrichmentContext).
        var enricher = Substitute.For<IActivityTagEnricher>();
        var listener = new DiagnosticListener([enricher]);
        using var activityListener = _CreateActivityListener();

        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = "order.created",
            [Headers.TenantId] = "tenant-42",
            [Headers.CorrelationId] = "corr-7",
        };
        var eventData = new MessageEventDataPubStore
        {
            Operation = "order.created",
            Message = new Message(headers, null),
            OperationTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        // when
        listener.OnNext(
            new KeyValuePair<string, object?>(MessageDiagnosticListenerNames.BeforePublishMessageStore, eventData)
        );

        // then
        _ = enricher
            .Received()
            .Enrich(
                Arg.Any<Activity>(),
                Arg.Is<MessagingEnrichmentContext>(c => c.TenantId == "tenant-42" && c.CorrelationId == "corr-7"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public void should_log_warning_when_enricher_returns_synchronously_faulted_value_task()
    {
        // given - an enricher whose ValueTask is already completed AND faulted before _CallEnrichers
        // inspects it (vt.IsCompleted && !vt.IsCompletedSuccessfully branch). Distinct from an
        // enricher that throws synchronously (caught at the call site) or yields (async tail path).
        var logger = new CapturingLogger();
        var listener = new DiagnosticListener([new SyncFaultedEnricher()], logger);
        using var activityListener = _CreateActivityListener();
        var eventData = _CreatePubStoreEventData("order.created");

        // when
        listener.OnNext(
            new KeyValuePair<string, object?>(MessageDiagnosticListenerNames.BeforePublishMessageStore, eventData)
        );

        // then - a Warning was logged for the sync-faulted ValueTask
        logger.Entries.Should().ContainSingle();
        logger.Entries[0].Level.Should().Be(LogLevel.Warning);
        logger.Entries[0].Exception.Should().BeOfType<InvalidOperationException>();
        logger.Entries[0].Exception!.Message.Should().Be("sync-faulted");
    }

    private sealed class SyncFaultedEnricher : IActivityTagEnricher
    {
        public ValueTask Enrich(
            Activity activity,
            in MessagingEnrichmentContext context,
            CancellationToken cancellationToken = default
        )
        {
            return ValueTask.FromException(new InvalidOperationException("sync-faulted"));
        }
    }

    [Fact]
    public void should_pass_messaging_cancellation_token_to_enricher()
    {
        // given - construct event data carrying an explicit CT and a capturing enricher.
        // The listener must forward eventData.CancellationToken through _CallEnrichers into
        // IActivityTagEnricher.Enrich(activity, context, cancellationToken).
        using var cts = new CancellationTokenSource();
        var enricher = new CapturingEnricher();
        var listener = new DiagnosticListener([enricher]);
        using var activityListener = _CreateActivityListener();

        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = "order.created",
        };
        var eventData = new MessageEventDataPubStore
        {
            Operation = "order.created",
            Message = new Message(headers, null),
            OperationTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            CancellationToken = cts.Token,
        };

        // when
        listener.OnNext(
            new KeyValuePair<string, object?>(MessageDiagnosticListenerNames.BeforePublishMessageStore, eventData)
        );

        // then - the enricher saw the exact same token plumbed through the DTO
        enricher.LastReceivedCancellationToken.Should().Be(cts.Token);
    }

    private sealed class CapturingEnricher : IActivityTagEnricher
    {
        public CancellationToken LastReceivedCancellationToken { get; private set; }

        public ValueTask Enrich(
            Activity activity,
            in MessagingEnrichmentContext context,
            CancellationToken cancellationToken = default
        )
        {
            LastReceivedCancellationToken = cancellationToken;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public void should_set_retry_count_tag_when_retry_count_is_positive()
    {
        // given
        var listener = new DiagnosticListener([new RetryCountTagEnricher()]);
        using var activityListener = _CreateActivityListener();
        var eventData = _CreateSubExecuteEventData("order.created", retryCount: 3);

        // when
        listener.OnNext(
            new KeyValuePair<string, object?>(MessageDiagnosticListenerNames.BeforeSubscriberInvoke, eventData)
        );

        // then
        Activity.Current?.GetTagItem(MessagingTags.RetryCount).Should().Be(3);
    }

    [Fact]
    public void should_not_set_retry_count_tag_when_retry_count_is_zero()
    {
        // given
        var listener = new DiagnosticListener([new RetryCountTagEnricher()]);
        using var activityListener = _CreateActivityListener();
        var eventData = _CreateSubExecuteEventData("order.created", retryCount: 0);

        // when
        listener.OnNext(
            new KeyValuePair<string, object?>(MessageDiagnosticListenerNames.BeforeSubscriberInvoke, eventData)
        );

        // then
        Activity.Current?.GetTagItem(MessagingTags.RetryCount).Should().BeNull();
    }

    private static ActivityListener _CreateActivityListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source =>
                string.Equals(source.Name, DiagnosticListener.SourceName, StringComparison.Ordinal),
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static MessageEventDataPubStore _CreatePubStoreEventData(string operation)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = operation,
        };

        return new MessageEventDataPubStore
        {
            Operation = operation,
            Message = new Message(headers, null),
            OperationTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
    }

    private static MessageEventDataPubSend _CreatePubSendEventData(string operation)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = operation,
        };

        return new MessageEventDataPubSend
        {
            Operation = operation,
            TransportMessage = new TransportMessage(headers, ReadOnlyMemory<byte>.Empty),
            BrokerAddress = new BrokerAddress("Test$localhost:5672"),
            OperationTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
    }

    private static MessageEventDataSubStore _CreateSubStoreEventData(string operation)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = operation,
        };

        return new MessageEventDataSubStore
        {
            Operation = operation,
            TransportMessage = new TransportMessage(headers, ReadOnlyMemory<byte>.Empty),
            BrokerAddress = new BrokerAddress("Test$localhost:5672"),
            OperationTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
    }

    private static MessageEventDataSubExecute _CreateSubExecuteEventData(string operation, int retryCount)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = operation,
        };

        return new MessageEventDataSubExecute
        {
            Operation = operation,
            Message = new Message(headers, null),
            MethodInfo = typeof(DiagnosticListenerTests).GetMethod(
                nameof(_CreateSubExecuteEventData),
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly,
                binder: null,
                types: [typeof(string), typeof(int)],
                modifiers: null
            )!,
            OperationTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            RetryCount = retryCount,
        };
    }
}
