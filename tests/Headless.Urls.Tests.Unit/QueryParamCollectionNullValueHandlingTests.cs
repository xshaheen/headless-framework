// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.Urls;

namespace Tests;

public sealed class QueryParamCollectionNullValueHandlingTests : TestBase
{
    [Fact]
    public void should_keep_every_repeated_value_when_add_or_replace_ignores_null()
    {
        // given
        var query = new QueryParamCollection("tag=first&sort=asc&tag=second");

        // when
        query.AddOrReplace("tag", value: null, nullValueHandling: NullValueHandling.Ignore);

        // then
        query.ToString().Should().Be("tag=first&sort=asc&tag=second");
    }

    [Fact]
    public void should_keep_every_repeated_value_when_set_query_param_ignores_null()
    {
        // given
        var url = new Url("http://example.com?tag=first&sort=asc&tag=second");

        // when
        url.SetQueryParam("tag", value: null, NullValueHandling.Ignore);

        // then
        url.ToString().Should().Be("http://example.com?tag=first&sort=asc&tag=second");
    }

    [Fact]
    public void should_keep_written_values_when_remove_null_trails_the_replacement_collection()
    {
        // given
        var query = new QueryParamCollection("x=1&y=2");

        // when - the null element lands past the single existing slot, so it is appended, not replaced
        query.AddOrReplace("x", new object?[] { "a", "b", null }, nullValueHandling: NullValueHandling.Remove);

        // then
        query.ToString().Should().Be("x=a&y=2&x=b");
    }

    [Fact]
    public void should_leave_single_value_when_add_or_replace_ignores_null()
    {
        // given
        var query = new QueryParamCollection("tag=first&sort=asc");

        // when
        query.AddOrReplace("tag", value: null, nullValueHandling: NullValueHandling.Ignore);

        // then
        query.ToString().Should().Be("tag=first&sort=asc");
    }

    [Fact]
    public void should_leave_query_unchanged_when_ignoring_null_for_a_missing_name()
    {
        // given
        var query = new QueryParamCollection("sort=asc");

        // when
        query.AddOrReplace("tag", value: null, nullValueHandling: NullValueHandling.Ignore);

        // then
        query.ToString().Should().Be("sort=asc");
    }

    [Fact]
    public void should_replace_slot_by_slot_and_skip_null_elements_when_ignoring_null_in_a_collection()
    {
        // given
        var query = new QueryParamCollection("tag=first&sort=asc&tag=second");

        // when - a null element keeps its slot; a non-null element replaces its slot
        query.AddOrReplace("tag", new object?[] { null, "new" }, nullValueHandling: NullValueHandling.Ignore);

        // then
        query.ToString().Should().Be("tag=first&sort=asc&tag=new");
    }

    [Fact]
    public void should_remove_every_repeated_value_when_add_or_replace_removes_null()
    {
        // given
        var query = new QueryParamCollection("tag=first&sort=asc&tag=second");

        // when
        query.AddOrReplace("tag", value: null, nullValueHandling: NullValueHandling.Remove);

        // then
        query.ToString().Should().Be("sort=asc");
    }

    [Fact]
    public void should_write_one_name_only_param_when_add_or_replace_uses_name_only_for_null()
    {
        // given
        var query = new QueryParamCollection("tag=first&sort=asc&tag=second");

        // when
        query.AddOrReplace("tag", value: null, nullValueHandling: NullValueHandling.NameOnly);

        // then - a scalar is one value, so the first slot becomes name-only and the extra slot goes
        query.ToString().Should().Be("tag&sort=asc");
    }

    [Fact]
    public void should_append_name_only_param_when_add_or_replace_uses_name_only_for_null_in_a_collection()
    {
        // given
        var query = new QueryParamCollection("x=1");

        // when
        query.AddOrReplace("x", new object?[] { "a", null }, nullValueHandling: NullValueHandling.NameOnly);

        // then
        query.ToString().Should().Be("x=a&x");
    }

    [Fact]
    public void should_skip_null_elements_when_add_removes_null_in_a_collection()
    {
        // given
        var query = new QueryParamCollection("x=1");

        // when
        query.Add("x", new object?[] { "a", null, "b" }, nullValueHandling: NullValueHandling.Remove);

        // then - only a scalar null clears the name in Add; null elements are skipped
        query.ToString().Should().Be("x=1&x=a&x=b");
    }
}
