// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using NSubstitute;

namespace Tests.Module;

public sealed class MergedListCursorTests : TestBase
{
    private static readonly MessagingStore _Primary = new(
        "primary",
        Substitute.For<IDataStorage>(),
        HoldsReceived: true
    );

    private static readonly MessagingStore _Billing = new(
        "billing",
        Substitute.For<IDataStorage>(),
        HoldsReceived: false
    );

    [Theory]
    [InlineData(ListSortDirection.Ascending, "a,b,c,d")]
    [InlineData(ListSortDirection.Descending, "d,c,b,a")]
    public void should_merge_two_stores_by_name_in_the_query_direction(ListSortDirection direction, string expected)
    {
        // given: each store's page already in the query's order
        var query = new MessageListQuery
        {
            MessageType = MessageType.Publish,
            SortBy = MessageListSortKey.Name,
            SortDirection = direction,
        };
        var ascending = direction == ListSortDirection.Ascending;
        (MessagingStore, MessageListPage)[] pages =
        [
            (_Primary, _Page(ascending ? ["a", "c"] : ["c", "a"])),
            (_Billing, _Page(ascending ? ["b", "d"] : ["d", "b"])),
        ];

        // when
        var (rows, taken) = MergedListCursor.Merge(pages, query, take: 4);

        // then
        string.Join(',', rows.Select(static row => row.View.Name)).Should().Be(expected);
        taken["primary"].Should().Be(2);
        taken["billing"].Should().Be(2);
    }

    [Fact]
    public void should_round_trip_a_cursor_that_fits_only_its_own_sort_and_filters()
    {
        // given: a name-sorted page that leaves rows in one store
        var query = new MessageListQuery
        {
            MessageType = MessageType.Publish,
            SortBy = MessageListSortKey.Name,
            SortDirection = ListSortDirection.Ascending,
            Content = "ORD-991",
        };
        MergedListCursor.TryParse(null, out var first).Should().BeTrue();
        var page = _Page(["a", "b"]) with
        {
            Next = new MessageListPosition(DateTimeOffset.UnixEpoch, "b", Guid.NewGuid()),
        };
        var (_, taken) = MergedListCursor.Merge([(_Primary, page)], query, take: 2);

        // when
        var text = first.Advance(query, [_Primary], [(_Primary, page)], taken)!.ToString();

        // then: the search text stays out of the cursor, and only the same query takes it
        text.Should().NotContain("ORD-991");
        MergedListCursor.TryParse(text, out var parsed).Should().BeTrue();
        parsed.Fits(query).Should().BeTrue();
        parsed.Fits(query with { Content = "ORD-992" }).Should().BeFalse();
        parsed.Fits(query with { SortDirection = ListSortDirection.Descending }).Should().BeFalse();
        parsed.Fits(query with { SortBy = MessageListSortKey.Added }).Should().BeFalse();
        parsed.PositionOf("primary")!.Value.Name.Should().Be("b");
        MergedListCursor.TryParse("s1|primary=-", out _).Should().BeFalse();
    }

    private static MessageListPage _Page(string[] names) =>
        new()
        {
            Items =
            [
                .. names.Select(static name => new MessageView
                {
                    StorageId = Guid.NewGuid(),
                    MessageId = name,
                    Version = "v1",
                    Name = name,
                    StatusName = StatusName.Succeeded,
                }),
            ],
        };
}
