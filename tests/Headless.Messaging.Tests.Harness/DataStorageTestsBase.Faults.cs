// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;
using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tests.Guarantees;
using Tests.Toxiproxy;

namespace Tests;

/// <summary>
/// How the storage behaves when its database fails, and on which registration path it runs. Each fault is the real
/// driver's, raised by cutting the network between the storage and a running database.
/// </summary>
public abstract partial class DataStorageTestsBase
{
    [Fact]
    [Guarantee("MSG-CB-06")]
    public async Task should_open_the_consumer_circuit_when_the_message_store_becomes_unreachable()
    {
        MessagingGuarantees.RequireSupport("MSG-CB-06");

        if (DatabaseHostPort is not { } databaseHostPort)
        {
            Assert.Skip("This storage has no network connection a proxy can cut.");
            return;
        }

        // given: a consumer whose admission writes reach the database through a route, and whose circuit opens at
        // three transient failures under the default classifier
        const string consumer = "store-outage-group";
        const int threshold = 3;
        await using var route = await ToxiproxyDatabaseRoute.StartAsync(databaseHostPort, AbortToken);
        var storage = CreateStorageThrough(route.Proxy.Host, route.Proxy.Port);
        await storage.StoreReceivedMessageAsync("store-outage", consumer, CreateMessage(), AbortToken);
        await using var metrics = new ServiceCollection().AddMetrics().BuildServiceProvider();
        await using var circuit = new CircuitBreakerStateManager(
            Options.Create(new CircuitBreakerOptions { FailureThreshold = threshold }),
            new ConsumerCircuitBreakerRegistry(),
            NullLogger<CircuitBreakerStateManager>.Instance,
            new CircuitBreakerMetrics(metrics.GetRequiredService<IMeterFactory>()),
            TimeProvider.System
        );

        // when: the database becomes unreachable and that many deliveries cannot be taken in; the consumer register
        // reports each failed admission write the same way
        await route.Proxy.DisableAsync(AbortToken);
        var failures = new List<Exception>();
        for (var delivery = 0; delivery < threshold; delivery++)
        {
            var failure = await Record.ExceptionAsync(async () =>
                await storage.StoreReceivedMessageAsync("store-outage", consumer, CreateMessage(), AbortToken)
            );
            failure.Should().NotBeNull("the database cannot be reached");
            failures.Add(failure!);
            await circuit.ReportPreHandlerFailureAsync(consumer, failure!, probeEpoch: 0, CancellationToken.None);
        }

        // then: the driver's outage faults count as transient, so the circuit is open
        circuit
            .IsOpen(consumer)
            .Should()
            .BeTrue(
                "an unreachable store must open the circuit; the driver reported: {0}",
                string.Join(" | ", failures.Select(failure => $"{failure.GetType().FullName}: {failure.Message}"))
            );
    }

    [Fact]
    [Guarantee("MSG-STORE-05")]
    public async Task should_store_and_reclaim_through_storage_registered_from_an_entity_framework_context()
    {
        if (!RegisteredFromEntityFrameworkContext)
        {
            Assert.Skip(
                "This leaf registers its storage from its own connection settings; the EF Core leaves run this test."
            );
        }

        // given: storage whose connection settings came from an EF Core context, as every other test in this suite on
        // this leaf uses it
        var storage = GetStorage();
        NodeMembership.SetIdentity("entity-framework-owner");

        // when: a published and a received row are stored, failed for retry, and picked up
        var published = await _StoreRetryDueAsync(storage, published: true, "entity-framework-published");
        var received = await _StoreRetryDueAsync(storage, published: false, "entity-framework-received");

        // then: both come back to the retry processor
        (await _PickUpRetriesAsync(storage, published: true))
            .Should()
            .Contain(m => m.StorageId == published.StorageId);
        (await _PickUpRetriesAsync(storage, published: false)).Should().Contain(m => m.StorageId == received.StorageId);
    }
}
