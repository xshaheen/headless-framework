// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Dashboard;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;

namespace Tests.Modules;

public sealed class DashboardResultsTests : TestBase
{
    [Fact]
    public async Task should_write_the_html_under_the_preview_policy_when_an_html_preview_executes()
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        await DashboardResults.HtmlPreview("<p>x</p>").ExecuteAsync(context);

        var response = context.Response;
        response.ContentType.Should().Be("text/html; charset=utf-8");
        response
            .Headers.ContentSecurityPolicy.ToString()
            .Should()
            .Be(
                "sandbox; default-src 'none'; style-src 'unsafe-inline'; img-src data:; font-src data:; "
                    + "base-uri 'none'; form-action 'none'; frame-ancestors 'self'"
            );
        response.Headers.XContentTypeOptions.ToString().Should().Be("nosniff");
        response.Headers.CacheControl.ToString().Should().Be("no-store");
        response.Headers["Referrer-Policy"].ToString().Should().Be("no-referrer");
        Encoding.UTF8.GetString(((MemoryStream)response.Body).ToArray()).Should().Be("<p>x</p>");
    }

    [Fact]
    public void should_throw_when_the_html_is_null()
    {
        var create = () => DashboardResults.HtmlPreview(null!);

        create.Should().Throw<ArgumentNullException>();
    }
}
