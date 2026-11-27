// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text;
using Headless.DistributedLocks;
using Headless.Fencing;
using Headless.MultiTenancy;

namespace Headless.Dashboards.Sandbox;

/// <summary>
/// Fenced leases in every state and held distributed locks, written through the framework's own APIs so the Leases
/// module's two views have something to show. Each call suffixes its resources, so a second run adds rows instead of
/// meeting its own leases and locks.
/// </summary>
/// <remarks>
/// Fencing and the locks run in memory in both store modes: the in-memory fencing store needs no provisioning, and the
/// in-memory lock provider lists only this host's handles, which is the case the locks view's scope note describes.
/// </remarks>
public static class SandboxLeases
{
    // The shortest lease Fencing grants by default; a lease granted for it expires while the scenario runs.
    private static readonly TimeSpan _Short = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _Long = TimeSpan.FromMinutes(30);

    // Long enough for a one-second lease to pass its expiry on the store's clock.
    private static readonly TimeSpan _PastExpiry = TimeSpan.FromMilliseconds(1500);

    private static readonly TimeSpan _LockTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan _LockRenewEvery = TimeSpan.FromSeconds(30);

    public static async Task<IReadOnlyList<string>> SeedLeasesAsync(
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        var leases = services.GetRequiredService<IFencedLeases>();
        var tenant = services.GetRequiredService<ICurrentTenant>();
        var suffix = _Suffix(services.GetRequiredService<TimeProvider>());
        // A set: the lease taken over after expiry is granted twice.
        var created = new SortedSet<string>(StringComparer.Ordinal);

        async Task<FencedLease> grantAsync(
            string? tenantId,
            string kind,
            string resource,
            TimeSpan duration,
            string? holder
        )
        {
            using (tenant.Change(tenantId))
            {
                var result = await leases.GrantAsync(
                    kind,
                    $"{resource}-{suffix}",
                    duration,
                    new LeaseGrantOptions { Holder = holder },
                    cancellationToken
                );

                if (!result.IsAcquired)
                {
                    throw new InvalidOperationException($"The lease {kind}/{resource}-{suffix} was not granted.");
                }

                created.Add($"{tenantId ?? "host"}/{kind}/{resource}-{suffix}");

                return result.Lease;
            }
        }

        // Live leases across the host scope and two tenants, some with a holder and some with recorded progress.
        var export = await grantAsync(tenantId: null, "ledger-export", "export/2026-11-03", _Long, holder: null);
        await leases.RenewAsync(
            export,
            _Long,
            new LeaseProgress(Encoding.UTF8.GetBytes(new string('c', 1229)), "export-cursor/v1"),
            cancellationToken
        );
        await grantAsync("acme", "ledger-export", "export/2026-11-04", _Long, holder: "orders-api-0@14");
        var invoices = await grantAsync("acme", "invoice-batch", "invoices/42", _Long, holder: "billing-worker-1@3");
        await leases.RenewAsync(
            invoices,
            _Long,
            new LeaseProgress(Encoding.UTF8.GetBytes("{\"page\":17}"), "invoice-page/v2"),
            cancellationToken
        );
        await grantAsync("globex", "search-reindex", "catalog/shoes", _Long, holder: "search-worker-0@9");

        // Two leases left to expire, waiting for a takeover or a sweep.
        await grantAsync("globex", "invoice-batch", "invoices/43", _Short, holder: "billing-worker-2@1");
        await grantAsync(tenantId: null, "ledger-export", "export/2026-11-02", _Short, holder: null);

        // One settled, one released, and one revoked lease.
        await leases.SettleAsync(
            await grantAsync("acme", "ledger-export", "export/2026-11-01", _Long, holder: "orders-api-0@14"),
            cancellationToken
        );
        await leases.ReleaseAsync(
            await grantAsync(tenantId: null, "invoice-batch", "invoices/41", _Long, holder: null),
            cancellationToken
        );
        await grantAsync("globex", "search-reindex", "catalog/hats", _Long, holder: "search-worker-1@2");

        using (tenant.Change("globex"))
        {
            await leases.RevokeAsync("search-reindex", $"catalog/hats-{suffix}", cancellationToken);
        }

        // One lease taken over after it expired, and one the sweep abandons after its expiry.
        await grantAsync(tenantId: null, "ledger-export", "export/2026-10-31", _Short, holder: "orders-api-1@2");
        await grantAsync(tenantId: null, "search-reindex", "catalog/socks", _Short, holder: "search-worker-2@4");
        // A sandbox wait for the store's clock, not a framework delay: the in-memory store reads the system clock.
        await Task.Delay(_PastExpiry, cancellationToken);
        await grantAsync(tenantId: null, "ledger-export", "export/2026-10-31", _Long, holder: "orders-api-0@14");
        await leases.SweepExpiredAsync(
            "search-reindex",
            static (_, _, _) => ValueTask.CompletedTask,
            limit: 10,
            cancellationToken
        );

        return [.. created];
    }

    public static async Task<IReadOnlyList<string>> HoldLocksAsync(
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        var locks = services.GetRequiredService<IDistributedLock>();
        var lifetime = services.GetRequiredService<IHostApplicationLifetime>();
        var suffix = _Suffix(services.GetRequiredService<TimeProvider>());
        var held = new List<IDistributedLease>();

        foreach (var resource in new[] { "orders:rebuild", "reports:daily", "invoices:close" })
        {
            held.Add(
                await locks.AcquireAsync(
                    $"{resource}-{suffix}",
                    new DistributedLockAcquireOptions { TimeUntilExpires = _LockTtl, ReleaseOnDispose = false },
                    cancellationToken
                )
            );
        }

        var forever = await locks.AcquireAsync(
            $"migrations:apply-{suffix}",
            new DistributedLockAcquireOptions { TimeUntilExpires = Timeout.InfiniteTimeSpan, ReleaseOnDispose = false },
            cancellationToken
        );

        // The sandbox's own task, stopped with the host: it renews the TTL locks so they stay listed.
        _ = Task.Run(() => _RenewAsync(held, lifetime.ApplicationStopping), CancellationToken.None);

        return [.. held.Select(static lease => lease.Resource), forever.Resource];
    }

    private static async Task _RenewAsync(IReadOnlyList<IDistributedLease> held, CancellationToken stopping)
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                // A sandbox renewal cadence, real time by design: the provider's TTL runs on the system clock.
                await Task.Delay(_LockRenewEvery, stopping);

                foreach (var lease in held)
                {
                    await lease.RenewAsync(_LockTtl, stopping);
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // The host is stopping; the locks go with the process.
        }
    }

    private static string _Suffix(TimeProvider clock) =>
        clock.GetUtcNow().ToUnixTimeMilliseconds().ToString("x", CultureInfo.InvariantCulture)[^6..];
}
