// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Messaging;
using Headless.Messaging.Persistence;

namespace Tests;

// The list filters, sorts, and searches the dashboard reads with. Every case filters by a name of its own, so rows
// other cases leave in a shared store never reach its assertions.
public abstract partial class DataStorageTestsBase
{
    public virtual async Task should_filter_a_list_by_added_time_with_an_exclusive_end()
    {
        // given: rows of one name, and the added times the store gave them
        var storage = GetStorage();
        var monitoring = storage.GetMonitoringApi();
        var name = $"added-range-{Guid.NewGuid():N}";
        for (var i = 0; i < 6; i++)
        {
            await storage.StoreMessageAsync(name, CreateMessage(), cancellationToken: AbortToken);
        }

        var all = await monitoring.ListMessagesAsync(
            new MessageListQuery { MessageType = MessageType.Publish, Name = name },
            after: null,
            pageSize: 50,
            AbortToken
        );
        var from = all.Items.Min(static row => row.Added);
        var before = all.Items.Max(static row => row.Added);

        // when: the range starts at the oldest row and ends at the newest
        var page = await monitoring.ListMessagesAsync(
            new MessageListQuery
            {
                MessageType = MessageType.Publish,
                Name = name,
                AddedFrom = from,
                AddedBefore = before,
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
                AddedFrom = from,
                AddedBefore = before,
            },
            cancellationToken: AbortToken
        );

        // then: the start is inclusive and the end exclusive
        var expected = all
            .Items.Where(row => row.Added >= from && row.Added < before)
            .Select(static row => row.StorageId)
            .ToList();
        page.Items.Select(static row => row.StorageId).Should().BeEquivalentTo(expected);
        page.Items.Should().NotContain(row => row.Added == before);
        count.Should().Be(new MessageCount(expected.Count, Capped: false));
    }

    public virtual async Task should_page_a_name_sorted_list_by_name_then_id_without_gaps_or_repeats()
    {
        // given: rows under three names that share a prefix, several rows per name, so the id breaks the ties; the
        // search for the prefix, which the envelope's name header holds, keeps other cases' rows out
        var storage = GetStorage();
        var monitoring = storage.GetMonitoringApi();
        var prefix = $"name-sort-{Guid.NewGuid():N}-";
        var stored = new List<(string Name, Guid Id)>();
        foreach (var suffix in new[] { "b", "a", "c", "a", "b", "a", "c", "b" })
        {
            var message = await storage.StoreMessageAsync(
                prefix + suffix,
                CreateMessage(messageName: prefix + suffix),
                cancellationToken: AbortToken
            );
            stored.Add((prefix + suffix, message.StorageId));
        }

        foreach (var direction in new[] { ListSortDirection.Ascending, ListSortDirection.Descending })
        {
            // when: the list is read three rows at a time
            var seen = new List<MessageView>();
            MessageListPosition? after = null;
            var pages = 0;
            do
            {
                var page = await monitoring.ListMessagesAsync(
                    new MessageListQuery
                    {
                        MessageType = MessageType.Publish,
                        SortBy = MessageListSortKey.Name,
                        SortDirection = direction,
                        Content = prefix,
                    },
                    after,
                    pageSize: 3,
                    AbortToken
                );
                seen.AddRange(page.Items);
                after = page.Next;
                pages++;
            } while (after is not null && pages < 10);

            // then: every row once, names in order, and one name's rows in the store's id order on every page
            seen.Select(static row => row.StorageId).Should().OnlyHaveUniqueItems();
            seen.Select(static row => row.StorageId).Should().BeEquivalentTo(stored.Select(static row => row.Id));
            var names = seen.Select(static row => row.Name).ToList();
            names
                .Should()
                .Equal(
                    direction == ListSortDirection.Ascending
                        ? names.Order(StringComparer.Ordinal)
                        : names.OrderDescending(StringComparer.Ordinal)
                );

            // The store orders ids its own way, so one read of all rows is the order every paged read must match.
            var whole = await monitoring.ListMessagesAsync(
                new MessageListQuery
                {
                    MessageType = MessageType.Publish,
                    SortBy = MessageListSortKey.Name,
                    SortDirection = direction,
                    Content = prefix,
                },
                after: null,
                pageSize: 50,
                AbortToken
            );
            seen.Select(static row => row.StorageId).Should().Equal(whole.Items.Select(static row => row.StorageId));
        }
    }

