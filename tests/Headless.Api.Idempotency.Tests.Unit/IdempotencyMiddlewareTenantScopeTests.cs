// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Api;
using Headless.Api.Idempotency;
using Headless.Constants;
using Headless.Context;
using Headless.Idempotency;
using Headless.MultiTenancy;
using Headless.Primitives;
using Headless.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// The tenant the durable store keys a record under comes from the authenticated principal's tenant claim, never
/// from the ambient tenant, which pre-auth resolution can set from a caller-controlled host or header.
/// </summary>
public sealed class IdempotencyMiddlewareTenantScopeTests : IdempotencyMiddlewareTestBase
{
    private const string _AmbientTenant = "tenant-b";
    private const string _ClaimTenant = "tenant-a";

    private readonly CurrentTenant _currentTenant = new(AsyncLocalCurrentTenantAccessor.Instance);

    [Fact]
    public async Task should_admit_under_the_claim_tenant_when_the_ambient_tenant_differs()
    {
        using var ambient = _currentTenant.Change(_AmbientTenant);
        var operations = CreateAdmittingOperations();
        var seen = _RecordStoreTenants(operations);
        var middleware = _CreateMiddleware(operations, _Authenticated("u1", _ClaimTenant));

        await middleware.InvokeAsync(CreateContext(idempotencyKey: "k1"), _ => Task.CompletedTask);

        seen.Should().Equal(_ClaimTenant);
    }

    [Fact]
    public async Task should_read_the_tenant_from_the_configured_claim_type()
    {
        using var ambient = _currentTenant.Change(_AmbientTenant);
        var operations = CreateAdmittingOperations();
        var seen = _RecordStoreTenants(operations);
        var principal = _Authenticated("u1", tenantClaim: null, new Claim("org", _ClaimTenant));
        var middleware = _CreateMiddleware(
            operations,
            principal,
            tenancyOptions: Options.Create(new MultiTenancyOptions { ClaimType = "org" })
        );

        await middleware.InvokeAsync(CreateContext(idempotencyKey: "k1"), _ => Task.CompletedTask);

        seen.Should().Equal(_ClaimTenant);
    }

    [Fact]
    public async Task should_admit_under_the_host_scope_when_the_authenticated_principal_has_no_tenant_claim()
    {
        using var ambient = _currentTenant.Change(_AmbientTenant);
        var operations = CreateAdmittingOperations();
        var seen = _RecordStoreTenants(operations);
        var middleware = _CreateMiddleware(operations, _Authenticated("u1", tenantClaim: null));

        await middleware.InvokeAsync(CreateContext(idempotencyKey: "k1"), _ => Task.CompletedTask);

        seen.Should().Equal((string?)null);
    }

