// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Text;
using Headless.Hosting;
using Headless.Http;
using Headless.Idempotency;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Tests;

/// <summary>
/// The post-commit completion window closed by a recovery point: a handler records a final "done" point carrying its
/// response in the same unit as its side effect, the attempt dies before the middleware's completion commits, and the
/// retry that takes the key over answers from that point instead of running the side effect again.
/// </summary>
public sealed class IdempotencyRecoveryPointInMemoryEndToEndTests : TestBase
{
    [Fact]
    public async Task should_answer_a_takeover_from_the_done_point_without_repeating_the_side_effect()
    {
        var clock = new ShiftableTimeProvider();
        var scenario = new RecoveryPointScenario();

        await scenario.RunAsync(
            services =>
            {
                services.AddHeadlessIdempotency(static setup =>
                {
                    setup.UseInMemory();
                    setup.ConfigureOptions(static options => options.PurgeInterval = null);
                });
                // Only the clock the store judges leases by moves; timers stay real.
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
            },
            static (services, cancellationToken) =>
                services.GetRequiredService<IUnitOfWorkFactory>().BeginAsync(cancellationToken: cancellationToken),
            (_, _) =>
            {
                clock.Advance(RecoveryPointScenario.Lease + TimeSpan.FromMinutes(1));

                return Task.CompletedTask;
            },
            AbortToken
        );
    }
}

[Collection<ApiIdempotencyPostgreSqlFixture>]
public sealed class IdempotencyRecoveryPointPostgreSqlEndToEndTests(ApiIdempotencyPostgreSqlFixture fixture) : TestBase
{
    [Fact]
    public async Task should_answer_a_takeover_from_the_done_point_without_repeating_the_side_effect()
    {
        var scenario = new RecoveryPointScenario();

        await scenario.RunAsync(
            fixture.ConfigureStore,
            async (services, cancellationToken) =>
            {
                // The handler's own unit on the database that holds the records, so the point commits with its
                // business writes. The unit opens the connection and ends with it.
                var connection = new NpgsqlConnection(fixture.ConnectionString);

                return await services
                    .GetRequiredService<IUnitOfWorkFactory>()
                    .BeginAsync(connection, cancellationToken);
            },
            async (key, cancellationToken) =>
            {
                // The database clock decides the lease, so the lease is aged in place rather than waited out.
                await using var connection = new NpgsqlConnection(fixture.ConnectionString);
                await connection.OpenAsync(cancellationToken);
                await using var command = new NpgsqlCommand(
                    $"""
                    UPDATE "{HeadlessStorageDefaults.Schema}".idempotency_records
                    SET lease_expires_at = lease_expires_at - interval '1 hour'
                    WHERE tenant_id = @tenant AND idempotency_key = @key
                    """,
                    connection
                );
                command.Parameters.AddWithValue("tenant", key.TenantId ?? "");
                command.Parameters.AddWithValue(nameof(key), key.Key);
                (await command.ExecuteNonQueryAsync(cancellationToken)).Should().Be(1);
            },
            AbortToken
        );
    }
}

/// <summary>A payment endpoint whose side effect must happen once, and a completion the test can make disappear.</summary>
internal sealed class RecoveryPointScenario
{
    public static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    private const string _DonePoint = "done";
    private const string _ReceiptContract = "payments.receipt/v1";

    private int _charges;
    private IdempotencyKey? _admittedKey;

    public async Task RunAsync(
        Action<IServiceCollection> configureStore,
        Func<IServiceProvider, CancellationToken, ValueTask<IUnitOfWork>> beginUnit,
        Func<IdempotencyKey, CancellationToken, Task> expireLease,
        CancellationToken cancellationToken
    )
    {
        var losing = new LosingCompletionSwitch();

        await using var app = await IdempotencyTestApp.CreateAsync(
            services =>
            {
                configureStore(services);
                losing.Decorate(services);
            },
            options => options.InFlightLease = Lease,
            app => _MapPayment(app, beginUnit)
        );
        using var client = IdempotencyTestApp.CreateClient(app);
        var headerKey = $"k-{Guid.NewGuid():N}";

        // The first attempt charges, records "done" with its receipt in the charge's own unit, and then dies before
        // the middleware's completion commits: the record stays pending under a lease nobody renews.
        losing.LoseNextCompletion = true;
        using var crashed = await _PostAsync(client, headerKey, cancellationToken);
        Volatile.Read(ref _charges).Should().Be(1);
        losing.LoseNextCompletion.Should().BeFalse("the first attempt reached its completion");

        _admittedKey.Should().NotBeNull("the handler ran");
        await expireLease(_admittedKey!.Value, cancellationToken);

        // The retry takes the key over, finds "done", and answers from it.
        using var resumed = await _PostAsync(client, headerKey, cancellationToken);
        resumed.StatusCode.Should().Be(HttpStatusCode.Created);
        resumed.Headers.GetValues("X-Resumed-From").Should().ContainSingle().Which.Should().Be(_DonePoint);
        resumed.Headers.GetValues("X-Takeover").Should().ContainSingle().Which.Should().Be("True");
        var body = await resumed.Content.ReadAsStringAsync(cancellationToken);
        body.Should().Be("""{"charge":1}""");
        Volatile.Read(ref _charges).Should().Be(1, "the side effect already committed and must not repeat");

        // The retry's own completion committed, so the next request replays it.
        using var replayed = await _PostAsync(client, headerKey, cancellationToken);
        replayed.StatusCode.Should().Be(HttpStatusCode.Created);
        replayed
            .Headers.GetValues(HttpHeaderNames.IdempotentReplayed)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("true");
        (await replayed.Content.ReadAsStringAsync(cancellationToken)).Should().Be(body);
        Volatile.Read(ref _charges).Should().Be(1);
    }

