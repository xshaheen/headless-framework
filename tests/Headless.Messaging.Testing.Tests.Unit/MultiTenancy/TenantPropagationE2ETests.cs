// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Abstractions;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.MultiTenancy;
using Headless.Messaging.Testing;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tests.MultiTenancy;

// ─── Message types & consumers ───────────────────────────────────────────────

public sealed record TenantOrderEvent(string OrderId);

/// <summary>
/// Captures both the typed <see cref="ConsumeContext{T}.TenantId"/> and the ambient
/// <see cref="ICurrentTenant.Id"/> observed inside the consumer body. Used by E2E tests to
/// verify that tenant context is restored from the envelope before user code runs.
/// </summary>
public sealed class TenantCapturingConsumer(ICurrentTenant currentTenant, TenantCapture capture)
    : IConsume<TenantOrderEvent>
{
    public ValueTask Consume(ConsumeContext<TenantOrderEvent> context, CancellationToken ct)
    {
        capture.Record(context.Message.OrderId, context.TenantId, currentTenant.Id);
        return ValueTask.CompletedTask;
    }
}

public sealed record TenantOrderUpstream(string OrderId);

/// <summary>
/// Receives <see cref="TenantOrderUpstream"/> on one topic and re-publishes a derived
/// <see cref="TenantOrderEvent"/> from inside the consume scope so we can verify that the
/// publish-filter picks up the restored ambient tenant on the consume side.
/// </summary>
public sealed class ChainedRepublishConsumer(IMessagePublisher publisher) : IConsume<TenantOrderUpstream>
{
    public async ValueTask Consume(ConsumeContext<TenantOrderUpstream> context, CancellationToken ct)
    {
        await publisher.PublishAsync(new TenantOrderEvent($"chained-{context.Message.OrderId}"), ct);
    }
}

