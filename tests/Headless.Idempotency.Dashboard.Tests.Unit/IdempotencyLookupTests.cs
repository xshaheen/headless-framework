// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Text;
using Headless.Idempotency;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Tests;

public sealed class IdempotencyLookupTests : TestBase
{
    private const string _Lookup = IdempotencyHost.Api + "/lookup";
    private const string _Key = "order-4711-charge";
    private const string _Contract = "orders.create/v1";

    private static readonly IdempotencyFingerprint _Fingerprint = IdempotencyFingerprint.Compute("""{"order":42}""");

    private static readonly byte[] _Payload = Encoding.UTF8.GetBytes(
        """{"orderId":42,"email":"someone@example.com"}"""
    );

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 27, 14, 20, 0, TimeSpan.Zero));

    public static TheoryData<IdempotencyTestStore> Stores =>
        [IdempotencyTestStore.InMemory, IdempotencyTestStore.Cache];

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task should_read_a_completed_key_with_its_result_contract_and_size_and_never_its_bytes(
        IdempotencyTestStore store
    )
    {
        // given
        await using var app = await IdempotencyHost.StartAsync(_time, AbortToken, store: store);
        var operations = app.Services.GetRequiredService<IIdempotentOperations>();
        var admission = await operations.AdmitAsync(_Key, _Fingerprint, cancellationToken: AbortToken);
        await operations.CompleteAsync(admission, _Payload, _Contract, cancellationToken: AbortToken);

        // when
        var (status, text, body) = await _LookUpAsync(app, new { key = _Key });

        // then
        status.Should().Be(HttpStatusCode.OK);
        text.Should().NotContain("someone@example.com").And.NotContain(Convert.ToBase64String(_Payload));
        text.Should().NotContain(_Key);
        body.GetProperty("found").GetBoolean().Should().BeTrue();
        body.TryGetProperty("tenantId", out _).Should().BeFalse();
        var record = body.GetProperty("record");
        record.GetProperty("status").GetString().Should().Be("Completed");
        record.GetProperty("generation").GetInt64().Should().Be(admission.Generation!.Value);
        record.GetProperty("resultContract").GetString().Should().Be(_Contract);
        record.GetProperty("resultSizeBytes").GetInt64().Should().Be(_Payload.Length);
        record.GetProperty("isLeaseExpired").GetBoolean().Should().BeFalse();
        record.TryGetProperty("leaseExpiresAt", out _).Should().BeFalse();
        record.GetProperty("observedAt").GetDateTimeOffset().Should().Be(_time.GetUtcNow());
        record.GetProperty("retentionUntil").GetDateTimeOffset().Should().BeAfter(_time.GetUtcNow());
        record.GetProperty("fingerprint").GetProperty("algorithm").GetString().Should().Be(IdempotencyFingerprint.V1);
        record
            .GetProperty("fingerprint")
            .GetProperty("hash")
            .GetString()
            .Should()
            .Be(Convert.ToHexStringLower(_Fingerprint.Hash.Span))
            .And.MatchRegex("^[0-9a-f]{64}$");

        if (store == IdempotencyTestStore.Cache)
        {
            record.TryGetProperty("takeoverCount", out _).Should().BeFalse();
        }
        else
        {
            record.GetProperty("takeoverCount").GetInt32().Should().Be(0);
        }
    }

    [Fact]
    public async Task should_read_an_in_flight_key_with_its_live_lease_and_recovery_point_without_its_state()
    {
        // given
        await using var app = await IdempotencyHost.StartAsync(_time, AbortToken);
        var operations = app.Services.GetRequiredService<IIdempotentOperations>();
        var admission = await operations.AdmitAsync(
            _Key,
            _Fingerprint,
            leaseDuration: TimeSpan.FromMinutes(10),
            cancellationToken: AbortToken
        );
        var state = Encoding.UTF8.GetBytes("""{"authorization":"auth_secret_123"}""");
        await operations.SetRecoveryPointAsync(
            admission,
            "payment-authorized",
            state,
            "payments.recovery/v1",
            AbortToken
        );

        // when
        var (status, text, body) = await _LookUpAsync(app, new { key = _Key });

        // then
        status.Should().Be(HttpStatusCode.OK);
        text.Should().NotContain("auth_secret_123").And.NotContain(Convert.ToBase64String(state));
        var record = body.GetProperty("record");
        record.GetProperty("status").GetString().Should().Be("Pending");
        record.GetProperty("isLeaseExpired").GetBoolean().Should().BeFalse();
        record.GetProperty("leaseExpiresAt").GetDateTimeOffset().Should().Be(_time.GetUtcNow().AddMinutes(10));
        record.GetProperty("recoveryPoint").GetString().Should().Be("payment-authorized");
        record.GetProperty("recoveryContract").GetString().Should().Be("payments.recovery/v1");
        record.GetProperty("recoveryStateSizeBytes").GetInt64().Should().Be(state.Length);
        record.TryGetProperty("resultSizeBytes", out _).Should().BeFalse();
    }

    [Fact]
    public async Task should_read_an_in_flight_key_past_its_retention_while_its_lease_is_live()
    {
        // given
        await using var app = await IdempotencyHost.StartAsync(_time, AbortToken);
        await app
            .Services.GetRequiredService<IIdempotentOperations>()
            .AdmitAsync(
                _Key,
                _Fingerprint,
                leaseDuration: TimeSpan.FromMinutes(10),
                retention: TimeSpan.FromSeconds(1),
                cancellationToken: AbortToken
            );
        _time.Advance(TimeSpan.FromSeconds(2));

        // when
        var (_, _, body) = await _LookUpAsync(app, new { key = _Key });

        // then
        var record = body.GetProperty("record");
        record.GetProperty("status").GetString().Should().Be("Pending");
        record
            .GetProperty("retentionUntil")
            .GetDateTimeOffset()
            .Should()
            .BeBefore(record.GetProperty("observedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task should_read_a_stalled_key_as_released_with_an_expired_lease()
    {
        // given
        await using var app = await IdempotencyHost.StartAsync(_time, AbortToken);
        await app
            .Services.GetRequiredService<IIdempotentOperations>()
            .AdmitAsync(_Key, _Fingerprint, leaseDuration: TimeSpan.FromSeconds(1), cancellationToken: AbortToken);
        _time.Advance(TimeSpan.FromSeconds(2));

        // when
        var (_, _, body) = await _LookUpAsync(app, new { key = _Key });

        // then
        var record = body.GetProperty("record");
        record.GetProperty("status").GetString().Should().Be("Released");
        record.GetProperty("isLeaseExpired").GetBoolean().Should().BeTrue();
        record.GetProperty("leaseExpiresAt").GetDateTimeOffset().Should().Be(_time.GetUtcNow().AddSeconds(-1));
    }

    [Fact]
    public async Task should_read_a_released_key_with_its_generation_and_no_lease()
    {
        // given
        await using var app = await IdempotencyHost.StartAsync(_time, AbortToken);
        var operations = app.Services.GetRequiredService<IIdempotentOperations>();
        var admission = await operations.AdmitAsync(_Key, _Fingerprint, cancellationToken: AbortToken);
        await operations.ReleaseAsync(admission, AbortToken);

        // when
        var (_, _, body) = await _LookUpAsync(app, new { key = _Key });

        // then
        var record = body.GetProperty("record");
        record.GetProperty("status").GetString().Should().Be("Released");
        record.GetProperty("isLeaseExpired").GetBoolean().Should().BeFalse();
        record.GetProperty("generation").GetInt64().Should().Be(admission.Generation!.Value);
        record.TryGetProperty("leaseExpiresAt", out _).Should().BeFalse();
    }

    [Fact]
    public async Task should_answer_found_false_for_a_key_never_admitted()
    {
        // given
        await using var app = await IdempotencyHost.StartAsync(_time, AbortToken);

        // when
        var (status, _, body) = await _LookUpAsync(app, new { key = _Key });

        // then
        status.Should().Be(HttpStatusCode.OK);
        body.GetProperty("found").GetBoolean().Should().BeFalse();
        body.TryGetProperty("record", out _).Should().BeFalse();
    }

    [Fact]
    public async Task should_find_a_tenant_key_only_in_that_tenant_and_echo_the_tenant_but_never_the_key()
    {
        // given
        await using var app = await IdempotencyHost.StartAsync(_time, AbortToken);
        var currentTenant = app.Services.GetRequiredService<ICurrentTenant>();

        using (currentTenant.Change("acme"))
        {
            await app
                .Services.GetRequiredService<IIdempotentOperations>()
                .AdmitAsync(_Key, _Fingerprint, cancellationToken: AbortToken);
        }

        // when
        var (_, tenantText, tenant) = await _LookUpAsync(app, new { key = _Key, tenantId = "acme" });
        var (_, _, host) = await _LookUpAsync(app, new { key = _Key });

        // then
        tenant.GetProperty("found").GetBoolean().Should().BeTrue();
        tenant.GetProperty("tenantId").GetString().Should().Be("acme");
        tenantText.Should().NotContain(_Key);
        host.GetProperty("found").GetBoolean().Should().BeFalse();
    }

    public static TheoryData<string, string> Refusals =>
        new()
        {
            { """{"key":"order-47""", "body" },
            { "[]", "body" },
            { """{"tenantId":"acme"}""", "key" },
            { """{"key":""}""", "key" },
            { $$"""{"key":"{{new string('k', 257)}}"}""", "key" },
            { """{"key":" order-4711-charge"}""", "key" },
            { """{"key":"order-4711-charge\u0000"}""", "key" },
            // System.Text.Json refuses an unpaired surrogate escape before the validator sees the key.
            { """{"key":"order-4711-charge\ud800"}""", "body" },
            { """{"key":"order-4711-charge","tenantId":""}""", "tenantId" },
            { """{"key":"order-4711-charge","tenantId":"  "}""", "tenantId" },
            { $$"""{"key":"order-4711-charge","tenantId":"{{new string('t', 129)}}"}""", "tenantId" },
        };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task should_refuse_a_bad_body_naming_the_field_and_never_the_key(string json, string field)
    {
        // given
        await using var app = await IdempotencyHost.StartAsync(_time, AbortToken);
        using var client = IdempotencyHost.Client(app);
        var cookie = await IdempotencyHost.SignInAsync(client, "viewer", AbortToken);

        // when
        using var response = await IdempotencyHost.SendAsync(
            client,
            HttpMethod.Post,
            _Lookup,
            new StringContent(json, Encoding.UTF8, "application/json"),
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var text = await response.Content.ReadAsStringAsync(AbortToken);
        text.Should().NotContain(_Key).And.NotContain("kkkkkkkkkk").And.NotContain("tttttttttt");
        JsonDocument.Parse(text).RootElement.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    [Fact]
    public async Task should_answer_503_naming_neither_key_nor_tenant_and_log_without_them_when_the_store_fails()
    {
        // given
        var inspector = Substitute.For<IIdempotencyInspector>();
        inspector
            .InspectAsync(default!, AbortToken)
            .ReturnsForAnyArgs<ValueTask<IdempotencyRecordSnapshot?>>(_ =>
                throw new InvalidOperationException($"The cache entry 'acme:{_Key}' is corrupt.")
            );
        using var logs = new RecordingLoggerProvider();
        await using var app = await IdempotencyHost.StartAsync(
            _time,
            AbortToken,
            configure: services => services.AddSingleton(inspector),
            logs: logs
        );

        // when
        var (status, text, body) = await _LookUpAsync(app, new { key = _Key, tenantId = "acme" });

        // then
        status.Should().Be(HttpStatusCode.ServiceUnavailable);
        text.Should().NotContain(_Key).And.NotContain("acme");
        body.GetProperty("detail").GetString().Should().Be("The idempotency store is unavailable. Try again.");
        var warnings = logs.Entries.Where(static entry => entry.Level >= LogLevel.Warning).ToList();
        warnings.Should().ContainSingle();
        warnings[0]
            .Message.Should()
            .Contain(nameof(InvalidOperationException))
            .And.NotContain(_Key)
            .And.NotContain("acme");
        warnings[0].Exception.Should().BeNull();
    }

    [Fact]
    public async Task should_answer_400_naming_the_field_when_the_store_refuses_an_argument()
    {
        // given
        var inspector = Substitute.For<IIdempotencyInspector>();
        inspector
            .InspectAsync(default!, AbortToken)
            .ReturnsForAnyArgs<ValueTask<IdempotencyRecordSnapshot?>>(_ =>
                throw new ArgumentException($"The tenant 'acme' is refused for '{_Key}'.",
#pragma warning disable MA0015 // The store names the tenant argument tenantId; the module maps that name to its field.
                    "tenantId"
#pragma warning restore MA0015
                )
            );
        await using var app = await IdempotencyHost.StartAsync(
            _time,
            AbortToken,
            configure: services => services.AddSingleton(inspector)
        );

        // when
        var (status, text, body) = await _LookUpAsync(app, new { key = _Key, tenantId = "acme" });

        // then
        status.Should().Be(HttpStatusCode.BadRequest);
        text.Should().NotContain(_Key).And.NotContain("acme");
        body.GetProperty("errors").TryGetProperty("tenantId", out _).Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_read_in_exactly_the_named_tenant_and_close_the_scope_when_the_store_answers_or_fails(
        bool fails
    )
    {
        // given
        var tenant = new RecordingCurrentTenant();
        var inspector = Substitute.For<IIdempotencyInspector>();
        string? seen = "unset";
        inspector
            .InspectAsync(default!, AbortToken)
            .ReturnsForAnyArgs<ValueTask<IdempotencyRecordSnapshot?>>(_ =>
            {
                seen = tenant.Id;

                return fails
                    ? throw new TimeoutException("The store timed out.")
                    : ValueTask.FromResult<IdempotencyRecordSnapshot?>(null);
            });
        await using var app = await IdempotencyHost.StartAsync(
            _time,
            AbortToken,
            configure: services =>
            {
                services.AddSingleton<ICurrentTenant>(tenant);
                services.AddSingleton(inspector);
            }
        );

        // when
        var (status, _, _) = await _LookUpAsync(app, new { key = _Key, tenantId = "acme" });
        var named = seen;
        await _LookUpAsync(app, new { key = _Key });

        // then
        status.Should().Be(fails ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        named.Should().Be("acme");
        seen.Should().BeNull("an absent tenant reads the host scope, never the ambient tenant");
        tenant.Changes.Should().Equal("acme", null);
        tenant.OpenScopes.Should().Be(0);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task should_answer_no_other_method_on_the_lookup_route(string method)
    {
        // given
        await using var app = await IdempotencyHost.StartAsync(_time, AbortToken);
        using var client = IdempotencyHost.Client(app);
        var cookie = await IdempotencyHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await IdempotencyHost.SendAsync(
            client,
            new HttpMethod(method),
            _Lookup + "?key=" + _Key,
            string.Equals(method, "GET", StringComparison.Ordinal)
                ? null
                : new StringContent($$"""{"key":"{{_Key}}"}""", Encoding.UTF8, "application/json"),
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().BeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_refuse_a_lookup_without_a_session()
    {
        // given
        await using var app = await IdempotencyHost.StartAsync(_time, AbortToken);
        using var client = IdempotencyHost.Client(app);

        // when
        using var response = await IdempotencyHost.PostAsync(client, _Lookup, new { key = _Key }, null, AbortToken);

        // then
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    private static async Task<(HttpStatusCode Status, string Text, JsonElement Body)> _LookUpAsync(
        WebApplication app,
        object body
    )
    {
        using var client = IdempotencyHost.Client(app);
        var cookie = await IdempotencyHost.SignInAsync(client, "viewer", AbortToken);
        using var response = await IdempotencyHost.PostAsync(client, _Lookup, body, cookie, AbortToken);
        var text = await response.Content.ReadAsStringAsync(AbortToken);

        return (response.StatusCode, text, JsonDocument.Parse(text).RootElement.Clone());
    }

    /// <summary>A current tenant that records every change and whether each scope was closed.</summary>
    private sealed class RecordingCurrentTenant : ICurrentTenant
    {
        private readonly Lock _lock = new();
        private readonly Stack<string?> _scopes = new();
        private readonly List<string?> _changes = [];

        public bool IsAvailable => Id is not null;

        public string? Id
        {
            get
            {
                lock (_lock)
                {
                    return _scopes.TryPeek(out var id) ? id : null;
                }
            }
        }

        public string? Name => null;

        public IReadOnlyList<string?> Changes
        {
            get
            {
                lock (_lock)
                {
                    return [.. _changes];
                }
            }
        }

        public int OpenScopes
        {
            get
            {
                lock (_lock)
                {
                    return _scopes.Count;
                }
            }
        }

        public IDisposable Change(string? id, string? name = null)
        {
            lock (_lock)
            {
                _changes.Add(id);
                _scopes.Push(id);
            }

            return new Scope(this);
        }

        private sealed class Scope(RecordingCurrentTenant owner) : IDisposable
        {
            public void Dispose()
            {
                lock (owner._lock)
                {
                    owner._scopes.Pop();
                }
            }
        }
    }
}