    public virtual async Task should_search_content_as_a_literal_substring_without_regard_to_case()
    {
        // given: a body with an order number and a percent sign, and one that differs only where a wildcard would match
        var storage = GetStorage();
        var monitoring = storage.GetMonitoringApi();
        var name = $"content-search-{Guid.NewGuid():N}";
        var order = await storage.StoreMessageAsync(
            name,
            CreateMessage(value: new { Order = "ORD-991", Discount = "50%" }),
            cancellationToken: AbortToken
        );
        var wildcard = await storage.StoreMessageAsync(
            name,
            CreateMessage(value: new { Order = "ORD-992", Discount = "50x" }),
            cancellationToken: AbortToken
        );

        // when
        async Task<IReadOnlyList<Guid>> search(string text) =>
            [
                .. (
                    await monitoring.ListMessagesAsync(
                        new MessageListQuery
                        {
                            MessageType = MessageType.Publish,
                            Name = name,
                            Content = text,
                        },
                        after: null,
                        pageSize: 50,
                        AbortToken
                    )
                ).Items.Select(static row => row.StorageId),
            ];

        // then
        (await search("ord-991"))
            .Should()
            .Equal(order.StorageId);
        (await search("50%")).Should().Equal(order.StorageId);
        (await search("ORD-99")).Should().BeEquivalentTo([order.StorageId, wildcard.StorageId]);
        (await search("ORD-993")).Should().BeEmpty();
        (
            await monitoring.CountMessagesAsync(
                new MessageListQuery
                {
                    MessageType = MessageType.Publish,
                    Name = name,
                    Content = "50%",
                },
                cancellationToken: AbortToken
            )
        ).Should().Be(new MessageCount(1, Capped: false));
    }

    public virtual async Task should_find_a_correlation_id_exactly_and_not_a_body_that_merely_contains_it()
    {
        // given: a message with the correlation id, one whose correlation id only starts with it, and one whose body
        // holds the id while its correlation id is another
        var storage = GetStorage();
        var monitoring = storage.GetMonitoringApi();
        var name = $"correlation-search-{Guid.NewGuid():N}";
        const string correlationId = "c-42";
        var correlated = await storage.StoreMessageAsync(
            name,
            _WithCorrelation(CreateMessage(), correlationId),
            cancellationToken: AbortToken
        );
        await storage.StoreMessageAsync(
            name,
            _WithCorrelation(CreateMessage(), correlationId + "0"),
            cancellationToken: AbortToken
        );
        await storage.StoreMessageAsync(
            name,
            _WithCorrelation(CreateMessage(value: new { Note = correlationId, Reference = correlationId }), "c-other"),
            cancellationToken: AbortToken
        );
        var received = await storage.StoreReceivedMessageAsync(
            name,
            "correlation.consumer",
            _WithCorrelation(CreateMessage(), correlationId),
            AbortToken
        );

        // when
        var published = await monitoring.ListMessagesAsync(
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
        var inbox = await monitoring.ListMessagesAsync(
            new MessageListQuery
            {
                MessageType = MessageType.Subscribe,
                Name = name,
                CorrelationId = correlationId,
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
                CorrelationId = correlationId,
            },
            cancellationToken: AbortToken
        );

        // then
        published.Items.Select(static row => row.StorageId).Should().Equal(correlated.StorageId);
        inbox.Items.Select(static row => row.StorageId).Should().Equal(received.StorageId);
        count.Should().Be(new MessageCount(1, Capped: false));
    }

    public virtual async Task should_filter_a_list_by_lane()
    {
        // given: one bus row and one queue row of one name
        var storage = GetStorage();
        var monitoring = storage.GetMonitoringApi();
        var name = $"lane-filter-{Guid.NewGuid():N}";
        var bus = await storage.StoreMessageAsync(name, _Medium(MessageLane.Bus), cancellationToken: AbortToken);
        var queue = await storage.StoreMessageAsync(name, _Medium(MessageLane.Queue), cancellationToken: AbortToken);

        // when
        async Task<IReadOnlyList<Guid>> list(MessageLane lane) =>
            [
                .. (
                    await monitoring.ListMessagesAsync(
                        new MessageListQuery
                        {
                            MessageType = MessageType.Publish,
                            Name = name,
                            Lane = lane,
                        },
                        after: null,
                        pageSize: 50,
                        AbortToken
                    )
                ).Items.Select(static row => row.StorageId),
            ];

        // then
        (await list(MessageLane.Queue))
            .Should()
            .Equal(queue.StorageId);
        (await list(MessageLane.Bus)).Should().Equal(bus.StorageId);
    }

    private static Message _WithCorrelation(Message message, string correlationId)
    {
        message.Headers[Headers.CorrelationId] = correlationId;

        return message;
    }

    private static MediumMessage _Medium(MessageLane lane) =>
        new()
        {
            StorageId = Guid.Empty,
            Origin = CreateMessage(),
            Content = string.Empty,
            Lane = lane,
        };
}
