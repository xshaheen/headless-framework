// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless.Abstractions;
using Headless.Api.Idempotency;
using Headless.Constants;
using Headless.Fencing;
using Headless.Idempotency;
using Headless.MultiTenancy;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using IdempotencyMiddleware = Headless.Api.Idempotency.IdempotencyMiddleware;

namespace Tests;

/// <summary>
/// Shared helpers for IdempotencyMiddleware unit tests. Centralizes the substitute graph (store, tenant, user, clock,
/// ct provider, logger) so individual tests override only the collaborators they care about.
/// </summary>
public abstract class IdempotencyMiddlewareTestBase : TestBase
{
    internal const string TestTenant = "t1";

    internal IdempotencyMiddleware CreateMiddleware(
        IOptionsMonitor<IdempotencyOptions>? options = null,
        IIdempotentOperations? operations = null,
        ICurrentTenant? currentTenant = null,
        ICurrentUser? currentUser = null,
        IProblemDetailsCreator? problemDetailsCreator = null,
        TimeProvider? timeProvider = null,
        ICancellationTokenProvider? cancellationTokenProvider = null,
        ILogger<IdempotencyMiddleware>? logger = null
    )
    {
        options ??= Monitor(new IdempotencyOptions());
        operations ??= CreateAdmittingOperations();

        // Default test identity: tenant "t1" + authenticated user "u1". A real user is the
        // default because IdempotencyOptions.RequireUserIdentity defaults to true — anonymous
        // tenant-only requests fall through without applying idempotency. Tests exercising the
        // "no user" or "no tenant" branches pass explicit substitutes returning null.
        if (currentTenant is null)
        {
            currentTenant = Substitute.For<ICurrentTenant>();
            currentTenant.Id.Returns(TestTenant);
        }

        if (currentUser is null)
        {
            currentUser = Substitute.For<ICurrentUser>();
            currentUser.UserId.Returns(new UserId("u1"));
        }

        problemDetailsCreator ??= CreateProblemDetailsCreator();
        timeProvider ??= new FakeTimeProvider(DateTimeOffset.UtcNow);

        if (cancellationTokenProvider is null)
        {
            cancellationTokenProvider = Substitute.For<ICancellationTokenProvider>();
            cancellationTokenProvider.Token.Returns(CancellationToken.None);
        }

        logger ??= LoggerFactory.CreateLogger<IdempotencyMiddleware>();

        return new IdempotencyMiddleware(
            options,
            operations,
            currentTenant,
            currentUser,
            problemDetailsCreator,
            timeProvider,
            cancellationTokenProvider,
            logger
        );
    }

    internal static IOptionsMonitor<IdempotencyOptions> Monitor(IdempotencyOptions options)
    {
        var monitor = Substitute.For<IOptionsMonitor<IdempotencyOptions>>();
        monitor.CurrentValue.Returns(options);
        return monitor;
    }

