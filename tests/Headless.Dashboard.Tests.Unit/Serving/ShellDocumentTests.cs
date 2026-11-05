// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Dashboard;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.TestHost;
using Tests.Serving.Fixtures;

namespace Tests.Serving;

public sealed class ShellDocumentTests : TestBase
{
    [Fact]
    public async Task should_use_a_fresh_nonce_that_the_policy_and_every_script_element_share()
    {
        await using var app = await ServingHost.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        using var first = await client.GetAsync("/headless/", AbortToken);
        using var second = await client.GetAsync("/headless/", AbortToken);

        var firstDocument = ServingHost.Parse(await first.Content.ReadAsStringAsync(AbortToken));
        var secondDocument = ServingHost.Parse(await second.Content.ReadAsStringAsync(AbortToken));
        var firstNonce = ServingHost.PolicyNonce(_Policy(first));

        firstNonce.Should().NotBeNullOrEmpty();
        firstNonce.Should().NotBe(ServingHost.PolicyNonce(_Policy(second)));
        firstDocument.ImportMapNonce.Should().Be(firstNonce);
        firstDocument.EntryNonce.Should().Be(firstNonce);
        firstDocument.Preloads.Should().HaveCount(2).And.OnlyContain(preload => preload.Nonce == firstNonce);
        secondDocument.ImportMapNonce.Should().NotBe(firstNonce);
    }

    [Fact]
    public async Task should_carry_the_nonce_on_every_script_except_the_state_block()
    {
        await using var app = await ServingHost.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/headless/", AbortToken);

        var nonce = ServingHost.PolicyNonce(_Policy(response));
        var document = ServingHost.Parse(await response.Content.ReadAsStringAsync(AbortToken));

        document.ScriptTags.Should().HaveCount(3);

        foreach (var tag in document.ScriptTags)
        {
            if (tag.Contains("type=\"application/json\"", StringComparison.Ordinal))
            {
                tag.Should().NotContain("nonce=");
            }
            else
            {
                tag.Should().Contain($"nonce=\"{nonce}\"");
            }
        }
    }

    [Fact]
    public async Task should_send_a_strict_policy_and_no_base_element()
    {
        await using var app = await ServingHost.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/headless/", AbortToken);

        var policy = _Policy(response);
        policy.Should().Contain("'strict-dynamic'");
        policy.Should().Contain("frame-ancestors 'none'");
        policy.Should().Contain("base-uri 'none'");
        policy.Should().Contain("object-src 'none'");
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().NotContainEquivalentOf("<base");
    }

    [Fact]
    public async Task should_escape_the_user_name_and_emit_one_state_block_when_the_name_holds_a_script_end_tag()
    {
        const string name = "</script><b>";
        var session = ServingHost.Session(
            name,
            ("diag", new DashboardModuleAccess { CanView = true, CanOperate = true })
        );
        await using var app = await ServingHost.StartAsync(configure: null, session, beforeRouting: null, AbortToken);
        using var client = app.GetTestClient();

        var html = await client.GetStringAsync("/headless/", AbortToken);

        var document = ServingHost.Parse(html);
        html.Should().NotContain(name);
        document.StateBlockCount.Should().Be(1);
        document.State!.Value.GetProperty("session").GetProperty("name").GetString().Should().Be(name);
    }

    [Fact]
    public async Task should_add_the_host_sources_and_keep_the_nonce_when_the_host_configures_the_policy()
    {
        await using var app = await ServingHost.StartAsync(
            dashboard =>
                dashboard.ConfigureContentSecurityPolicy(policy =>
                {
                    policy.ConnectSources.Add("https://telemetry.example.com");
                    policy.ScriptSources.Add("https://cdn.example.com");
                }),
            session: null,
            beforeRouting: null,
            AbortToken
        );
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/headless/", AbortToken);

        var policy = _Policy(response);
        policy.Should().Contain("connect-src 'self' https://telemetry.example.com");
        policy.Should().Contain("'strict-dynamic' https://cdn.example.com");
        ServingHost.PolicyNonce(policy).Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task should_not_let_any_cache_keep_the_document()
    {
        await using var app = await ServingHost.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/headless/", AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.CacheControl.MaxAge.Should().BeNull();
        response.Headers.CacheControl.Public.Should().BeFalse();
    }

    [Fact]
    public async Task should_inline_only_the_modules_the_session_may_view()
    {
        var session = ServingHost.Session(
            "viewer",
            ("diag", new DashboardModuleAccess { CanView = true, CanOperate = false })
        );
        await using var app = await ServingHost.StartAsync(configure: null, session, beforeRouting: null, AbortToken);
        using var client = app.GetTestClient();

        var document = ServingHost.Parse(await client.GetStringAsync("/headless/", AbortToken));

        var modules = document.State!.Value.GetProperty("modules").EnumerateArray().ToList();
        modules.Should().ContainSingle();
        modules[0].GetProperty("id").GetString().Should().Be("diag");
        modules[0].GetProperty("canOperate").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task should_inline_a_refusal_entry_with_no_entry_url_for_a_refused_module()
    {
        await using var app = await ServingHost.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        var document = ServingHost.Parse(await client.GetStringAsync("/headless/", AbortToken));

        var legacy = document
            .State!.Value.GetProperty("modules")
            .EnumerateArray()
            .Single(module => string.Equals(module.GetProperty("id").GetString(), "legacy", StringComparison.Ordinal));
        legacy.TryGetProperty("entry", out _).Should().BeFalse();
        legacy.GetProperty("refusal").GetProperty("builtAgainst").GetString().Should().Be("2.0.0");
        legacy.GetProperty("refusal").GetProperty("shellSdkVersion").GetString().Should().Be("1.0.0");
    }

    [Fact]
    public async Task should_inline_no_module_and_a_signed_out_session_when_the_request_has_no_session()
    {
        await using var app = await ServingHost.StartAsync(
            configure: null,
            ServingHost.SignedOut,
            beforeRouting: null,
            AbortToken
        );
        using var client = app.GetTestClient();

        var document = ServingHost.Parse(await client.GetStringAsync("/headless/sign-in", AbortToken));

        document.State!.Value.GetProperty("session").GetProperty("authenticated").GetBoolean().Should().BeFalse();
        document.State.Value.GetProperty("modules").GetArrayLength().Should().Be(0);
        document.State.Value.GetProperty("authMode").GetString().Should().Be("none");
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task should_answer_404_not_the_document_when_no_api_endpoint_matches(string method)
    {
        await using var app = await ServingHost.StartAsync(AbortToken);
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), "/headless/api/no-such-endpoint");

        using var response = await client.SendAsync(request, AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        mediaType.Should().NotBe("text/html");
    }

    [Fact]
    public void should_refuse_a_host_source_that_would_add_a_directive()
    {
        var host = new DashboardContentSecurityPolicy { ConnectSources = ["https://a.example.com; script-src *"] };

        var act = () => ContentSecurityPolicy.Build("nonce", host);

        act.Should().Throw<InvalidOperationException>().WithMessage("*connect-src*");
    }

    private static string _Policy(HttpResponseMessage response)
    {
        return response.Headers.GetValues("Content-Security-Policy").Single();
    }
}
