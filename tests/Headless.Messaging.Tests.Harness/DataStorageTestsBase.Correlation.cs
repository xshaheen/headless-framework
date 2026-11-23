// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Messaging;
using Headless.Messaging.Persistence;

namespace Tests;

// The correlation id lookup: an exact, ordinal match on a column of its own, shared by the list filter and the trace
// read. Every case prefixes its ids with a fresh value, because the trace read has no name filter and rows other cases
// leave in a shared store must never reach its assertions.
public abstract partial class DataStorageTestsBase
{
    public virtual async Task should_match_a_correlation_id_ordinally_in_the_list_filter()
    {
        // given: three ids that differ only by case and a trailing space, on both tables
        var storage = GetStorage();
        var monitoring = storage.GetMonitoringApi();
        var prefix = $"ord-{Guid.NewGuid():N}-";
        var name = $"correlation-ordinal-{Guid.NewGuid():N}";
        var variants = new[] { prefix + "C-42", prefix + "c-42", prefix + "c-42 " };
        var published = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var received = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var variant in variants)
        {
            published[variant] = (
                await storage.StoreMessageAsync(name, _CorrelatedMessage(variant), cancellationToken: AbortToken)
            ).StorageId;
            received[variant] = (
                await storage.StoreReceivedMessageAsync(
                    name,
                    $"correlation.ordinal.{Guid.NewGuid():N}",
                    _CorrelatedMessage(variant),
                    AbortToken
                )
            ).StorageId;
        }