    /// <summary>Problem-details creator whose results carry the status code and the first error code.</summary>
    internal static IProblemDetailsCreator CreateProblemDetailsCreator()
    {
        var creator = Substitute.For<IProblemDetailsCreator>();
        creator
            .Conflict(Arg.Any<IReadOnlyCollection<ErrorDescriptor>>())
            .Returns(ci => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Detail = ci.Arg<IReadOnlyCollection<ErrorDescriptor>>().First().Code,
            });
        creator
            .UnprocessableEntity(Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>>>())
            .Returns(ci => new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Detail = ci.Arg<IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>>>().Values.First()[0].Code,
            });
        creator
            .BadRequest(Arg.Any<string?>(), Arg.Any<ErrorDescriptor?>())
            .Returns(ci => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Detail = ci.ArgAt<ErrorDescriptor?>(1)?.Code,
            });
        return creator;
    }

    /// <summary>A store that admits every request as its owner and accepts every renewal, completion, and release.</summary>
    internal static IIdempotentOperations CreateAdmittingOperations(bool isTakeover = false)
    {
        var operations = Substitute.For<IIdempotentOperations>();
        operations
            .AdmitAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyFingerprint>(),
                Arg.Any<string?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci => new ValueTask<IdempotentAdmission>(
                Admitted(ci.ArgAt<string>(0), ci.ArgAt<IdempotencyFingerprint>(1), isTakeover)
            ));
        operations
            .ReleaseAsync(Arg.Any<IdempotentAdmission>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<LeaseSettlementStatus>(LeaseSettlementStatus.Released));
        operations
            .RenewAsync(Arg.Any<IdempotentAdmission>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(
                new ValueTask<LeaseRenewalResult>(
                    new LeaseRenewalResult(LeaseRenewalStatus.Renewed, DateTimeOffset.UtcNow.AddMinutes(1))
                )
            );
        return operations;
    }

    /// <summary>Makes every admission return the given admissions in order (the last repeats).</summary>
    internal static void AdmitReturns(
        IIdempotentOperations operations,
        params Func<string, IdempotentAdmission>[] steps
    )
    {
        var index = 0;
        operations
            .AdmitAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyFingerprint>(),
                Arg.Any<string?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci =>
            {
                var step = steps[Math.Min(Interlocked.Increment(ref index) - 1, steps.Length - 1)];
                return new ValueTask<IdempotentAdmission>(step(ci.ArgAt<string>(0)));
            });
    }

    /// <summary>Makes every peek return the given statuses in order (the last repeats).</summary>
    internal static void PeekReturns(IIdempotentOperations operations, params IdempotencyPeekStatus[] steps)
    {
        var index = 0;
        operations
            .PeekAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var step = steps[Math.Min(Interlocked.Increment(ref index) - 1, steps.Length - 1)];
                return new ValueTask<IdempotencyPeekStatus>(step);
            });
    }

    internal static IdempotencyFingerprint FingerprintOf(byte[] body)
    {
        return IdempotencyFingerprint.Compute(SHA256.HashData(body));
    }

    internal static IdempotentAdmission Admitted(
        string key,
        IdempotencyFingerprint fingerprint,
        bool isTakeover = false
    )
    {
        return IdempotentAdmission.Admitted(
            new IdempotencyKey(TestTenant, key),
            fingerprint,
            new FencedLease(TestTenant, IdempotentAdmission.LeaseKind, key, 7),
            DateTimeOffset.UtcNow.AddMinutes(1),
            isTakeover,
            TimeSpan.FromHours(24)
        );
    }

    internal static IdempotentAdmission InFlight(string key)
    {
        return IdempotentAdmission.InFlight(
            new IdempotencyKey(TestTenant, key),
            IdempotencyFingerprint.Compute("any"),
            DateTimeOffset.UtcNow.AddMinutes(1)
        );
    }

    internal static IdempotentAdmission Replay(string key, IdempotencyResponseSnapshot snapshot)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            snapshot,
            IdempotencyJsonContext.Default.IdempotencyResponseSnapshot
        );

        return IdempotentAdmission.Replay(
            new IdempotencyKey(TestTenant, key),
            IdempotencyFingerprint.Compute("any"),
            new IdempotentResult(payload, IdempotencyResponseSnapshot.Contract)
        );
    }

    internal static IdempotentAdmission FingerprintConflict(string key)
    {
        return IdempotentAdmission.FingerprintConflict(
            new IdempotencyKey(TestTenant, key),
            IdempotencyFingerprint.Compute("mine"),
            IdempotencyFingerprint.Compute("stored")
        );
    }

    protected static DefaultHttpContext CreateContext(
        string? idempotencyKey = null,
        string method = "POST",
        string path = "/v1/test",
        byte[]? body = null
    )
    {
        var ctx = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider(),
        };
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        ctx.Request.Body = body is { Length: > 0 } ? new MemoryStream(body) : new MemoryStream();
        ctx.Response.Body = new MemoryStream();

        if (idempotencyKey is not null)
        {
            ctx.Request.Headers.Append(HttpHeaderNames.IdempotencyKey, idempotencyKey);
        }

        return ctx;
    }

    protected static async Task<string> ReadResponseAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    /// <summary>The idempotency keys the store was asked to admit, in call order.</summary>
    internal static List<string> AdmittedKeys(IIdempotentOperations operations)
    {
        return operations
            .ReceivedCalls()
            .Where(c =>
                string.Equals(
                    c.GetMethodInfo().Name,
                    nameof(IIdempotentOperations.AdmitAsync),
                    StringComparison.Ordinal
                )
            )
            .Select(c => (string)c.GetArguments()[0]!)
            .ToList();
    }

    internal static int CallCount(IIdempotentOperations operations, string method)
    {
        return operations
            .ReceivedCalls()
            .Count(c => string.Equals(c.GetMethodInfo().Name, method, StringComparison.Ordinal));
    }
}