/// <summary>Throws on the first invocation per OrderId, succeeds on the second — and records both observations.</summary>
public sealed class FlakyTenantConsumer(ICurrentTenant currentTenant, TenantCapture capture)
    : IConsume<TenantOrderEvent>
{
    private readonly ConcurrentDictionary<string, int> _attempts = new(StringComparer.Ordinal);

    public ValueTask Consume(ConsumeContext<TenantOrderEvent> context, CancellationToken ct)
    {
        var attempt = _attempts.AddOrUpdate(context.Message.OrderId, 1, (_, prev) => prev + 1);
        capture.Record(context.Message.OrderId, context.TenantId, currentTenant.Id);
        if (attempt == 1)
        {
            throw new InvalidOperationException("first attempt fails");
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Concurrency-safe observation store keyed by <c>OrderId</c>. Tests use this to read what the
/// consumer body actually saw — ambient tenant + envelope tenant — without relying on
/// in-flight harness state.
/// </summary>
public sealed class TenantCapture
{
    private readonly ConcurrentBag<(string OrderId, string? EnvelopeTenant, string? AmbientTenant)> _records = [];

    public void Record(string orderId, string? envelopeTenant, string? ambientTenant)
    {
        _records.Add((orderId, envelopeTenant, ambientTenant));
    }

    public IReadOnlyCollection<(string OrderId, string? EnvelopeTenant, string? AmbientTenant)> Records =>
        _records.ToArray();
}

// ─── Tests ────────────────────────────────────────────────────────────────────

/// <summary>
/// End-to-end coverage for tenant propagation through <see cref="MessagingTestHarness"/>.
/// Maps to origin Acceptance Examples AE1-AE8.
/// </summary>
public sealed class TenantPropagationE2ETests : TestBase
{
    private static Task<MessagingTestHarness> _CreateHarnessAsync(
        TenantCapture capture,
        Action<MessagingOptions> configureMessaging
    )
    {
        return MessagingTestHarness.CreateAsync(services =>
        {
            // Real ICurrentTenant + AsyncLocal accessor — required for ambient propagation
            // and for cross-thread isolation under concurrent consumers.
            services.TryAddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
            services.TryAddSingleton<ICurrentTenant, CurrentTenant>();
            services.AddSingleton(capture);
            services.AddTransient<TenantCapturingConsumer>();
            services.AddSingleton<FlakyTenantConsumer>();

            services.AddHeadlessMessaging(options =>
            {
                options.UseInMemoryMessageQueue();
                options.UseInMemoryStorage();
                configureMessaging(options);
            });
            services.AddTenantPropagationServices();
        });
    }

    // ─── AE1 + AE3 — round-trip ──────────────────────────────────────────────

    [Fact]
    public async Task should_propagate_ambient_tenant_through_publish_and_restore_on_consume()
    {
        // given — Covers AE1, AE3
        var capture = new TenantCapture();
        await using var harness = await _CreateHarnessAsync(
            capture,
            options => options.Subscribe<TenantCapturingConsumer>("tenant-orders")
        );
        var currentTenant = harness.ServiceProvider.GetRequiredService<ICurrentTenant>();

        // when — publish under ambient tenant "acme"
        using (currentTenant.Change("acme"))
        {
            await harness.Publisher.PublishAsync(new TenantOrderEvent("ORD-1"), AbortToken);
        }

        await harness.WaitForConsumed<TenantOrderEvent>(TimeSpan.FromSeconds(5), AbortToken);

        // then — consumer body observed the ambient tenant via both the envelope and ICurrentTenant
        var record = capture.Records.Single(r => r.OrderId == "ORD-1");
        record.EnvelopeTenant.Should().Be("acme");
        record.AmbientTenant.Should().Be("acme");

        // and — restored after consume completes (no lingering ambient outside the using block)
        currentTenant.Id.Should().BeNull();
    }

    // ─── AE2 + AE5 + AE7 — system message (no ambient, no caller-set) ────────

    [Fact]
    public async Task should_treat_publish_without_ambient_tenant_as_system_message()
    {
        // given — Covers AE2, AE5, AE7
        var capture = new TenantCapture();
        await using var harness = await _CreateHarnessAsync(
            capture,
            options => options.Subscribe<TenantCapturingConsumer>("tenant-orders")
        );

        // when — no ambient tenant; publish without explicit options
        await harness.Publisher.PublishAsync(new TenantOrderEvent("ORD-SYS"), AbortToken);
        await harness.WaitForConsumed<TenantOrderEvent>(TimeSpan.FromSeconds(5), AbortToken);

        // then — envelope carries no tenant; consumer observes no ambient tenant
        var record = capture.Records.Single(r => r.OrderId == "ORD-SYS");
        record.EnvelopeTenant.Should().BeNull();
        record.AmbientTenant.Should().BeNull();
    }

    // ─── AE4 — caller override preserved ─────────────────────────────────────

    [Fact]
    public async Task should_preserve_explicit_caller_tenant_id_over_ambient()
    {
        // given — Covers AE4
        var capture = new TenantCapture();
        await using var harness = await _CreateHarnessAsync(
            capture,
            options => options.Subscribe<TenantCapturingConsumer>("tenant-orders")
        );
        var currentTenant = harness.ServiceProvider.GetRequiredService<ICurrentTenant>();

        // when — ambient is "acme" but caller explicitly sets PublishOptions.TenantId = "system"
        using (currentTenant.Change("acme"))
        {
            await harness.Publisher.PublishAsync(
                new TenantOrderEvent("ORD-OVR"),
                new PublishOptions { TenantId = "system" },
                AbortToken
            );
        }

        await harness.WaitForConsumed<TenantOrderEvent>(TimeSpan.FromSeconds(5), AbortToken);

        // then — explicit value wins over ambient
        var record = capture.Records.Single(r => r.OrderId == "ORD-OVR");
        record.EnvelopeTenant.Should().Be("system");
        record.AmbientTenant.Should().Be("system");
    }

    // ─── AE6 — exception path preserves envelope tenant ──────────────────────

    [Fact]
    public async Task should_preserve_envelope_tenant_when_consumer_throws()
    {
        // given — Covers AE6 (the per-attempt invariant: each invocation sees the envelope tenant)
        var capture = new TenantCapture();
        await using var harness = await _CreateHarnessAsync(
            capture,
            options => options.Subscribe<FlakyTenantConsumer>("tenant-orders")
        );
        var currentTenant = harness.ServiceProvider.GetRequiredService<ICurrentTenant>();

        // when — first attempt throws; the harness records it as faulted
        using (currentTenant.Change("acme"))
        {
            await harness.Publisher.PublishAsync(new TenantOrderEvent("ORD-RETRY"), AbortToken);
        }

        await harness.WaitForFaulted<TenantOrderEvent>(TimeSpan.FromSeconds(5), AbortToken);

        // and — wait for the second (successful) attempt so we can assert that the retry
        //       observed the same tenant context, not just the first faulted invocation.
        await harness.WaitForConsumed<TenantOrderEvent>(
            msg => msg.OrderId == "ORD-RETRY",
            TimeSpan.FromSeconds(10),
            AbortToken
        );

        // then — both attempts (faulted + successful) saw the envelope tenant. AE6 isolation
        //         covers the retry path: tenant context is rebuilt from the envelope on each
        //         invocation, never reused from a previous attempt's residue.
        var retryRecords = capture.Records.Where(r => r.OrderId == "ORD-RETRY").ToArray();
        retryRecords.Should().HaveCount(2);
        retryRecords
            .Should()
            .AllSatisfy(record =>
            {
                record.EnvelopeTenant.Should().Be("acme");
                record.AmbientTenant.Should().Be("acme");
            });

        // and — ambient is restored (filter disposed on exception path; no leaked AsyncLocal)
        currentTenant.Id.Should().BeNull();
    }

    // ─── AE8 — concurrent isolation ──────────────────────────────────────────

    [Fact]
    public async Task should_isolate_tenants_across_concurrent_messages()
    {
        // given — Covers AE8 (AsyncLocal isolation under parallel dispatch)
        var capture = new TenantCapture();
        await using var harness = await _CreateHarnessAsync(
            capture,
            options =>
            {
                options.Subscribe<TenantCapturingConsumer>("tenant-orders");
                // Allow parallel subscriber execution to actually exercise concurrent dispatch
                options.EnableSubscriberParallelExecute = true;
            }
        );
        var currentTenant = harness.ServiceProvider.GetRequiredService<ICurrentTenant>();

        // when — fan out N concurrent publishes, each under its own ambient tenant
        var tenants = Enumerable.Range(0, 20).Select(i => $"tenant-{i}").ToArray();
        var publishTasks = tenants.Select(tenantId =>
            Task.Run(
                async () =>
                {
                    using (currentTenant.Change(tenantId))
                    {
                        await harness.Publisher.PublishAsync(new TenantOrderEvent(tenantId), AbortToken);
                    }
                },
                AbortToken
            )
        );
        await Task.WhenAll(publishTasks);

        // and — wait for all to be consumed
        foreach (var tenantId in tenants)
        {
            await harness.WaitForConsumed<TenantOrderEvent>(
                msg => msg.OrderId == tenantId,
                TimeSpan.FromSeconds(10),
                AbortToken
            );
        }

        // then — each consumer observation matched its own tenant; no cross-talk
        foreach (var tenantId in tenants)
        {
            var record = capture.Records.Single(r => r.OrderId == tenantId);
            record.EnvelopeTenant.Should().Be(tenantId);
            record.AmbientTenant.Should().Be(tenantId);
        }

        // and — ambient is restored on the publishing thread
        currentTenant.Id.Should().BeNull();
    }

    // ─── Chained propagation: consume → re-publish → consume ────────────────

    [Fact]
    public async Task should_propagate_tenant_through_chained_publishes()
    {
        // given — Consumer A re-publishes from inside its consume scope; Consumer B captures.
        // The chain proves that the consume filter restored ICurrentTenant *before* user code
        // (and the inner publish filter), so the ambient tenant is stamped onto the second hop.
        var capture = new TenantCapture();
        await using var harness = await _CreateHarnessAsync(
            capture,
            options =>
            {
                options.Subscribe<ChainedRepublishConsumer>("upstream-orders");
                options.Subscribe<TenantCapturingConsumer>("tenant-orders");
            }
        );
        var currentTenant = harness.ServiceProvider.GetRequiredService<ICurrentTenant>();

        // when — kick off the chain under ambient "tenant-x"
        using (currentTenant.Change("tenant-x"))
        {
            await harness.Publisher.PublishAsync(new TenantOrderUpstream("HOP-1"), AbortToken);
        }

        await harness.WaitForConsumed<TenantOrderEvent>(
            msg => msg.OrderId == "chained-HOP-1",
            TimeSpan.FromSeconds(10),
            AbortToken
        );

        // then — the second consumer observed the same tenant the chain started with
        var record = capture.Records.Single(r => r.OrderId == "chained-HOP-1");
        record.EnvelopeTenant.Should().Be("tenant-x");
        record.AmbientTenant.Should().Be("tenant-x");
    }

    // ─── Integration: outbox publish path ─────────────────────────────────────

    [Fact]
    public async Task should_propagate_ambient_tenant_through_outbox_publisher()
    {
        // given — IOutboxPublisher routes through the same publish pipeline (and filter chain)
        var capture = new TenantCapture();
        await using var harness = await _CreateHarnessAsync(
            capture,
            options => options.Subscribe<TenantCapturingConsumer>("tenant-orders")
        );
        var currentTenant = harness.ServiceProvider.GetRequiredService<ICurrentTenant>();
        var outbox = harness.ServiceProvider.GetRequiredService<IOutboxPublisher>();

        // when — publish via outbox under ambient tenant
        using (currentTenant.Change("globex"))
        {
            await outbox.PublishAsync(new TenantOrderEvent("ORD-OUTBOX"), AbortToken);
        }

        await harness.WaitForConsumed<TenantOrderEvent>(TimeSpan.FromSeconds(10), AbortToken);

        // then — envelope and ambient both reflect the publishing tenant on the consume side
        var record = capture.Records.Single(r => r.OrderId == "ORD-OUTBOX");
        record.EnvelopeTenant.Should().Be("globex");
        record.AmbientTenant.Should().Be("globex");
    }
}
