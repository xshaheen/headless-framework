// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Headless.Testing;
using Headless.Testing.Tests;

namespace Tests.Helpers;

public sealed class TenantIsolationHttpAssertionsTests : TestBase
{
    private const string _CrossTenantUri = "/orders/owned-by-a";
    private const string _MissingUri = "/orders/does-not-exist";

    [Fact]
    public async Task should_pass_when_bodies_differ_only_in_per_request_members()
    {
        using var client = _Client(
            cross: () => _NotFound(traceId: "trace-1", timestamp: "2026-01-01T00:00:00Z", instance: _CrossTenantUri),
            missing: () => _NotFound(traceId: "trace-2", timestamp: "2026-01-01T00:00:05Z", instance: _MissingUri)
        );

        await _Assert(client);
    }

    [Fact]
    public async Task should_pass_when_plain_text_bodies_are_equal()
    {
        using var client = _Client(
            cross: () => _Response(HttpStatusCode.NotFound, "not found", "text/plain"),
            missing: () => _Response(HttpStatusCode.NotFound, "not found", "text/plain")
        );

        await _Assert(client);
    }

    [Fact]
    public async Task should_report_a_success_status_as_returning_another_tenants_resource()
    {
        using var client = _Client(
            cross: () => _Json(HttpStatusCode.OK, """{"id":"owned-by-a"}"""),
            missing: () => _NotFound()
        );

        var failure = await _Fails(client);

        failure.Should().Contain("200").And.Contain("returned another tenant's resource");
    }

    [Fact]
    public async Task should_report_a_bare_forbidden_as_an_existence_leak()
    {
        using var client = _Client(
            cross: () => _Json(HttpStatusCode.Forbidden, """{"status":403,"title":"forbidden"}"""),
            missing: () => _NotFound()
        );

        var failure = await _Fails(client);

        failure.Should().Contain("403").And.Contain("existence leak");
    }

    [Fact]
    public async Task should_not_call_the_framework_tenant_required_answer_a_leak()
    {
        using var client = _Client(
            cross: () =>
                _Json(
                    HttpStatusCode.Forbidden,
                    """{"status":403,"error":{"code":"g:tenant_required","description":"no tenant"}}"""
                ),
            missing: () => _NotFound()
        );

        var failure = await _Fails(client);

        failure.Should().Contain("g:tenant_required").And.Contain("not a cross-tenant leak");
        failure.Should().NotContain("existence leak");
    }

    [Fact]
    public async Task should_report_the_framework_cross_tenant_write_refusal_from_an_errors_array()
    {
        using var client = _Client(
            cross: () =>
                _Json(
                    HttpStatusCode.Conflict,
                    """{"status":409,"errors":[{"code":"g:cross_tenant_write","description":"refused"}]}"""
                ),
            missing: () => _NotFound()
        );

        var failure = await _Fails(client);

        failure.Should().Contain("g:cross_tenant_write").And.Contain("write guard");
    }

    [Fact]
    public async Task should_diagnose_a_client_without_tenant_context_at_the_control()
    {
        const string body = """{"status":403,"error":{"code":"g:tenant_required","description":"no tenant"}}""";
        using var client = _Client(
            cross: () => _Json(HttpStatusCode.Forbidden, body),
            missing: () => _Json(HttpStatusCode.Forbidden, body)
        );

        var failure = await _Fails(client);

        failure.Should().Contain(_MissingUri).And.Contain("g:tenant_required").And.Contain("not a cross-tenant leak");
        failure.Should().NotContain("proves nothing");
    }

    [Fact]
    public async Task should_fail_when_tenant_resolution_rejects_both_requests()
    {
        // Identical 404s from tenant resolution would otherwise pass without the endpoint ever running.
        const string body =
            """{"status":404,"title":"not-found","error":{"code":"g:tenant_resolution_failed","description":"x"}}""";
        using var client = _Client(
            cross: () => _Json(HttpStatusCode.NotFound, body),
            missing: () => _Json(HttpStatusCode.NotFound, body)
        );

        var failure = await _Fails(client);

        failure.Should().Contain("g:tenant_resolution_failed").And.Contain("Seed the probing tenant");
    }

    [Fact]
    public async Task should_pass_when_details_differ_only_by_the_quoted_request_path()
    {
        using var client = _Client(
            cross: () => _NotFound(detail: $"The requested endpoint '{_CrossTenantUri}' was not found."),
            missing: () => _NotFound(detail: $"The requested endpoint '{_MissingUri}' was not found.")
        );

        await _Assert(client);
    }

