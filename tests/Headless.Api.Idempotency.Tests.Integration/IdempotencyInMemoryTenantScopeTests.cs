// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Constants;
using Headless.Idempotency;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CA2025 // False positive: _PostAsync awaits SendAsync before the request is disposed.

namespace Tests;

/// <summary>
/// End to end on the in-memory store: the ambient tenant a caller steers through a header never decides which tenant
/// namespace an idempotency record lands in. Authenticated records follow the tenant claim; anonymous records share one
/// namespace outside every tenant.
/// </summary>
public sealed class IdempotencyInMemoryTenantScopeTests : TestBase
{
    private const string _TenantHeader = "X-Tenant";

    [Fact]
    public async Task should_not_let_an_anonymous_request_routed_to_a_tenant_pre_seed_that_tenants_user_key()
    {
        var key = _UniqueKey();
        await using var app = await _CreateAppAsync();
        using var client = IdempotencyTestApp.CreateClient(app);
        var user = app.Services.GetRequiredService<IdempotencyTestApp.TestCurrentUserState>();

        user.SetAnonymous();
        var anonymous = await _PostAsync(client, key, tenant: "tenant-a");
        user.SetAuthenticated(tenantClaim: "tenant-a");
        var authenticated = await _PostAsync(client, key, tenant: "tenant-a");

        anonymous.StatusCode.Should().Be(HttpStatusCode.Created);
        authenticated.StatusCode.Should().Be(HttpStatusCode.Created);
        _IsReplay(authenticated).Should().BeFalse("the anonymous record lives outside the tenant's namespace");
        (await _BodyAsync(authenticated)).Should().NotBe(await _BodyAsync(anonymous));
    }

    [Fact]
    public async Task should_not_let_an_anonymous_request_routed_to_another_tenant_collide_with_a_tenant_user_key()
    {
        var key = _UniqueKey();
        await using var app = await _CreateAppAsync();
        using var client = IdempotencyTestApp.CreateClient(app);
        var user = app.Services.GetRequiredService<IdempotencyTestApp.TestCurrentUserState>();

        user.SetAnonymous();
        var anonymous = await _PostAsync(client, key, tenant: "tenant-b");
        user.SetAuthenticated(tenantClaim: "tenant-a");
        var authenticated = await _PostAsync(client, key, tenant: "tenant-a");

        anonymous.StatusCode.Should().Be(HttpStatusCode.Created);
        authenticated.StatusCode.Should().Be(HttpStatusCode.Created);
        _IsReplay(authenticated).Should().BeFalse();
    }

    [Fact]
    public async Task should_not_replay_a_tenant_users_response_to_an_anonymous_request_routed_to_that_tenant()
    {
        var key = _UniqueKey();
        await using var app = await _CreateAppAsync();
        using var client = IdempotencyTestApp.CreateClient(app);
        var user = app.Services.GetRequiredService<IdempotencyTestApp.TestCurrentUserState>();

        user.SetAuthenticated(tenantClaim: "tenant-a");
        var authenticated = await _PostAsync(client, key, tenant: "tenant-a");
        user.SetAnonymous();
        var anonymous = await _PostAsync(client, key, tenant: "tenant-a");

        authenticated.StatusCode.Should().Be(HttpStatusCode.Created);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Created);
        _IsReplay(anonymous).Should().BeFalse("an anonymous caller cannot read the tenant's record");
        (await _BodyAsync(anonymous)).Should().NotBe(await _BodyAsync(authenticated));
    }

    [Fact]
    public async Task should_keep_an_authenticated_record_under_the_claim_tenant_whatever_the_header_says()
    {
        var key = _UniqueKey();
        await using var app = await _CreateAppAsync();
        using var client = IdempotencyTestApp.CreateClient(app);
        var user = app.Services.GetRequiredService<IdempotencyTestApp.TestCurrentUserState>();
        user.SetAuthenticated(tenantClaim: "tenant-a");

        var first = await _PostAsync(client, key, tenant: "tenant-b");
        var retry = await _PostAsync(client, key, tenant: "tenant-a");

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        retry.StatusCode.Should().Be(HttpStatusCode.Created);
        _IsReplay(retry)
            .Should()
            .BeTrue("both requests key under the claim tenant, so the header did not move the record");
        (await _BodyAsync(retry)).Should().Be(await _BodyAsync(first));
    }

    [Fact]
    public async Task should_keep_records_of_different_claim_tenants_apart_for_the_same_user_and_key()
    {
        var key = _UniqueKey();
        await using var app = await _CreateAppAsync();
        using var client = IdempotencyTestApp.CreateClient(app);
        var user = app.Services.GetRequiredService<IdempotencyTestApp.TestCurrentUserState>();

        user.SetAuthenticated(tenantClaim: "tenant-a");
        var first = await _PostAsync(client, key, tenant: "tenant-a");
        user.SetAuthenticated(tenantClaim: "tenant-b");
        var second = await _PostAsync(client, key, tenant: "tenant-a");

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        _IsReplay(second).Should().BeFalse("the claim tenant, not the shared user id or header, partitions the record");
        (await _BodyAsync(second)).Should().NotBe(await _BodyAsync(first));
    }

    [Fact]
    public async Task should_share_one_namespace_for_anonymous_requests_routed_to_different_tenants()
    {
        var key = _UniqueKey();
        await using var app = await _CreateAppAsync();
        using var client = IdempotencyTestApp.CreateClient(app);
        app.Services.GetRequiredService<IdempotencyTestApp.TestCurrentUserState>().SetAnonymous();

        var first = await _PostAsync(client, key, tenant: "tenant-a");
        var second = await _PostAsync(client, key, tenant: "tenant-b");

        _IsReplay(second).Should().BeTrue("anonymous traffic shares one namespace whatever tenant it is routed to");
        (await _BodyAsync(second)).Should().Be(await _BodyAsync(first));
    }

    private static Task<WebApplication> _CreateAppAsync()
    {
        return IdempotencyTestApp.CreateAsync(
            static services =>
                services.AddHeadlessIdempotency(static setup =>
                {
                    setup.UseInMemory();
                    setup.ConfigureOptions(static options => options.PurgeInterval = null);
                }),
            static options => options.RequireUserIdentity = false,
            tenantHeaderName: _TenantHeader
        );
    }

    private static string _UniqueKey()
    {
        return $"k-{Guid.NewGuid():N}";
    }

    private static bool _IsReplay(HttpResponseMessage response)
    {
        return response.Headers.Contains(HttpHeaderNames.IdempotentReplayed);
    }

    private async Task<string> _BodyAsync(HttpResponseMessage response)
    {
        return await response.Content.ReadAsStringAsync(AbortToken);
    }

    private async Task<HttpResponseMessage> _PostAsync(HttpClient client, string key, string tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/echo");
        request.Content = new StringContent("hello");
        request.Headers.Add(HttpHeaderNames.IdempotencyKey, key);
        request.Headers.Add(_TenantHeader, tenant);

        return await client.SendAsync(request, cancellationToken: AbortToken);
    }
}
