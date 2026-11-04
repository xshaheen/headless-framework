// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;

namespace Tests.Dashboard;

public sealed class JobsDashboardShellTests : TestBase
{
    private const string _Shell = "<html><head><title>Jobs</title></head><body></body></html>";

    [Fact]
    public void should_encode_a_path_base_that_carries_markup_into_the_shell()
    {
        // A proxy that forwards an untrusted X-Forwarded-Prefix sets PathBase to whatever the client sent.
        var context = new DefaultHttpContext();
        context.Request.PathBase = new PathString("/x\"><script>alert(1)</script>");

        var html = JobsDashboardServiceCollectionExtensions.ReplaceBasePath(
            _Shell,
            context,
            "/jobs",
            new DashboardOptionsBuilder().WithNoAuth()
        );

        html.Should().NotContain("<script>alert");
        html.Should().NotContain("\"><");
        html.Should().Contain("<base href=\"/x&quot;&gt;&lt;script&gt;alert(1)&lt;/script&gt;/jobs/\" />");
    }

    [Fact]
    public void should_write_an_ordinary_path_base_unchanged()
    {
        var context = new DefaultHttpContext();
        context.Request.PathBase = new PathString("/tenant-a");

        var html = JobsDashboardServiceCollectionExtensions.ReplaceBasePath(
            _Shell,
            context,
            "/jobs",
            new DashboardOptionsBuilder().WithNoAuth()
        );

        html.Should().Contain("<base href=\"/tenant-a/jobs/\" />");
        html.Should().Contain("\"basePath\":\"/tenant-a/jobs\"");
    }

    [Theory]
    [InlineData("oidc", true)]
    [InlineData(null, false)]
    public void should_inject_whether_sign_in_is_available_and_no_session_timeout(
        string? challengeScheme,
        bool signInAvailable
    )
    {
        var html = JobsDashboardServiceCollectionExtensions.ReplaceBasePath(
            _Shell,
            new DefaultHttpContext(),
            "/jobs",
            new DashboardOptionsBuilder().WithHostAuthentication(host => host.ChallengeScheme = challengeScheme)
        );

        var auth = _InjectedConfig(html).GetProperty("auth");
        auth.GetProperty("mode").GetString().Should().Be("host");
        auth.GetProperty("enabled").GetBoolean().Should().BeTrue();
        auth.GetProperty("signInAvailable").GetBoolean().Should().Be(signInAvailable);
        auth.TryGetProperty("sessionTimeout", out _).Should().BeFalse();
    }

    [Fact]
    public void should_never_offer_sign_in_outside_host_auth()
    {
        var html = JobsDashboardServiceCollectionExtensions.ReplaceBasePath(
            _Shell,
            new DefaultHttpContext(),
            "/jobs",
            new DashboardOptionsBuilder().WithBasicAuth("ops", "secret")
        );

        var auth = _InjectedConfig(html).GetProperty("auth");
        auth.GetProperty("mode").GetString().Should().Be("basic");
        auth.GetProperty("signInAvailable").GetBoolean().Should().BeFalse();
    }

    private static JsonElement _InjectedConfig(string html)
    {
        const string marker = "window.JobsConfig = ";
        var start = html.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = html.IndexOf("};", start, StringComparison.Ordinal) + 1;
        using var document = JsonDocument.Parse(html[start..end]);
        return document.RootElement.Clone();
    }
}
