// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;

namespace Tests.Http;

public sealed class HeadlessHttpRequestExtensionsTests : TestBase
{
    [Fact]
    public void should_detect_ajax_request_from_the_x_requested_with_header()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.XRequestedWith = "XMLHttpRequest";

        context.Request.IsAjaxRequest().Should().BeTrue();
    }

    [Fact]
    public void should_not_detect_ajax_request_from_a_query_string_parameter()
    {
        // A link anyone can craft must not flip a branch keyed on "this was AJAX" for a top-level navigation.
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?X-Requested-With=XMLHttpRequest");

        context.Request.IsAjaxRequest().Should().BeFalse();
    }

    [Fact]
    public void should_not_detect_ajax_request_without_the_header()
    {
        new DefaultHttpContext().Request.IsAjaxRequest().Should().BeFalse();
    }
}
