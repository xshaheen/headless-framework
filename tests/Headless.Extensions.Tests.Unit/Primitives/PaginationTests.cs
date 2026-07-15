// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Tests.Primitives;

public sealed class PaginationTests
{
    #region IndexPage - Construction & Properties

    [Fact]
    public void should_store_items_and_pagination_info_when_index_page()
    {
        // given
        var items = new[] { "a", "b", "c" };

        // when
        var page = new IndexPage<string>(items, index: 0, size: 10, totalItems: 100);

        // then
        page.Items.Should().BeEquivalentTo(items);
        page.Index.Should().Be(0);
        page.Size.Should().Be(10);
        page.TotalItems.Should().Be(100);
    }

    [Fact]
    public void should_calculate_total_pages_when_index_page()
    {
        // when
        var page = new IndexPage<int>([1, 2, 3], index: 0, size: 10, totalItems: 100);

        // then
        page.TotalPages.Should().Be(10);
    }

    [Fact]
    public void should_calculate_total_pages_with_remainder_when_index_page()
    {
        // when
        var page = new IndexPage<int>([1, 2, 3], index: 0, size: 10, totalItems: 95);

        // then
        page.TotalPages.Should().Be(10); // ceil(95/10) = 10
    }

    [Fact]
    public void should_return_zero_total_pages_when_index_page_no_items()
    {
        // when
        var page = new IndexPage<int>([], index: 0, size: 10, totalItems: 0);

        // then
        page.TotalPages.Should().Be(0);
    }

    [Fact]
    public void should_return_zero_total_pages_when_index_page_size_is_zero()
    {
        // when
        var page = new IndexPage<int>([1, 2], index: 0, size: 0, totalItems: 10);

        // then
        page.TotalPages.Should().Be(0);
    }

    #endregion

    #region IndexPage - HasPrevious & HasNext

    [Fact]
    public void should_identify_has_previous_page_when_index_page()
    {
        // when
        var firstPage = new IndexPage<int>([1], index: 0, size: 10, totalItems: 100);
        var secondPage = new IndexPage<int>([2], index: 1, size: 10, totalItems: 100);

        // then
        firstPage.HasPrevious.Should().BeFalse();
        secondPage.HasPrevious.Should().BeTrue();
    }

    [Fact]
    public void should_identify_has_next_page_when_index_page()
    {
        // given
        const int totalPages = 10; // 100 items / 10 per page

        // when
        var firstPage = new IndexPage<int>([1], index: 0, size: 10, totalItems: 100);
        var lastPage = new IndexPage<int>([10], index: totalPages - 1, size: 10, totalItems: 100);

        // then
        firstPage.HasNext.Should().BeTrue();
        lastPage.HasNext.Should().BeFalse();
    }

    [Fact]
    public void should_handle_single_page_when_index_page()
    {
        // when
        var page = new IndexPage<int>([1, 2, 3], index: 0, size: 10, totalItems: 3);

        // then
        page.TotalPages.Should().Be(1);
        page.HasPrevious.Should().BeFalse();
        page.HasNext.Should().BeFalse();
    }

    [Fact]
    public void should_handle_empty_page_when_index_page()
    {
        // when
        var page = new IndexPage<int>([], index: 0, size: 10, totalItems: 0);

        // then
        page.TotalPages.Should().Be(0);
        page.HasPrevious.Should().BeFalse();
        page.HasNext.Should().BeFalse();
    }

    #endregion

    #region IndexPage - Select & Where

    [Fact]
    public void should_transform_items_when_index_page_select()
    {
        // given
        var page = new IndexPage<int>([1, 2, 3], index: 0, size: 10, totalItems: 100);

        // when
        var mapped = page.Select(x => x.ToString(CultureInfo.InvariantCulture));

        // then
        mapped.Items.Should().BeEquivalentTo(["1", "2", "3"]);
        mapped.Index.Should().Be(0);
        mapped.Size.Should().Be(10);
        mapped.TotalItems.Should().Be(100);
    }

    [Fact]
    public void should_filter_items_when_index_page_where()
    {
        // given
        var page = new IndexPage<int>([1, 2, 3, 4, 5], index: 0, size: 10, totalItems: 100);

        // when
        var filtered = page.Where(x => x > 2);

        // then
        filtered.Items.Should().BeEquivalentTo([3, 4, 5]);
        filtered.Index.Should().Be(0);
        filtered.Size.Should().Be(10);
        filtered.TotalItems.Should().Be(100);
    }

    #endregion

    #region ContinuationPage - Construction & Properties

    [Fact]
    public void should_store_items_and_token_when_continuation_page()
    {
        // given
        var items = new[] { "a", "b", "c" };
        const string token = "next-page-token";

        // when
        var page = new ContinuationPage<string>(items, size: 10, continuationToken: token);

        // then
        page.Items.Should().BeEquivalentTo(items);
        page.Size.Should().Be(10);
        page.ContinuationToken.Should().Be(token);
    }

    [Fact]
    public void should_expose_continuation_token_when_continuation_page()
    {
        // given
        const string token = "abc123";

        // when
        var page = new ContinuationPage<int>([1, 2], size: 10, continuationToken: token);

        // then
        page.ContinuationToken.Should().Be(token);
    }

    [Fact]
    public void should_allow_null_token_when_continuation_page()
    {
        // when
        var page = new ContinuationPage<int>([1, 2], size: 10, continuationToken: null);

        // then
        page.ContinuationToken.Should().BeNull();
    }

    #endregion

    #region ContinuationPage - HasNext

    [Fact]
    public void should_have_no_next_when_continuation_page_token_is_null()
    {
        // when
        var page = new ContinuationPage<int>([1], size: 10, continuationToken: null);

        // then
        page.HasNext.Should().BeFalse();
    }

    [Fact]
    public void should_have_next_when_continuation_page_token_exists()
    {
        // when
        var page = new ContinuationPage<int>([1], size: 10, continuationToken: "next");

        // then
        page.HasNext.Should().BeTrue();
    }

    #endregion

    #region ContinuationPage - Select & Where

    [Fact]
    public void should_transform_items_when_continuation_page_select()
    {
        // given
        var page = new ContinuationPage<int>([1, 2, 3], size: 10, continuationToken: "token");

        // when
        var mapped = page.Select(x => x.ToString(CultureInfo.InvariantCulture));

        // then
        mapped.Items.Should().BeEquivalentTo(["1", "2", "3"]);
        mapped.Size.Should().Be(10);
        mapped.ContinuationToken.Should().Be("token");
    }

    [Fact]
    public void should_filter_items_when_continuation_page_where()
    {
        // given
        var page = new ContinuationPage<int>([1, 2, 3, 4, 5], size: 10, continuationToken: "token");

        // when
        var filtered = page.Where(x => x > 2);

        // then
        filtered.Items.Should().BeEquivalentTo([3, 4, 5]);
        filtered.Size.Should().Be(10);
        filtered.ContinuationToken.Should().Be("token");
    }

    [Fact]
    public void should_preserve_null_token_when_continuation_page_select()
    {
        // given
        var page = new ContinuationPage<int>([1, 2], size: 10, continuationToken: null);

        // when
        var mapped = page.Select(x => x * 2);

        // then
        mapped.ContinuationToken.Should().BeNull();
    }

    #endregion
}
