// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Persistence;

namespace Tests;

// An Operation scoped by a content or correlation id search: the scope reads the rows the list's search finds, and
// every chunk applies the search again, so a row is acted on only while it still matches.
public abstract partial class DataStorageTestsBase
{
    public virtual async Task should_scope_a_published_operation_to_the_rows_a_search_matches()
    {
        // given: three failures for good under one name: one holding the order number, one carrying the correlation
        // id, and one whose body only mentions that id
        var storage = GetStorage();
        var published = storage.GetPublishedOperationsApi();
        var name = $"published-search-scope-{Guid.NewGuid():N}";
        var order = await _FailPublishedMessageAsync(storage, name, CreateMessage(value: new { Order = "ORD-991" }));
        var correlated = await _FailPublishedMessageAsync(
            storage,
            name,
            _WithCorrelation(CreateMessage(value: new { Order = "ORD-100" }), "c-42")
        );
        var mentions = await _FailPublishedMessageAsync(
            storage,
            name,
            _WithCorrelation(CreateMessage(value: new { Note = "c-42" }), "c-other")
        );
        var scope = new MessageOperationScope
        {
            Kind = MessageOperationKind.Resend,
            Query = new MessageListQuery { MessageType = MessageType.Publish, Name = name },
            HighWaterMark = (await storage.GetLatestMessageChangeMarkerAsync(AbortToken))!.Value,
        };
        var byContent = scope with { Query = scope.Query with { Content = "ord-991" } };
        var byCorrelation = scope with { Query = scope.Query with { CorrelationId = "C-42" } };

        // when: each search scopes the Operation, read one row per chunk
        var contentCount = await published.CountOperationScopeAsync(byContent, cancellationToken: AbortToken);
        var contentTargets = await _ReadWholePublishedScopeAsync(published, byContent, chunkSize: 1);
        var correlationCount = await published.CountOperationScopeAsync(byCorrelation, cancellationToken: AbortToken);
        var correlationTargets = await _ReadWholePublishedScopeAsync(published, byCorrelation, chunkSize: 1);

        // then: content matches as a substring without regard to case, and the correlation id matches its header
        // only, never a body that mentions it
        contentCount.Should().Be(new MessageCount(1, Capped: false));
        contentTargets.Select(static target => target.StorageId).Should().Equal(order.StorageId);
        correlationCount.Should().Be(new MessageCount(1, Capped: false));
        correlationTargets.Select(static target => target.StorageId).Should().Equal(correlated.StorageId);
        correlationTargets.Should().NotContain(target => target.StorageId == mentions.StorageId);
    }

    public virtual async Task should_scope_an_inbox_operation_to_the_generations_a_content_search_matches()
    {
        // given: two terminal failures of one consumer, one holding the order number
        var storage = GetStorage();
        var operations = storage.GetInboxOperationsApi();
        var consumer = $"search-scope-{Guid.NewGuid():N}";
        var order = await _AdmitTerminalInboxAsync(
            storage,
            consumer,
            CreateMessage($"search-{Guid.NewGuid():N}", "orders.created", new { Order = "ORD-991" })
        );
        await _AdmitTerminalInboxAsync(
            storage,
            consumer,
            CreateMessage($"search-{Guid.NewGuid():N}", "orders.created", new { Order = "ORD-100" })
        );
        var scope = new MessageOperationScope
        {
            Kind = MessageOperationKind.ReExecute,
            Query = new MessageListQuery
            {
                MessageType = MessageType.Subscribe,
                ConsumerIdentity = consumer,
                Content = "ord-991",
            },
            HighWaterMark = (await storage.GetLatestMessageChangeMarkerAsync(AbortToken))!.Value,
        };

        // when: the Operation runs over its scope, then reads the scope again
        var count = await operations.CountOperationScopeAsync(scope, cancellationToken: AbortToken);
        var targets = await _ReadWholeScopeAsync(operations, scope, chunkSize: 1);
        var applied = new List<InboxOperationResult>();
        foreach (var target in targets)
        {
            applied.Add(await operations.ForceReprocessAsync(_OperatorRequest(target, "operator-a"), AbortToken));
        }

        var rerun = await _ReadWholeScopeAsync(operations, scope, chunkSize: 1);

        // then: only the matching generation was re-executed, and a second read of the scope finds nothing left: the
        // child holds the same content but was written after the mark
        count.Should().Be(new MessageCount(1, Capped: false));
        targets.Select(static target => target.IncarnationId).Should().Equal(order.InboxGeneration!.IncarnationId);
        applied.Should().ContainSingle().Which.Outcome.Should().Be(InboxOperationOutcome.Applied);
        rerun.Should().BeEmpty();
    }

    private static async Task<MediumMessage> _FailPublishedMessageAsync(
        IDataStorage storage,
        string name,
        Message message
    )
    {
        var stored = await storage.StoreMessageAsync(name, message, cancellationToken: AbortToken);
        (
            await storage.ChangePublishStateAsync(
                stored,
                StatusName.Failed,
                retryDelay: null,
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();

        return stored;
    }
}