        // when / then: each id finds its own row on each table and no other
        foreach (var variant in variants)
        {
            var publishedPage = await monitoring.ListMessagesAsync(
                new MessageListQuery
                {
                    MessageType = MessageType.Publish,
                    Name = name,
                    CorrelationId = variant,
                },
                after: null,
                pageSize: 50,
                AbortToken
            );
            var receivedPage = await monitoring.ListMessagesAsync(
                new MessageListQuery
                {
                    MessageType = MessageType.Subscribe,
                    Name = name,
                    CorrelationId = variant,
                },
                after: null,
                pageSize: 50,
                AbortToken
            );
            var count = await monitoring.CountMessagesAsync(
                new MessageListQuery
                {
                    MessageType = MessageType.Publish,
                    Name = name,
                    CorrelationId = variant,
                },
                cancellationToken: AbortToken
            );

            publishedPage.Items.Select(static row => row.StorageId).Should().Equal(published[variant]);
            receivedPage.Items.Select(static row => row.StorageId).Should().Equal(received[variant]);
            count.Should().Be(new MessageCount(1, Capped: false));
        }
    }

    public virtual async Task should_trace_a_correlation_id_ordinally()
    {
        // given: three ids that differ only by case and a trailing space, on both tables
        var storage = GetStorage();
        var monitoring = storage.GetMonitoringApi();
        var prefix = $"trace-ord-{Guid.NewGuid():N}-";
        var name = $"trace-ordinal-{Guid.NewGuid():N}";
        var published = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var received = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var variant in new[] { prefix + "C-42", prefix + "c-42", prefix + "c-42 " })
        {
            published[variant] = (
                await storage.StoreMessageAsync(name, _CorrelatedMessage(variant), cancellationToken: AbortToken)
            ).StorageId;
            received[variant] = (
                await storage.StoreReceivedMessageAsync(
                    name,
                    $"trace.ordinal.{Guid.NewGuid():N}",
                    _CorrelatedMessage(variant),
                    AbortToken
                )
            ).StorageId;
        }

        // when / then: each id returns its own row of each table and no other
        foreach (var variant in published.Keys)
        {
            var trace = await monitoring.GetCorrelatedMessagesAsync(
                new MessageTraceQuery { CorrelationId = variant, Limit = 10 },
                AbortToken
            );

            trace.Published.Select(static row => row.StorageId).Should().Equal(published[variant]);
            trace.Received.Select(static row => row.StorageId).Should().Equal(received[variant]);
        }
    }

    public virtual async Task should_trace_a_published_message_and_every_generation_delivered_from_it()
    {
        // given: a published message and a received one that a replay then followed with a second generation
        var storage = GetStorage();
        var monitoring = storage.GetMonitoringApi();
        var operations = storage.GetInboxOperationsApi();
        var correlationId = $"trace-chain-{Guid.NewGuid():N}";
        var causationId = $"cause-{Guid.NewGuid():N}";
        var origin = _CorrelatedMessage(correlationId, causationId, $"trace-chain-msg-{Guid.NewGuid():N}");
        var messageId = origin.Id;
        var published = await storage.StoreMessageAsync(origin.Name, origin, cancellationToken: AbortToken);
        var root = await _AdmitInboxAsync(storage, origin);
        await _CompleteInboxForReplayAsync(storage, root.Message);
        var child = await operations.ForceReprocessAsync(
            new InboxOperationRequest(
                Guid.NewGuid(),
                root.Message.InboxGeneration!.IncarnationId,
                StatusName.Failed,
                "replay for the trace",
                new OperatorAuthorizationContext(
                    new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "trace-operator")], "test"))
                )
            ),
            AbortToken
        );
        child.Outcome.Should().Be(InboxOperationOutcome.Applied);

        // when
        var trace = await monitoring.GetCorrelatedMessagesAsync(
            new MessageTraceQuery { CorrelationId = correlationId, Limit = 10 },
            AbortToken
        );

        // then: the published row, and both generations with the causation the envelope carried, the replay's child
        // included
        trace.Published.Should().ContainSingle();
        trace.Published[0].StorageId.Should().Be(published.StorageId);
        trace.Published[0].MessageId.Should().Be(messageId);
        trace.Published[0].CausationId.Should().Be(causationId);
        trace
            .Received.Select(static row => row.StorageId)
            .Should()
            .BeEquivalentTo([root.Message.StorageId, child.ChildStorageId!.Value]);
        trace.Received.Should().OnlyContain(row => row.MessageId == messageId && row.CausationId == causationId);
    }

    public virtual async Task should_trace_the_oldest_rows_first_and_one_past_the_limit()
    {
        // given: 700 published rows of one correlation id
        var storage = GetStorage();
        var monitoring = storage.GetMonitoringApi();
        var correlationId = $"trace-limit-{Guid.NewGuid():N}";
        var name = $"trace-limit-{Guid.NewGuid():N}";
        var stored = new List<Guid>();
        for (var i = 0; i < 700; i++)
        {
            stored.Add(
                (
                    await storage.StoreMessageAsync(
                        name,
                        _CorrelatedMessage(correlationId),
                        cancellationToken: AbortToken
                    )
                ).StorageId
            );
        }

        // when
        var limited = await monitoring.GetCorrelatedMessagesAsync(
            new MessageTraceQuery { CorrelationId = correlationId, Limit = 500 },
            AbortToken
        );
        var whole = await monitoring.GetCorrelatedMessagesAsync(
            new MessageTraceQuery { CorrelationId = correlationId, Limit = 1000 },
            AbortToken
        );

        // then: one row past the limit, and they are the oldest ones, in the order a read of every row gives
        limited.Published.Should().HaveCount(501);
        limited.Published.Select(static row => row.Added).Should().BeInAscendingOrder();
        whole.Published.Select(static row => row.StorageId).Order().Should().Equal(stored.Order());
        var oldest = whole.Published.Take(501).Select(static row => row.StorageId).ToList();
        limited.Published.Select(static row => row.StorageId).Should().Equal(oldest);
        limited.Received.Should().BeEmpty();
    }

    public virtual async Task should_not_trace_a_correlation_id_longer_than_the_column_and_still_store_the_message()
    {
        // given: a received message whose correlation id is one character over what a column holds, and one that fits
        var storage = GetStorage();
        var monitoring = storage.GetMonitoringApi();
        var name = $"trace-long-{Guid.NewGuid():N}";
        var fits = $"{Guid.NewGuid():N}".PadRight(200, 'x');
        var tooLong = $"{Guid.NewGuid():N}".PadRight(201, 'x');
        var stored = await storage.StoreReceivedMessageAsync(
            name,
            $"trace.long.{Guid.NewGuid():N}",
            _CorrelatedMessage(tooLong),
            AbortToken
        );
        var fitting = await storage.StoreReceivedMessageAsync(
            name,
            $"trace.long.{Guid.NewGuid():N}",
            _CorrelatedMessage(fits),
            AbortToken
        );

        // when
        var found = await monitoring.GetCorrelatedMessagesAsync(
            new MessageTraceQuery { CorrelationId = fits, Limit = 10 },
            AbortToken
        );
        var notFound = await monitoring.GetCorrelatedMessagesAsync(
            new MessageTraceQuery { CorrelationId = tooLong, Limit = 10 },
            AbortToken
        );
        var listed = await monitoring.ListMessagesAsync(
            new MessageListQuery
            {
                MessageType = MessageType.Subscribe,
                Name = name,
                CorrelationId = tooLong,
            },
            after: null,
            pageSize: 50,
            AbortToken
        );

        // then: the receive stored the row, the id at the limit is found, and the longer one is not found anywhere
        (await monitoring.GetReceivedMessageAsync(stored.StorageId, AbortToken))
            .Should()
            .NotBeNull();
        found.Received.Select(static row => row.StorageId).Should().Equal(fitting.StorageId);
        notFound.Received.Should().BeEmpty();
        notFound.Published.Should().BeEmpty();
        listed.Items.Should().BeEmpty();
    }

    public virtual async Task should_not_find_a_correlation_id_that_only_the_body_holds()
    {
        // given: a row whose body holds the header's exact text under another correlation id
        var storage = GetStorage();
        var monitoring = storage.GetMonitoringApi();
        var name = $"correlation-body-{Guid.NewGuid():N}";
        var correlationId = $"c-body-{Guid.NewGuid():N}";
        var impostor = _CorrelatedMessage(
            "c-other",
            causationId: null,
            messageId: null,
            value: new Dictionary<string, string> { [Headers.CorrelationId] = correlationId }
        );
        await storage.StoreMessageAsync(name, impostor, cancellationToken: AbortToken);

        // when
        var listed = await monitoring.ListMessagesAsync(
            new MessageListQuery
            {
                MessageType = MessageType.Publish,
                Name = name,
                CorrelationId = correlationId,
            },
            after: null,
            pageSize: 50,
            AbortToken
        );
        var trace = await monitoring.GetCorrelatedMessagesAsync(
            new MessageTraceQuery { CorrelationId = correlationId, Limit = 10 },
            AbortToken
        );

        // then
        listed.Items.Should().BeEmpty();
        trace.Published.Should().BeEmpty();
    }

    private static Message _CorrelatedMessage(
        string correlationId,
        string? causationId = null,
        string? messageId = null,
        object? value = null
    )
    {
        var message = CreateMessage(messageId, value: value);
        message.Headers[Headers.CorrelationId] = correlationId;

        if (causationId is not null)
        {
            message.Headers[Headers.CausationId] = causationId;
        }

        return message;
    }
}
