// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Caching;
using Headless.Context;
using Headless.LiveQueries;
using Headless.MultiTenancy;
using Headless.Security;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Tests;

public sealed class LiveEndpointTenancyTests : TestBase
{
    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public async Task should_rerun_each_tenants_stream_under_its_own_tenant_and_user(int? maxConcurrentReruns)
    {
        // given - tenants A and B each hold a stream on the same tenant-owned rows
        var rows = new TenantRows();
        rows.Add("A", "a1");
        rows.Add("B", "b1");
        await using var app = await LiveTestApp.StartAsync(
            application =>
                application
                    .MapGet(
                        "/rows",
                        (ICurrentTenant tenant, ICurrentUser user, ILiveQueryDependencies dependencies) =>
                        {
                            dependencies.DependsOn("rows");

                            return new
                            {
                                tenant = tenant.Id,
                                user = user.Principal?.FindFirst(UserClaimTypes.UserId)?.Value,
                                rows = rows.Of(tenant.Id),
                                revision = rows.Revision,
                            };
                        }
                    )
                    .RequireAuthorization()
                    .WithLiveUpdates(),
            AbortToken,
            configureLive: options => options.MaxConcurrentReruns = maxConcurrentReruns ?? options.MaxConcurrentReruns,
            configureBuilder: builder =>
                builder.AddHeadlessTenancy(tenancy => tenancy.Http(http => http.ResolveFromClaims())),
            afterAuthentication: application => application.UseHeadlessTenancy()
        );
        app.Users.Set("alice", (UserClaimTypes.TenantId, "A"));
        app.Users.Set("bob", (UserClaimTypes.TenantId, "B"));
        using var streamOfA = await app.OpenAsync("/rows", AbortToken, user: "alice");
        using var streamOfB = await app.OpenAsync("/rows", AbortToken, user: "bob");
        await streamOfA.NextResultAsync(AbortToken);
        await streamOfB.NextResultAsync(AbortToken);

        // when - A writes
        rows.Add("A", "a2");
        await app.InvalidateAsync("rows", AbortToken);
        var resultOfA = await streamOfA.NextResultAsync(AbortToken);
        var resultOfB = await streamOfB.NextResultAsync(AbortToken);

        // then
        resultOfB.GetProperty("revision").GetInt32().Should().Be(3);
        resultOfB.GetProperty("tenant").GetString().Should().Be("B");
        resultOfB.GetProperty("user").GetString().Should().Be("bob");
        resultOfB.GetProperty("rows").EnumerateArray().Select(row => row.GetString()).Should().Equal("b1");
        resultOfA.GetProperty("tenant").GetString().Should().Be("A");
        resultOfA.GetProperty("user").GetString().Should().Be("alice");
        resultOfA.GetProperty("rows").EnumerateArray().Select(row => row.GetString()).Should().Equal("a1", "a2");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_end_the_stream_when_the_principal_no_longer_belongs_to_the_tenant_it_opened_under(
        bool requireAuthorization
    )
    {
        // given - a host that resolves the tenant from the claim, once, when the stream opens
        var value = new Shared<int>(1);
        await using var app = await LiveTestApp.StartAsync(
            application =>
            {
                var endpoint = application
                    .MapGet(
                        "/tenant-data",
                        (ICurrentTenant tenant, ILiveQueryDependencies dependencies) =>
                        {
                            dependencies.DependsOn("value");

                            return new { tenant = tenant.Id, value = value.Value };
                        }
                    )
                    .WithLiveUpdates();

                if (requireAuthorization)
                {
                    endpoint.RequireAuthorization();
                }
            },
            AbortToken,
            configureBuilder: builder =>
                builder.AddHeadlessTenancy(tenancy => tenancy.Http(http => http.ResolveFromClaims())),
            afterAuthentication: application => application.UseHeadlessTenancy()
        );
        app.Users.Set("alice", (UserClaimTypes.TenantId, "A"));
        using var stream = await app.OpenAsync("/tenant-data", AbortToken, user: "alice");
        var first = await stream.NextResultAsync(AbortToken);

        // when - the same credential now belongs to tenant B
        app.Users.Set("alice", (UserClaimTypes.TenantId, "B"));
        value.Value = 2;
        await app.InvalidateAsync("value", AbortToken);
        var failure = await stream.NextFailureAsync(AbortToken);

        // then - the stream ends instead of reading tenant A's data for a principal of tenant B
        first.GetProperty("tenant").GetString().Should().Be("A");
        failure.GetProperty("status").GetInt32().Should().Be(403);
        failure.GetProperty("error").GetProperty("code").GetString().Should().Be(LiveEndpointsErrorCodes.Unauthorized);
        (await stream.NextEventAsync(AbortToken)).Should().BeNull("a failure ends the stream");
    }

    [Fact]
    public async Task should_reject_a_rerun_whose_tenant_claim_no_longer_matches_the_tenant_the_request_named()
    {
        // given - a stream opened for the tenant its header names, by a user of that tenant
        var value = new Shared<int>(1);
        await using var app = await LiveTestApp.StartAsync(
            application =>
                application
                    .MapGet(
                        "/tenant-data",
                        (ICurrentTenant tenant, ILiveQueryDependencies dependencies) =>
                        {
                            dependencies.DependsOn("value");

                            return new { tenant = tenant.Id, value = value.Value };
                        }
                    )
                    .RequireAuthorization()
                    .WithLiveUpdates(),
            AbortToken,
            configureBuilder: builder =>
            {
                builder.Services.AddHeadlessCaching(caching => caching.UseInMemory());
                builder.AddHeadlessTenancy(tenancy =>
                {
                    tenancy.Catalog(catalog =>
                        catalog.UseInMemory(options =>
                        {
                            options.Tenants.Add(new TenantInfo("ten_acme", "acme", "Acme", isEnabled: true));
                            options.Tenants.Add(new TenantInfo("ten_globex", "globex", "Globex", isEnabled: true));
                        })
                    );
                    tenancy.Http(http =>
                    {
                        http.ResolveFromClaims();
                        http.ResolveFromCatalog(catalog =>
                            catalog.AddSource(context =>
                                context.Request.Headers.TryGetValue("X-Tenant", out var named) ? named.ToString() : null
                            )
                        );
                    });
                });
            },
            beforeAuthentication: application => application.UseHeadlessTenantCatalogResolution(),
            afterAuthentication: application => application.UseHeadlessTenancy(),
            // The catalog integration requires the status-code rewriter, which the fixed outer block brings.
            useHeadless: true
        );
        app.Users.Set("alice", (UserClaimTypes.TenantId, "ten_acme"));
        using var stream = await app.OpenAsync(
            "/tenant-data",
            AbortToken,
            user: "alice",
            configure: request => request.Headers.Add("X-Tenant", "acme")
        );
        var first = await stream.NextResultAsync(AbortToken);

        // when - the same credential now belongs to another tenant
        app.Users.Set("alice", (UserClaimTypes.TenantId, "ten_globex"));
        value.Value = 2;
        await app.InvalidateAsync("value", AbortToken);
        var failure = await stream.NextFailureAsync(AbortToken);

        // then
        first.GetProperty("tenant").GetString().Should().Be("ten_acme");
        failure.GetProperty("status").GetInt32().Should().Be(403);
        failure.GetProperty("error").GetProperty("code").GetString().Should().Be(LiveEndpointsErrorCodes.Unauthorized);
        (await stream.NextEventAsync(AbortToken)).Should().BeNull("a failure ends the stream");
    }

    private sealed class TenantRows
    {
        private readonly Lock _lock = new();
        private readonly List<(string Tenant, string Row)> _rows = [];

        public int Revision
        {
            get
            {
                lock (_lock)
                {
                    return _rows.Count;
                }
            }
        }

        public void Add(string tenant, string row)
        {
            lock (_lock)
            {
                _rows.Add((tenant, row));
            }
        }

        public string[] Of(string? tenant)
        {
            lock (_lock)
            {
                return
                [
                    .. _rows
                        .Where(row => string.Equals(row.Tenant, tenant, StringComparison.Ordinal))
                        .Select(row => row.Row),
                ];
            }
        }
    }
}
