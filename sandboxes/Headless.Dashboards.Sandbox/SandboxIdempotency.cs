// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Checks;
using Headless.Idempotency;
using Headless.MultiTenancy;

namespace Headless.Dashboards.Sandbox;

/// <summary>The fixture that admits idempotency keys in every state the Idempotency module names.</summary>
public static class SandboxIdempotency
{
    public const string Tenant = "acme";
    public const string ResultContract = "orders.create/v1";
    public const string RecoveryContract = "payments.recovery/v1";

    private static readonly TimeSpan _LiveLease = TimeSpan.FromMinutes(10);

    // The minimum lease the store accepts, so the stalled key reaches its state a second after the call.
    private static readonly TimeSpan _StallingLease = TimeSpan.FromSeconds(1);

    // Shorter than the live lease, so the record outlives its retention while its attempt still owns it.
    private static readonly TimeSpan _ShortRetention = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Admits four new keys: one completed under the tenant <c>acme</c>, one in flight with a recovery point, one that
    /// stalls once its one-second lease ends, and one in flight past its one-second retention. Each call makes new keys,
    /// so it is safe to run again.
    /// </summary>
    /// <returns>Each key with its tenant, the state the screen names, and when it reaches that state, one line each.</returns>
    public static async Task<IReadOnlyList<string>> SeedAsync(
        IServiceProvider provider,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(provider);

        var operations = provider.GetRequiredService<IIdempotentOperations>();
        var currentTenant = provider.GetRequiredService<ICurrentTenant>();
        var clock = provider.GetRequiredService<TimeProvider>();
        var suffix = Guid.CreateVersion7().ToString("N")[^8..];

        var completed = $"order-{suffix}-create";
        var inFlight = $"order-{suffix}-charge";
        var stalled = $"order-{suffix}-refund";
        var pastRetention = $"order-{suffix}-invoice";

        using (currentTenant.Change(Tenant))
        {
            var admission = await operations
                .AdmitAsync(completed, _Fingerprint(completed), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var result = Encoding.UTF8.GetBytes($$"""{"orderId":"{{suffix}}","status":"created"}""");
            await operations
                .CompleteAsync(admission, result, ResultContract, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        using (currentTenant.Change(id: null))
        {
            var charging = await operations
                .AdmitAsync(
                    inFlight,
                    _Fingerprint(inFlight),
                    leaseDuration: _LiveLease,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
            await operations
                .SetRecoveryPointAsync(
                    charging,
                    "payment-authorized",
                    """{"authorization":"sandbox"}"""u8.ToArray(),
                    RecoveryContract,
                    cancellationToken
                )
                .ConfigureAwait(false);

            await operations
                .AdmitAsync(
                    stalled,
                    _Fingerprint(stalled),
                    leaseDuration: _StallingLease,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            await operations
                .AdmitAsync(
                    pastRetention,
                    _Fingerprint(pastRetention),
                    leaseDuration: _LiveLease,
                    retention: _ShortRetention,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
        }

        var now = clock.GetUtcNow();

        return
        [
            $"{completed} tenant {Tenant} Completed from {now:O}",
            $"{inFlight} host scope In flight until {now + _LiveLease:O}",
            $"{stalled} host scope Stalled from {now + _StallingLease:O}",
            $"{pastRetention} host scope In flight past retention from {now + _ShortRetention:O} until {now + _LiveLease:O}",
        ];
    }

    private static IdempotencyFingerprint _Fingerprint(string key) => IdempotencyFingerprint.Compute(key);
}