    [Fact]
    public async Task should_ignore_a_tenant_claim_on_an_unauthenticated_principal()
    {
        using var ambient = _currentTenant.Change(_AmbientTenant);
        var operations = CreateAdmittingOperations();
        var seen = _RecordStoreTenants(operations);
        var user = new TestCurrentUser
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(UserClaimTypes.TenantId, "x")])),
        };
        var middleware = _CreateMiddleware(operations, user, requireUserIdentity: false);

        await middleware.InvokeAsync(CreateContext(idempotencyKey: "k1"), _ => Task.CompletedTask);

        seen.Should().Equal((string?)null);
    }

    [Fact]
    public async Task should_admit_an_anonymous_request_under_the_host_scope_whatever_the_ambient_tenant()
    {
        using var ambient = _currentTenant.Change(_AmbientTenant);
        var operations = CreateAdmittingOperations();
        var seen = _RecordStoreTenants(operations);
        var middleware = _CreateMiddleware(operations, _Anonymous(), requireUserIdentity: false);
        IIdempotencyContext? idempotency = null;

        await middleware.InvokeAsync(
            CreateContext(idempotencyKey: "k1"),
            ctx =>
            {
                idempotency = ctx.GetIdempotencyContext();
                return Task.CompletedTask;
            }
        );

        seen.Should().Equal((string?)null);
        idempotency!.Scope.Should().Be("idem::POST:/v1/test:k1");
    }

    [Fact]
    public async Task should_admit_an_anonymous_request_when_no_ambient_tenant_is_resolved()
    {
        var operations = CreateAdmittingOperations();
        var seen = _RecordStoreTenants(operations);
        var middleware = _CreateMiddleware(operations, _Anonymous(), requireUserIdentity: false);
        var nextCalled = false;

        await middleware.InvokeAsync(
            CreateContext(idempotencyKey: "k1"),
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            }
        );

        nextCalled.Should().BeTrue();
        seen.Should().Equal((string?)null);
    }

    [Fact]
    public async Task should_admit_under_the_host_scope_when_a_key_deriver_serves_an_anonymous_request()
    {
        using var ambient = _currentTenant.Change(_AmbientTenant);
        var operations = CreateAdmittingOperations();
        var seen = _RecordStoreTenants(operations);
        var options = new IdempotencyOptions { KeyDeriver = (_, key) => $"webhook:{key}" };
        var middleware = CreateMiddleware(
            options: Monitor(options),
            operations: operations,
            currentTenant: _currentTenant,
            currentUser: _Anonymous()
        );

        await middleware.InvokeAsync(CreateContext(idempotencyKey: "k1"), _ => Task.CompletedTask);

        seen.Should().Equal((string?)null);
    }

    [Fact]
    public async Task should_admit_under_the_claim_tenant_when_a_key_deriver_serves_an_authenticated_request()
    {
        using var ambient = _currentTenant.Change(_AmbientTenant);
        var operations = CreateAdmittingOperations();
        var seen = _RecordStoreTenants(operations);
        var options = new IdempotencyOptions { KeyDeriver = (_, key) => $"custom:{key}" };
        var middleware = CreateMiddleware(
            options: Monitor(options),
            operations: operations,
            currentTenant: _currentTenant,
            currentUser: _Authenticated("u1", _ClaimTenant)
        );

        await middleware.InvokeAsync(CreateContext(idempotencyKey: "k1"), _ => Task.CompletedTask);

        seen.Should().Equal(_ClaimTenant);
    }

    [Fact]
    public async Task should_keep_the_claim_tenant_across_an_asynchronous_store_admission()
    {
        using var ambient = _currentTenant.Change(_AmbientTenant);
        string? storeTenant = null;
        var operations = CreateAdmittingOperations();
        operations
            .AdmitAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyFingerprint>(),
                Arg.Any<string?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci => admitAfterYieldAsync(ci.ArgAt<string>(0), ci.ArgAt<IdempotencyFingerprint>(1)));
        var middleware = _CreateMiddleware(operations, _Authenticated("u1", _ClaimTenant));

        await middleware.InvokeAsync(CreateContext(idempotencyKey: "k1"), _ => Task.CompletedTask);

        storeTenant.Should().Be(_ClaimTenant, "a relational store reads the tenant after its first await");

        async ValueTask<IdempotentAdmission> admitAfterYieldAsync(string key, IdempotencyFingerprint fingerprint)
        {
            await Task.Yield();
            storeTenant = _currentTenant.Id;
            return Admitted(key, fingerprint);
        }
    }

    [Fact]
    public async Task should_peek_and_readmit_under_the_claim_tenant_while_waiting()
    {
        using var ambient = _currentTenant.Change(_AmbientTenant);
        var operations = Substitute.For<IIdempotentOperations>();
        AdmitReturns(
            operations,
            InFlight,
            key => Replay(key, new IdempotencyResponseSnapshot { StatusCode = 201, Body = [7] })
        );
        PeekReturns(operations, IdempotencyPeekStatus.Completed);
        var admitted = _RecordStoreTenants(operations);
        var peeked = new List<string?>();
        operations
            .When(o => o.PeekAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(_ => peeked.Add(_currentTenant.Id));
        var options = new IdempotencyOptions
        {
            InFlightStrategy = InFlightStrategy.WaitAndReplay,
            InFlightLockTimeout = TimeSpan.FromSeconds(10),
        };
        var middleware = CreateMiddleware(
            options: Monitor(options),
            operations: operations,
            currentTenant: _currentTenant,
            currentUser: _Authenticated("u1", _ClaimTenant),
            timeProvider: TimeProvider.System
        );
        var context = CreateContext(idempotencyKey: "k1");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Response.StatusCode.Should().Be(201);
        admitted.Should().Equal(_ClaimTenant, _ClaimTenant);
        peeked.Should().Equal(_ClaimTenant);
    }

    [Fact]
    public async Task should_run_the_handler_and_return_under_the_unchanged_ambient_tenant()
    {
        using var ambient = _currentTenant.Change(_AmbientTenant);
        var operations = CreateAdmittingOperations();
        var middleware = _CreateMiddleware(operations, _Authenticated("u1", _ClaimTenant));
        string? handlerTenant = null;

        await middleware.InvokeAsync(
            CreateContext(idempotencyKey: "k1"),
            _ =>
            {
                handlerTenant = _currentTenant.Id;
                return Task.CompletedTask;
            }
        );

        handlerTenant.Should().Be(_AmbientTenant);
        _currentTenant.Id.Should().Be(_AmbientTenant);
    }

    private IdempotencyMiddleware _CreateMiddleware(
        IIdempotentOperations operations,
        ICurrentUser user,
        bool requireUserIdentity = true,
        IOptions<MultiTenancyOptions>? tenancyOptions = null
    )
    {
        return CreateMiddleware(
            options: Monitor(new IdempotencyOptions { RequireUserIdentity = requireUserIdentity }),
            operations: operations,
            currentTenant: _currentTenant,
            currentUser: user,
            tenancyOptions: tenancyOptions
        );
    }

    /// <summary>Records the tenant the store would key each admission under: the ambient tenant at call time.</summary>
    private List<string?> _RecordStoreTenants(IIdempotentOperations operations)
    {
        var seen = new List<string?>();
        operations
            .When(o =>
                o.AdmitAsync(
                    Arg.Any<string>(),
                    Arg.Any<IdempotencyFingerprint>(),
                    Arg.Any<string?>(),
                    Arg.Any<TimeSpan?>(),
                    Arg.Any<TimeSpan?>(),
                    Arg.Any<CancellationToken>()
                )
            )
            .Do(_ => seen.Add(_currentTenant.Id));
        return seen;
    }

    private static TestCurrentUser _Authenticated(string userId, string? tenantClaim, params Claim[] extraClaims)
    {
        List<Claim> claims = [new(UserClaimTypes.UserId, userId), .. extraClaims];
        if (tenantClaim is not null)
        {
            claims.Add(new Claim(UserClaimTypes.TenantId, tenantClaim));
        }

        return new TestCurrentUser
        {
            IsAuthenticated = true,
            UserId = new UserId(userId),
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
        };
    }

    private static TestCurrentUser _Anonymous()
    {
        return new TestCurrentUser();
    }
}