    [Fact]
    public async Task should_report_an_unexpected_status()
    {
        using var client = _Client(cross: () => _Json(HttpStatusCode.BadRequest, "{}"), missing: () => _NotFound());

        var failure = await _Fails(client);

        failure.Should().Contain("400").And.Contain("404");
    }

    [Fact]
    public async Task should_show_both_bodies_when_the_not_found_bodies_differ()
    {
        using var client = _Client(
            cross: () => _NotFound(detail: "order owned-by-a is private"),
            missing: () => _NotFound()
        );

        var failure = await _Fails(client);

        failure.Should().Contain("order owned-by-a is private").And.Contain("tell another tenant's id");
    }

    [Fact]
    public async Task should_fail_when_the_content_types_differ()
    {
        using var client = _Client(
            cross: () => _Response(HttpStatusCode.NotFound, "not found", "text/plain"),
            missing: () => _Response(HttpStatusCode.NotFound, "not found", "text/html")
        );

        var failure = await _Fails(client);

        failure.Should().Contain("text/plain").And.Contain("text/html");
    }

    [Fact]
    public async Task should_fail_when_the_missing_id_control_does_not_answer_not_found()
    {
        using var client = _Client(cross: () => _NotFound(), missing: () => _Json(HttpStatusCode.OK, "{}"));

        var failure = await _Fails(client);

        failure.Should().Contain(_MissingUri).And.Contain("proves nothing");
    }

    [Fact]
    public async Task should_ignore_extra_members_the_caller_names()
    {
        using var client = _Client(
            cross: () => _Json(HttpStatusCode.NotFound, """{"status":404,"correlationId":"a"}"""),
            missing: () => _Json(HttpStatusCode.NotFound, """{"status":404,"correlationId":"b"}""")
        );

        await TenantIsolationHttpAssertions.ShouldAnswerNotFoundAcrossTenantsAsync(
            client,
            _CrossTenantUri,
            _MissingUri,
            ignoredMembers: ["correlationId"],
            cancellationToken: AbortToken
        );
    }

    [Fact]
    public async Task should_send_the_requests_the_factories_build()
    {
        using var handler = new _StubHandler(() => _NotFound(), () => _NotFound());
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };

        await TenantIsolationHttpAssertions.ShouldAnswerNotFoundAcrossTenantsAsync(
            client,
            () => new HttpRequestMessage(HttpMethod.Delete, _CrossTenantUri),
            () => new HttpRequestMessage(HttpMethod.Delete, _MissingUri),
            cancellationToken: AbortToken
        );

        handler.Methods.Should().Equal(HttpMethod.Delete, HttpMethod.Delete);
    }

    private Task _Assert(HttpClient client) =>
        TenantIsolationHttpAssertions.ShouldAnswerNotFoundAcrossTenantsAsync(
            client,
            _CrossTenantUri,
            _MissingUri,
            cancellationToken: AbortToken
        );

    private async Task<string> _Fails(HttpClient client)
    {
        var act = () => _Assert(client);

        var failure = await act.Should().ThrowAsync<Exception>();

        return failure.Which.Message;
    }

    private static HttpClient _Client(Func<HttpResponseMessage> cross, Func<HttpResponseMessage> missing)
    {
#pragma warning disable CA2000 // False positive: the returned client owns and disposes the handler.
        return new HttpClient(new _StubHandler(cross, missing), disposeHandler: true)
        {
            BaseAddress = new Uri("http://localhost"),
        };
#pragma warning restore CA2000
    }

    private static HttpResponseMessage _NotFound(
        string traceId = "trace",
        string timestamp = "2026-01-01T00:00:00Z",
        string instance = "/",
        string detail = "The requested resource was not found."
    ) =>
        _Json(
            HttpStatusCode.NotFound,
            $$"""{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"not-found","status":404,"detail":"{{detail}}","traceId":"{{traceId}}","timestamp":"{{timestamp}}","instance":"{{instance}}"}"""
        );

    private static HttpResponseMessage _Json(HttpStatusCode status, string body) =>
        _Response(status, body, "application/problem+json");

    private static HttpResponseMessage _Response(HttpStatusCode status, string body, string mediaType)
    {
        var content = new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);

        return new HttpResponseMessage(status) { Content = content };
    }

    // Builds each response on send, so the assertion under test owns and disposes every one it receives.
    private sealed class _StubHandler(Func<HttpResponseMessage> cross, Func<HttpResponseMessage> missing)
        : HttpMessageHandler
    {
        public List<HttpMethod> Methods { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Methods.Add(request.Method);
            var path = request.RequestUri!.AbsolutePath;

            return Task.FromResult(
                string.Equals(path, _CrossTenantUri, StringComparison.Ordinal) ? cross() : missing()
            );
        }
    }
}
