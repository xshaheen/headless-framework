// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless.Abstractions;
using Headless.Api.Idempotency;
using Headless.Constants;
using Headless.Fencing;
using Headless.Idempotency;
using Headless.MultiTenancy;
using Headless.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using IdempotencyMiddleware = Headless.Api.Idempotency.IdempotencyMiddleware;

namespace Tests;

public sealed class IdempotencyMiddlewareTests : IdempotencyMiddlewareTestBase
{
    // ── pass-through ──────────────────────────────────────────────────────────

    [Fact]
    public async Task should_pass_through_without_store_call_when_idempotency_key_header_is_missing()
    {
        var operations = CreateAdmittingOperations();
        var middleware = CreateMiddleware(operations: operations);
        var context = CreateContext();
        var nextCalled = false;

        await middleware.InvokeAsync(context, _ => _Run(() => nextCalled = true));

        nextCalled.Should().BeTrue();
        operations.ReceivedCalls().Should().BeEmpty();
        context.GetIdempotencyContext().Should().BeNull();
    }

    [Fact]
    public async Task should_pass_through_without_store_call_when_method_is_not_in_methods_set()
    {
        var operations = CreateAdmittingOperations();
        var middleware = CreateMiddleware(operations: operations);
        var context = CreateContext(idempotencyKey: "k1", method: "GET");
        var nextCalled = false;

        await middleware.InvokeAsync(context, _ => _Run(() => nextCalled = true));

        nextCalled.Should().BeTrue();
        operations.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task should_pass_through_without_store_call_when_key_is_whitespace_or_empty(string key)
    {
        var operations = CreateAdmittingOperations();
        var middleware = CreateMiddleware(operations: operations);
        var context = CreateContext(idempotencyKey: key);
        var nextCalled = false;

        await middleware.InvokeAsync(context, _ => _Run(() => nextCalled = true));

        nextCalled.Should().BeTrue();
        operations.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_pass_through_without_store_call_when_tenant_and_user_are_null()
    {
        var operations = CreateAdmittingOperations();
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns((string?)null);
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns((UserId?)null);
        var middleware = CreateMiddleware(operations: operations, currentTenant: tenant, currentUser: user);
        var nextCalled = false;

        await middleware.InvokeAsync(CreateContext(idempotencyKey: "k1"), _ => _Run(() => nextCalled = true));

        nextCalled.Should().BeTrue();
        operations.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_pass_through_when_only_tenant_is_present_and_require_user_identity_is_true()
    {
        var operations = CreateAdmittingOperations();
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns((UserId?)null);
        var middleware = CreateMiddleware(operations: operations, currentUser: user);
        var nextCalled = false;

        await middleware.InvokeAsync(CreateContext(idempotencyKey: "k1"), _ => _Run(() => nextCalled = true));

        nextCalled.Should().BeTrue();
        operations.ReceivedCalls().Should().BeEmpty();
    }

    // ── key and admission arguments ──────────────────────────────────────────

    [Fact]
    public async Task should_admit_sha256_hex_of_user_method_path_query_and_header_without_tenant()
    {
        var operations = CreateAdmittingOperations();
        var middleware = CreateMiddleware(operations: operations);
        var context = CreateContext(idempotencyKey: "k1", method: "post");
        context.Request.QueryString = new QueryString("?mode=a");
        IIdempotencyContext? seen = null;

        await middleware.InvokeAsync(context, ctx => _Run(() => seen = ctx.GetIdempotencyContext()));

        const string scope = "idem:u1:POST:/v1/test?mode=a:k1";
        var expectedKey = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(scope)));
        AdmittedKeys(operations).Should().Equal(expectedKey);
        seen.Should().NotBeNull();
        seen!.Scope.Should().Be(scope);
        seen.HeaderKey.Should().Be("k1");
        seen.Key.Should().Be(expectedKey);
    }

    [Fact]
    public async Task should_use_empty_user_segment_when_only_tenant_is_present_and_require_user_identity_is_false()
    {
        var operations = CreateAdmittingOperations();
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns((UserId?)null);
        var middleware = CreateMiddleware(
            options: Monitor(new IdempotencyOptions { RequireUserIdentity = false }),
            operations: operations,
            currentUser: user
        );
        IIdempotencyContext? seen = null;

        await middleware.InvokeAsync(
            CreateContext(idempotencyKey: "k1"),
            ctx => _Run(() => seen = ctx.GetIdempotencyContext())
        );

        seen!.Scope.Should().Be("idem::POST:/v1/test:k1");
    }

    [Fact]
    public async Task should_keep_store_key_at_64_characters_when_header_is_255_characters_on_a_500_character_path()
    {
        var operations = CreateAdmittingOperations();
        var middleware = CreateMiddleware(operations: operations);
        var header = new string('h', 255);
        var path = "/" + new string('p', 499);

        await middleware.InvokeAsync(CreateContext(idempotencyKey: header, path: path), _ => Task.CompletedTask);

        var key = AdmittedKeys(operations).Should().ContainSingle().Which;
        key.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public async Task should_admit_with_contract_lease_retention_and_body_fingerprint()
    {
        var operations = CreateAdmittingOperations();
        var options = new IdempotencyOptions
        {
            InFlightLease = TimeSpan.FromSeconds(42),
            Retention = TimeSpan.FromHours(3),
        };
        var middleware = CreateMiddleware(options: Monitor(options), operations: operations);
        byte[] body = [1, 2, 3];

        await middleware.InvokeAsync(CreateContext(idempotencyKey: "k1", body: body), _ => Task.CompletedTask);

        await operations
            .Received(1)
            .AdmitAsync(
                Arg.Any<string>(),
                Arg.Is(FingerprintOf(body)),
                Arg.Is<string?>(IdempotencyResponseSnapshot.Contract),
                Arg.Is<TimeSpan?>(TimeSpan.FromSeconds(42)),
                Arg.Is<TimeSpan?>(TimeSpan.FromHours(3)),
                Arg.Any<CancellationToken>()
            );
    }

    // ── admitted → execute + complete ────────────────────────────────────────

    [Fact]
    public async Task should_set_idempotency_context_before_handler_and_complete_with_captured_response()
    {
        var operations = CreateAdmittingOperations();
        var middleware = CreateMiddleware(operations: operations);
        var context = CreateContext(idempotencyKey: "k1", body: [1, 2, 3]);
        IIdempotencyContext? seen = null;

        await middleware.InvokeAsync(
            context,
            async ctx =>
            {
                seen = ctx.GetIdempotencyContext();
                ctx.Response.StatusCode = StatusCodes.Status201Created;
                ctx.Response.Headers.ContentType = "application/json";
                ctx.Response.Headers.Append("Set-Cookie", "session=abc");
                await ctx.Response.WriteAsync("{\"id\":1}", AbortToken);
            }
        );

        seen.Should().NotBeNull();
        seen!.IsTakeover.Should().BeFalse();
        seen.Lease.Kind.Should().Be(IdempotentAdmission.LeaseKind);
        seen.Lease.Resource.Should().Be(seen.Key);
        seen.Admission.IsAdmitted.Should().BeTrue();

        var complete = operations
            .ReceivedCalls()
            .Should()
            .ContainSingle(c => c.GetMethodInfo().Name == nameof(IIdempotentOperations.CompleteAsync))
            .Which.GetArguments();
        complete[0].Should().BeSameAs(seen.Admission);
        complete[2].Should().Be(IdempotencyResponseSnapshot.Contract);
        complete[3].Should().BeNull("the admission's retention applies");
        complete[4].Should().Be(CancellationToken.None, "a client disconnect must not strand the admission");
        CallCount(operations, nameof(IIdempotentOperations.ReleaseAsync)).Should().Be(0);

        var stored = (ReadOnlyMemory<byte>)complete[1]!;
        var snapshot = new IdempotentResult(stored.Span, IdempotencyResponseSnapshot.Contract).Deserialize(
            IdempotencyJsonContext.Default.IdempotencyResponseSnapshot
        )!;
        snapshot.StatusCode.Should().Be(201);
        snapshot.Headers.Should().ContainKey("Content-Type").And.NotContainKey("Set-Cookie");
        Encoding.UTF8.GetString(snapshot.Body).Should().Be("{\"id\":1}");
    }

    [Fact]
    public async Task should_expose_takeover_flag_to_the_handler()
    {
        var operations = CreateAdmittingOperations(isTakeover: true);
        var middleware = CreateMiddleware(operations: operations);
        bool? takeover = null;

        await middleware.InvokeAsync(
            CreateContext(idempotencyKey: "k1"),
            ctx => _Run(() => takeover = ctx.GetIdempotencyContext()!.IsTakeover)
        );

        takeover.Should().BeTrue();
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(401)]
    public async Task should_release_instead_of_complete_when_default_predicate_rejects_status(int status)
    {
        var operations = CreateAdmittingOperations();
        var middleware = CreateMiddleware(operations: operations);

        await middleware.InvokeAsync(
            CreateContext(idempotencyKey: "k1"),
            ctx => _Run(() => ctx.Response.StatusCode = status)
        );

        await operations.Received(1).ReleaseAsync(Arg.Any<IdempotentAdmission>(), Arg.Is(CancellationToken.None));
        await operations
            .DidNotReceive()
            .CompleteAsync(
                Arg.Any<IdempotentAdmission>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_complete_422_response_using_default_predicate()
    {
        var operations = CreateAdmittingOperations();
        var middleware = CreateMiddleware(operations: operations);

        await middleware.InvokeAsync(
            CreateContext(idempotencyKey: "k1"),
            ctx => _Run(() => ctx.Response.StatusCode = 422)
        );

        CallCount(operations, nameof(IIdempotentOperations.CompleteAsync)).Should().Be(1);
    }

    [Fact]
    public async Task should_release_when_consumer_predicate_rejects_a_2xx_response()
    {
        var operations = CreateAdmittingOperations();
        var middleware = CreateMiddleware(
            options: Monitor(new IdempotencyOptions { ShouldCacheResponse = _ => false }),
            operations: operations
        );

        await middleware.InvokeAsync(
            CreateContext(idempotencyKey: "k1"),
            ctx => _Run(() => ctx.Response.StatusCode = 200)
        );

        CallCount(operations, nameof(IIdempotentOperations.ReleaseAsync)).Should().Be(1);
        CallCount(operations, nameof(IIdempotentOperations.CompleteAsync)).Should().Be(0);
    }

    [Fact]
    public async Task should_release_when_response_body_exceeds_capture_cap()
    {
        var operations = CreateAdmittingOperations();
        var middleware = CreateMiddleware(
            options: Monitor(new IdempotencyOptions { MaxBodySizeForHashing = 8 }),
            operations: operations
        );

        await middleware.InvokeAsync(
            CreateContext(idempotencyKey: "k1"),
            async ctx =>
            {
                ctx.Response.StatusCode = 200;
                await ctx.Response.WriteAsync(new string('x', 64), AbortToken);
            }
        );

        CallCount(operations, nameof(IIdempotentOperations.ReleaseAsync)).Should().Be(1);
        CallCount(operations, nameof(IIdempotentOperations.CompleteAsync)).Should().Be(0);
    }

    [Fact]
    public async Task should_release_and_rethrow_when_handler_throws()
    {
        var operations = CreateAdmittingOperations();
        operations
            .ReleaseAsync(Arg.Any<IdempotentAdmission>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<LeaseSettlementStatus>>(_ => throw new InvalidOperationException("store down"));
        var middleware = CreateMiddleware(operations: operations);
        var context = CreateContext(idempotencyKey: "k1");
        var originalBody = context.Response.Body;

        var act = () => middleware.InvokeAsync(context, _ => throw new TimeoutException("handler"));

        // The handler's exception propagates, not the release failure.
        await act.Should().ThrowAsync<TimeoutException>();
        CallCount(operations, nameof(IIdempotentOperations.ReleaseAsync)).Should().Be(1);
        context.Response.Body.Should().BeSameAs(originalBody);
    }

    [Theory]
    [InlineData(OnStoreErrorBehavior.Throw)]
    [InlineData(OnStoreErrorBehavior.FailOpen)]
    public async Task should_log_and_swallow_stale_lease_on_completion_whatever_on_store_error_says(
        OnStoreErrorBehavior behavior
    )
    {
        var operations = CreateAdmittingOperations();
        _CompleteThrows(
            operations,
            new StaleLeaseException(new FencedLease(TestTenant, "k", "r", 1), LeaseFenceStatus.Stale)
        );
        var middleware = CreateMiddleware(
            options: Monitor(new IdempotencyOptions { OnStoreError = behavior }),
            operations: operations
        );

        var act = () =>
            middleware.InvokeAsync(
                CreateContext(idempotencyKey: "k1"),
                ctx => _Run(() => ctx.Response.StatusCode = 201)
            );

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_throw_completion_failure_when_on_store_error_is_throw_and_response_not_started()
    {
        var operations = CreateAdmittingOperations();
        _CompleteThrows(operations, new InvalidOperationException("store down"));
        var middleware = CreateMiddleware(operations: operations);

        var act = () =>
            middleware.InvokeAsync(
                CreateContext(idempotencyKey: "k1"),
                ctx => _Run(() => ctx.Response.StatusCode = 201)
            );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("store down");
    }

    [Fact]
    public async Task should_log_completion_failure_when_on_store_error_is_fail_open()
    {
        var operations = CreateAdmittingOperations();
        _CompleteThrows(operations, new InvalidOperationException("store down"));
        var middleware = CreateMiddleware(
            options: Monitor(new IdempotencyOptions { OnStoreError = OnStoreErrorBehavior.FailOpen }),
            operations: operations
        );

        var act = () =>
            middleware.InvokeAsync(
                CreateContext(idempotencyKey: "k1"),
                ctx => _Run(() => ctx.Response.StatusCode = 201)
            );

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_log_and_swallow_completion_failure_after_response_started_even_when_on_store_error_is_throw()
    {
        var operations = CreateAdmittingOperations();
        _CompleteThrows(operations, new InvalidOperationException("store down"));
        var middleware = CreateMiddleware(operations: operations);
        var context = CreateContext(idempotencyKey: "k1");
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        var act = () => middleware.InvokeAsync(context, ctx => _Run(() => ctx.Response.StatusCode = 201));

        await act.Should().NotThrowAsync();
    }

    // ── replay ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task should_replay_stored_response_without_calling_next()
    {
        var operations = Substitute.For<IIdempotentOperations>();
        AdmitReturns(
            operations,
            key =>
                Replay(
                    key,
                    new IdempotencyResponseSnapshot
                    {
                        StatusCode = 201,
                        Headers = new(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Content-Type"] = ["application/json"],
                            ["Set-Cookie"] = ["leaked=1"],
                        },
                        Body = [10, 20, 30],
                    }
                )
        );
        var middleware = CreateMiddleware(operations: operations);
        var context = CreateContext(idempotencyKey: "k1", body: [1, 2, 3]);
        context.Response.Headers.ContentType = "text/plain"; // set by upstream middleware; must not leak
        var nextCalled = false;

        await middleware.InvokeAsync(context, _ => _Run(() => nextCalled = true));

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(201);
        context.Response.Headers[HttpHeaderNames.IdempotentReplayed].ToString().Should().Be("true");
        context.Response.Headers.ContentType.ToString().Should().Be("application/json");
        context.Response.Headers.ContainsKey("Set-Cookie").Should().BeFalse("replay filters through the allowlist");
        context.Response.ContentLength.Should().Be(3);
        context.Response.Body.Position = 0;
        ((MemoryStream)context.Response.Body).ToArray().Should().Equal(10, 20, 30);
        context.GetIdempotencyContext().Should().BeNull();
    }

    // ── conflict ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task should_return_422_key_reused_when_fingerprint_conflicts()
    {
        var operations = Substitute.For<IIdempotentOperations>();
        AdmitReturns(operations, FingerprintConflict);
        var middleware = CreateMiddleware(operations: operations);
        var context = CreateContext(idempotencyKey: "k1");
        var nextCalled = false;

        await middleware.InvokeAsync(context, _ => _Run(() => nextCalled = true));

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(422);
        (await ReadResponseAsync(context)).Should().Contain(IdempotencyErrorCodes.KeyReused);
    }

    [Fact]
    public async Task should_return_409_key_reused_when_mismatch_status_code_is_409()
    {
        var operations = Substitute.For<IIdempotentOperations>();
        AdmitReturns(operations, FingerprintConflict);
        var middleware = CreateMiddleware(
            options: Monitor(new IdempotencyOptions { MismatchStatusCode = 409 }),
            operations: operations
        );
        var context = CreateContext(idempotencyKey: "k1");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Response.StatusCode.Should().Be(409);
        (await ReadResponseAsync(context)).Should().Contain(IdempotencyErrorCodes.KeyReused);
    }

    [Fact]
    public async Task should_report_contract_conflict_as_key_reused_without_running_handler()
    {
        var operations = Substitute.For<IIdempotentOperations>();
        AdmitReturns(
            operations,
            key =>
                IdempotentAdmission.ContractConflict(
                    new IdempotencyKey(TestTenant, key),
                    IdempotencyFingerprint.Compute("x"),
                    "headless.api.idempotency.response/v0"
                )
        );
        var middleware = CreateMiddleware(operations: operations);
        var context = CreateContext(idempotencyKey: "k1");
        var nextCalled = false;

        await middleware.InvokeAsync(context, _ => _Run(() => nextCalled = true));

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(422);
    }

    // ── in flight ────────────────────────────────────────────────────────────

    [Fact]
    public async Task should_return_409_in_flight_when_strategy_is_reject()
    {
        var operations = Substitute.For<IIdempotentOperations>();
        AdmitReturns(operations, InFlight);
        var middleware = CreateMiddleware(operations: operations);
        var context = CreateContext(idempotencyKey: "k1");
        var nextCalled = false;

        await middleware.InvokeAsync(context, _ => _Run(() => nextCalled = true));

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(409);
        var body = await ReadResponseAsync(context);
        body.Should().Contain(IdempotencyErrorCodes.InFlight).And.NotContain(IdempotencyErrorCodes.InFlightTimeout);
        AdmittedKeys(operations).Should().ContainSingle("Reject never polls");
    }

    [Fact]
    public async Task should_poll_and_replay_when_wait_and_replay_and_running_attempt_completes()
    {
        var operations = Substitute.For<IIdempotentOperations>();
        AdmitReturns(
            operations,
            InFlight,
            InFlight,
            key => Replay(key, new IdempotencyResponseSnapshot { StatusCode = 201, Body = [7] })
        );
        var middleware = CreateMiddleware(
            options: Monitor(_WaitAndReplay(TimeSpan.FromSeconds(10))),
            operations: operations,
            timeProvider: TimeProvider.System
        );
        var context = CreateContext(idempotencyKey: "k1");
        var nextCalled = false;

        await middleware.InvokeAsync(context, _ => _Run(() => nextCalled = true));

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(201);
        context.Response.Headers[HttpHeaderNames.IdempotentReplayed].ToString().Should().Be("true");
        AdmittedKeys(operations).Should().HaveCount(3).And.OnlyContain(k => k == AdmittedKeys(operations)[0]);
    }

    [Fact]
    public async Task should_run_handler_as_takeover_when_wait_and_replay_poll_is_admitted()
    {
        var operations = CreateAdmittingOperations();
        AdmitReturns(operations, InFlight, key => Admitted(key, IdempotencyFingerprint.Compute("x"), isTakeover: true));
        var middleware = CreateMiddleware(
            options: Monitor(_WaitAndReplay(TimeSpan.FromSeconds(10))),
            operations: operations,
            timeProvider: TimeProvider.System
        );
        bool? takeover = null;

        await middleware.InvokeAsync(
            CreateContext(idempotencyKey: "k1"),
            ctx =>
            {
                takeover = ctx.GetIdempotencyContext()!.IsTakeover;
                ctx.Response.StatusCode = 201;
                return Task.CompletedTask;
            }
        );

        takeover.Should().BeTrue();
        CallCount(operations, nameof(IIdempotentOperations.CompleteAsync)).Should().Be(1);
    }

    [Fact]
    public async Task should_return_409_in_flight_timeout_when_wait_and_replay_budget_ends()
    {
        var operations = Substitute.For<IIdempotentOperations>();
        AdmitReturns(operations, InFlight);
        var middleware = CreateMiddleware(
            options: Monitor(_WaitAndReplay(TimeSpan.FromMilliseconds(300))),
            operations: operations,
            timeProvider: TimeProvider.System
        );
        var context = CreateContext(idempotencyKey: "k1");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Response.StatusCode.Should().Be(409);
        (await ReadResponseAsync(context)).Should().Contain(IdempotencyErrorCodes.InFlightTimeout);
        AdmittedKeys(operations).Should().HaveCountGreaterThan(1, "the waiter polls before giving up");
    }

    [Fact]
    public async Task should_return_409_in_flight_timeout_without_running_handler_when_poll_fails_and_fail_open()
    {
        var operations = Substitute.For<IIdempotentOperations>();
        AdmitReturns(operations, InFlight, _ => throw new InvalidOperationException("store down"));
        var options = _WaitAndReplay(TimeSpan.FromSeconds(10));
        options.OnStoreError = OnStoreErrorBehavior.FailOpen;
        var middleware = CreateMiddleware(
            options: Monitor(options),
            operations: operations,
            timeProvider: TimeProvider.System
        );
        var context = CreateContext(idempotencyKey: "k1");
        var nextCalled = false;

        await middleware.InvokeAsync(context, _ => _Run(() => nextCalled = true));

        nextCalled.Should().BeFalse("another attempt holds the key, so running here could execute twice");
        context.Response.StatusCode.Should().Be(409);
        (await ReadResponseAsync(context)).Should().Contain(IdempotencyErrorCodes.InFlightTimeout);
    }

    // ── store failure before the handler ─────────────────────────────────────

    [Fact]
    public async Task should_propagate_admission_failure_when_on_store_error_is_throw()
    {
        var operations = Substitute.For<IIdempotentOperations>();
        AdmitReturns(operations, _ => throw new InvalidOperationException("store down"));
        var middleware = CreateMiddleware(operations: operations);
        var nextCalled = false;

        var act = () => middleware.InvokeAsync(CreateContext(idempotencyKey: "k1"), _ => _Run(() => nextCalled = true));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("store down");
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task should_run_handler_unguarded_and_log_once_when_admission_fails_and_fail_open()
    {
        var operations = Substitute.For<IIdempotentOperations>();
        AdmitReturns(operations, _ => throw new InvalidOperationException("store down"));
        var logger = Substitute.For<ILogger<IdempotencyMiddleware>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var middleware = CreateMiddleware(
            options: Monitor(new IdempotencyOptions { OnStoreError = OnStoreErrorBehavior.FailOpen }),
            operations: operations,
            logger: logger
        );
        var context = CreateContext(idempotencyKey: "k1");
        var nextCalled = false;

        await middleware.InvokeAsync(context, ctx => _Run(() => nextCalled = true));

        nextCalled.Should().BeTrue();
        context.GetIdempotencyContext().Should().BeNull();
        logger
            .ReceivedCalls()
            .Count(c =>
                string.Equals(c.GetMethodInfo().Name, nameof(ILogger.Log), StringComparison.Ordinal)
                && (LogLevel)c.GetArguments()[0]! == LogLevel.Warning
            )
            .Should()
            .Be(1);
        CallCount(operations, nameof(IIdempotentOperations.CompleteAsync)).Should().Be(0);
        CallCount(operations, nameof(IIdempotentOperations.ReleaseAsync)).Should().Be(0);
    }

    [Fact]
    public async Task should_propagate_request_cancellation_even_when_fail_open()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var ctProvider = Substitute.For<ICancellationTokenProvider>();
        ctProvider.Token.Returns(cts.Token);
        var operations = Substitute.For<IIdempotentOperations>();
        AdmitReturns(operations, _ => throw new OperationCanceledException(cts.Token));
        var middleware = CreateMiddleware(
            options: Monitor(new IdempotencyOptions { OnStoreError = OnStoreErrorBehavior.FailOpen }),
            operations: operations,
            cancellationTokenProvider: ctProvider
        );
        var nextCalled = false;

        var act = () => middleware.InvokeAsync(CreateContext(idempotencyKey: "k1"), _ => _Run(() => nextCalled = true));

        await act.Should().ThrowAsync<OperationCanceledException>();
        nextCalled.Should().BeFalse();
    }

    // ── lease renewal ────────────────────────────────────────────────────────

    [Fact]
    public async Task should_renew_lease_every_third_of_in_flight_lease_while_handler_runs()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var operations = CreateAdmittingOperations();
        var lease = TimeSpan.FromSeconds(30);
        var middleware = CreateMiddleware(
            options: Monitor(new IdempotencyOptions { InFlightLease = lease }),
            operations: operations,
            timeProvider: time
        );

        await middleware.InvokeAsync(
            CreateContext(idempotencyKey: "k1"),
            async ctx =>
            {
                await _AdvanceUntilAsync(time, lease / 3, () => CallCount(operations, "RenewAsync") >= 2);
                ctx.Response.StatusCode = 201;
            }
        );

        await operations
            .Received()
            .RenewAsync(Arg.Any<IdempotentAdmission>(), Arg.Is(lease), Arg.Any<CancellationToken>());
        var renewals = CallCount(operations, "RenewAsync");
        time.Advance(lease * 3);
        await Task.Delay(50, AbortToken);
        CallCount(operations, "RenewAsync").Should().Be(renewals, "renewal stops once the handler returns");
    }

    [Theory]
    [InlineData(LeaseRenewalStatus.Expired)]
    [InlineData(LeaseRenewalStatus.Stale)]
    [InlineData(LeaseRenewalStatus.Abandoned)]
    public async Task should_stop_renewing_when_lease_is_lost(LeaseRenewalStatus status)
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var operations = CreateAdmittingOperations();
        operations
            .RenewAsync(Arg.Any<IdempotentAdmission>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<LeaseRenewalResult>(new LeaseRenewalResult(status, null)));
        var lease = TimeSpan.FromSeconds(30);
        var middleware = CreateMiddleware(
            options: Monitor(new IdempotencyOptions { InFlightLease = lease }),
            operations: operations,
            timeProvider: time
        );

        await middleware.InvokeAsync(
            CreateContext(idempotencyKey: "k1"),
            async ctx =>
            {
                await _AdvanceUntilAsync(time, lease / 3, () => CallCount(operations, "RenewAsync") >= 1);

                for (var i = 0; i < 5; i++)
                {
                    time.Advance(lease / 3);
                    await Task.Delay(20, AbortToken);
                }

                ctx.Response.StatusCode = 201;
            }
        );

        CallCount(operations, "RenewAsync").Should().Be(1);
    }

    [Fact]
    public async Task should_not_stall_renewal_loop_when_a_renewal_blocks_past_its_timeout()
    {
        // A handler holding its fenced transaction blocks the renewal on the lease row. The loop must give that call
        // up at the timeout, cancel it, and renew again on the next tick.
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var operations = CreateAdmittingOperations();
        var firstCallCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        operations
            .RenewAsync(Arg.Any<IdempotentAdmission>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    var token = ci.ArgAt<CancellationToken>(2);
                    token.Register(() => firstCallCancelled.TrySetResult());
                    return new ValueTask<LeaseRenewalResult>(
                        Task.Delay(Timeout.Infinite, token)
                            .ContinueWith(
                                static _ => new LeaseRenewalResult(LeaseRenewalStatus.Renewed, null),
                                CancellationToken.None,
                                TaskContinuationOptions.ExecuteSynchronously,
                                TaskScheduler.Default
                            )
                    );
                }

                return new ValueTask<LeaseRenewalResult>(
                    new LeaseRenewalResult(LeaseRenewalStatus.Renewed, DateTimeOffset.UtcNow.AddMinutes(1))
                );
            });
        var lease = TimeSpan.FromSeconds(30);
        var middleware = CreateMiddleware(
            options: Monitor(new IdempotencyOptions { InFlightLease = lease }),
            operations: operations,
            timeProvider: time
        );

        await middleware.InvokeAsync(
            CreateContext(idempotencyKey: "k1"),
            async ctx =>
            {
                await _AdvanceUntilAsync(time, lease / 3, () => Volatile.Read(ref calls) >= 2);
                ctx.Response.StatusCode = 201;
            }
        );

        Volatile.Read(ref calls).Should().BeGreaterThanOrEqualTo(2);
        await firstCallCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
    }

    // ── malformed header ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("key\u0001value")]
    [InlineData("key\u007fvalue")]
    public async Task should_reject_with_400_when_key_contains_control_characters(string malformedKey)
    {
        var operations = CreateAdmittingOperations();
        var creator = CreateProblemDetailsCreator();
        var middleware = CreateMiddleware(operations: operations, problemDetailsCreator: creator);

        await middleware.InvokeAsync(CreateContext(idempotencyKey: malformedKey), _ => Task.CompletedTask);

        creator
            .Received(1)
            .BadRequest(Arg.Any<string?>(), Arg.Is<ErrorDescriptor>(e => e.Code == IdempotencyErrorCodes.KeyMalformed));
        operations.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_reject_with_400_when_key_exceeds_255_characters()
    {
        var operations = CreateAdmittingOperations();
        var creator = CreateProblemDetailsCreator();
        var middleware = CreateMiddleware(operations: operations, problemDetailsCreator: creator);

        await middleware.InvokeAsync(CreateContext(idempotencyKey: new string('k', 256)), _ => Task.CompletedTask);

        creator
            .Received(1)
            .BadRequest(Arg.Any<string?>(), Arg.Is<ErrorDescriptor>(e => e.Code == IdempotencyErrorCodes.KeyMalformed));
        operations.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_reject_with_400_when_key_is_multi_valued()
    {
        var operations = CreateAdmittingOperations();
        var creator = CreateProblemDetailsCreator();
        var middleware = CreateMiddleware(operations: operations, problemDetailsCreator: creator);
        var context = CreateContext(idempotencyKey: "a");
        context.Request.Headers.Append(HttpHeaderNames.IdempotencyKey, "b");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        creator
            .Received(1)
            .BadRequest(Arg.Any<string?>(), Arg.Is<ErrorDescriptor>(e => e.Code == IdempotencyErrorCodes.KeyMalformed));
        operations.ReceivedCalls().Should().BeEmpty();
    }

    // ── oversize body ────────────────────────────────────────────────────────

    [Fact]
    public async Task should_reject_with_413_without_store_call_when_body_exceeds_cap_and_behavior_is_reject()
    {
        var operations = CreateAdmittingOperations();
        var middleware = CreateMiddleware(
            options: Monitor(new IdempotencyOptions { MaxBodySizeForHashing = 4 }),
            operations: operations
        );
        var context = CreateContext(idempotencyKey: "k1", body: new byte[16]);
        var nextCalled = false;

        await middleware.InvokeAsync(context, _ => _Run(() => nextCalled = true));

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(413);
        operations.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_pass_through_without_store_call_when_body_exceeds_cap_and_behavior_is_pass_through()
    {
        var operations = CreateAdmittingOperations();
        var middleware = CreateMiddleware(
            options: Monitor(
                new IdempotencyOptions { MaxBodySizeForHashing = 4, OversizeBehavior = OversizeBehavior.PassThrough }
            ),
            operations: operations
        );
        var nextCalled = false;

        await middleware.InvokeAsync(
            CreateContext(idempotencyKey: "k1", body: new byte[16]),
            _ => _Run(() => nextCalled = true)
        );

        nextCalled.Should().BeTrue();
        operations.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public async Task should_preserve_fingerprint_and_rewind_across_request_buffer_threshold(int threshold)
    {
        var operations = CreateAdmittingOperations();
        var middleware = CreateMiddleware(
            options: Monitor(new IdempotencyOptions { RequestBodyBufferThreshold = threshold }),
            operations: operations
        );
        byte[] body = [1, 2, 3, 4, 5, 6, 7, 8];
        byte[]? seenBody = null;

        await middleware.InvokeAsync(
            CreateContext(idempotencyKey: "k1", body: body),
            async ctx =>
            {
                using var buffer = new MemoryStream();
                await ctx.Request.Body.CopyToAsync(buffer, AbortToken);
                seenBody = buffer.ToArray();
            }
        );

        seenBody.Should().Equal(body);
        await operations
            .Received(1)
            .AdmitAsync(
                Arg.Any<string>(),
                Arg.Is(FingerprintOf(body)),
                Arg.Any<string?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public void should_hash_scope_to_lowercase_sha256_hex()
    {
        var key = IdempotencyMiddleware.HashScope("idem:u1:POST:/x:k");

        key.Should().Be(Convert.ToHexStringLower(SHA256.HashData("idem:u1:POST:/x:k"u8)));
        IdempotencyMiddleware.HashScope(new string('x', 5000)).Should().HaveLength(64);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static Task _Run(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private static IdempotencyOptions _WaitAndReplay(TimeSpan timeout)
    {
        return new IdempotencyOptions
        {
            InFlightStrategy = InFlightStrategy.WaitAndReplay,
            InFlightLockTimeout = timeout,
        };
    }

    private static void _CompleteThrows(IIdempotentOperations operations, Exception exception)
    {
        operations
            .CompleteAsync(
                Arg.Any<IdempotentAdmission>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<ValueTask>(_ => throw exception);
    }

    /// <summary>
    /// Advances the fake clock one step at a time, yielding real time between steps so the renewal loop's continuations
    /// run, until <paramref name="condition" /> holds.
    /// </summary>
    private static async Task _AdvanceUntilAsync(FakeTimeProvider time, TimeSpan step, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The renewal loop never reached the expected state.");
            }

            time.Advance(step);
            await Task.Delay(20, AbortToken);
        }
    }

    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }
}
