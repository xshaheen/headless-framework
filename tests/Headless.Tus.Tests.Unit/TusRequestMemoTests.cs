// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.Tus.Internal;
using Microsoft.AspNetCore.Http;

namespace Tests;

public sealed class TusRequestMemoTests : TestBase
{
    [Fact]
    public void should_keep_an_entry_for_the_rest_of_the_request()
    {
        // given
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var memo = new TusRequestMemo<object>(accessor);
        var entry = new object();

        // when
        memo.Set("file-1", entry);

        // then
        memo.TryGet("file-1", out var found).Should().BeTrue();
        found.Should().BeSameAs(entry);
        memo.TryGet("file-2", out _).Should().BeFalse();
    }

    [Fact]
    public void should_forget_a_removed_entry()
    {
        // given
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var memo = new TusRequestMemo<object>(accessor);
        memo.Set("file-1", new object());

        // when
        memo.Remove("file-1");

        // then
        memo.TryGet("file-1", out _).Should().BeFalse();
    }

    [Fact]
    public void should_keep_nothing_across_requests()
    {
        // given
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var memo = new TusRequestMemo<object>(accessor);
        memo.Set("file-1", new object());

        // when
        accessor.HttpContext = new DefaultHttpContext();

        // then
        memo.TryGet("file-1", out _).Should().BeFalse();
    }

    [Fact]
    public void should_keep_nothing_outside_a_request()
    {
        // given - an accessor with no current request, and no accessor at all
        var withoutRequest = new TusRequestMemo<object>(new HttpContextAccessor());
        var withoutAccessor = new TusRequestMemo<object>(httpContextAccessor: null);

        // when
        withoutRequest.Set("file-1", new object());
        withoutAccessor.Set("file-1", new object());
        withoutRequest.Remove("file-1");

        // then
        withoutRequest.TryGet("file-1", out _).Should().BeFalse();
        withoutAccessor.TryGet("file-1", out _).Should().BeFalse();
    }

    [Fact]
    public void should_keep_the_entries_of_two_memos_in_one_request_apart()
    {
        // given - two stores serving the same request
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var first = new TusRequestMemo<object>(accessor);
        var second = new TusRequestMemo<object>(accessor);

        // when
        first.Set("file-1", new object());

        // then
        second.TryGet("file-1", out _).Should().BeFalse();
    }
}