    private void _MapPayment(
        WebApplication app,
        Func<IServiceProvider, CancellationToken, ValueTask<IUnitOfWork>> beginUnit
    )
    {
        app.MapPost(
            "/pay",
            async ctx =>
            {
                var idempotency = ctx.GetIdempotencyContext()!;
                _admittedKey = idempotency.Admission.Key;
                ctx.Response.Headers["X-Takeover"] = idempotency.IsTakeover.ToString();

                if (idempotency.RecoveryPoint is { Name: _DonePoint } done)
                {
                    // The work committed on an earlier attempt: rebuild the response from what it recorded.
                    ctx.Response.Headers["X-Resumed-From"] = done.Name;
                    ctx.Response.StatusCode = StatusCodes.Status201Created;
                    await ctx.Response.Body.WriteAsync(done.State, ctx.RequestAborted);

                    return;
                }

                var receipt = Encoding.UTF8.GetBytes($$"""{"charge":{{Interlocked.Increment(ref _charges)}}}""");

                // The charge and its "done" point commit together, so a crash after this line resumes from the point.
                await using (var unit = await beginUnit(ctx.RequestServices, ctx.RequestAborted))
                {
                    await unit.Idempotency.SetRecoveryPointAsync(
                        idempotency.Admission,
                        _DonePoint,
                        receipt,
                        _ReceiptContract,
                        ctx.RequestAborted
                    );
                    await unit.CompleteAsync(ctx.RequestAborted);
                }

                ctx.Response.StatusCode = StatusCodes.Status201Created;
                await ctx.Response.Body.WriteAsync(receipt, ctx.RequestAborted);
            }
        );
    }

    private static async Task<HttpResponseMessage> _PostAsync(
        HttpClient client,
        string key,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/pay");
        request.Content = new StringContent("amount=10");
        request.Headers.Add(HttpHeaderNames.IdempotencyKey, key);

        return await client.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// Wraps the registered <see cref="IIdempotentOperations" /> so a test can make the next completion fail as if the
/// process died before it committed.
/// </summary>
internal sealed class LosingCompletionSwitch
{
    private int _loseNext;

    public bool LoseNextCompletion
    {
        get => Volatile.Read(ref _loseNext) == 1;
        set => Volatile.Write(ref _loseNext, value ? 1 : 0);
    }

    public void Decorate(IServiceCollection services)
    {
        var registered = services.Single(static d => d.ServiceType == typeof(IIdempotentOperations));
        var implementation = registered.ImplementationType!;
        services.Remove(registered);
        services.AddSingleton<IIdempotentOperations>(sp => new Operations(
            (IIdempotentOperations)ActivatorUtilities.CreateInstance(sp, implementation),
            this
        ));
    }

    private sealed class Operations(IIdempotentOperations inner, LosingCompletionSwitch owner) : IIdempotentOperations
    {
        public ValueTask<IdempotentAdmission> AdmitAsync(
            string key,
            IdempotencyFingerprint fingerprint,
            string? expectedContract = null,
            TimeSpan? leaseDuration = null,
            TimeSpan? retention = null,
            CancellationToken cancellationToken = default
        )
        {
            return inner.AdmitAsync(key, fingerprint, expectedContract, leaseDuration, retention, cancellationToken);
        }

        public ValueTask CompleteAsync(
            IdempotentAdmission admission,
            ReadOnlyMemory<byte> result,
            string contract,
            TimeSpan? retention = null,
            CancellationToken cancellationToken = default
        )
        {
            if (Interlocked.Exchange(ref owner._loseNext, 0) == 1)
            {
                throw new InvalidOperationException("Simulated crash before the completion committed.");
            }

            return inner.CompleteAsync(admission, result, contract, retention, cancellationToken);
        }

        public ValueTask SetRecoveryPointAsync(
            IdempotentAdmission admission,
            string point,
            ReadOnlyMemory<byte> state,
            string contract,
            CancellationToken cancellationToken = default
        )
        {
            return inner.SetRecoveryPointAsync(admission, point, state, contract, cancellationToken);
        }

        public ValueTask<IdempotentLeaseStatus> ReleaseAsync(
            IdempotentAdmission admission,
            CancellationToken cancellationToken = default
        )
        {
            return inner.ReleaseAsync(admission, cancellationToken);
        }

        public ValueTask<IdempotentLeaseRenewal> RenewAsync(
            IdempotentAdmission admission,
            TimeSpan duration,
            CancellationToken cancellationToken = default
        )
        {
            return inner.RenewAsync(admission, duration, cancellationToken);
        }

        public ValueTask<IdempotencyPeekStatus> PeekAsync(string key, CancellationToken cancellationToken = default)
        {
            return inner.PeekAsync(key, cancellationToken);
        }

        public ValueTask<IdempotentResult?> GetResultAsync(string key, CancellationToken cancellationToken = default)
        {
            return inner.GetResultAsync(key, cancellationToken);
        }
    }
}

/// <summary>
/// The system clock shifted by a settable offset. Only the reported time moves; timers created through it stay on
/// real time, so the middleware's renewal loop behaves normally.
/// </summary>
internal sealed class ShiftableTimeProvider : TimeProvider
{
    private long _offsetTicks;

    public void Advance(TimeSpan by)
    {
        Interlocked.Add(ref _offsetTicks, by.Ticks);
    }

    public override DateTimeOffset GetUtcNow()
    {
        return System.GetUtcNow() + TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));
    }
}
