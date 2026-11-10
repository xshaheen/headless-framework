// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Hosting;
using Headless.Hosting.Schema;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Messaging.Processor;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tests.Guarantees;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// One outbox per database, against a real provider: a unit begun on an additional outbox's database commits its
/// row there and the relay delivers it, a rollback leaves no row anywhere, a unit on an unregistered database is
/// refused, two outboxes on one database fail startup, one database's outage does not stop the others' relay, and
/// an outbox whose database is down at startup does not stop the host and works once its database is back. The
/// host's deploy script also creates an outbox's tables on an empty database.
/// </summary>
/// <remarks>Each test creates its own databases, so the primary and the outboxes never share one by accident.</remarks>
public abstract class AdditionalOutboxConformanceTests : TestBase
{
    private static readonly TimeSpan _DeliveryTimeout = TimeSpan.FromSeconds(45);

    private readonly List<string> _databases = [];

    /// <summary>Creates an empty database and returns a connection string to it.</summary>
    protected abstract Task<string> CreateDatabaseAsync(string name);

    /// <summary>
    /// Returns the name of a new database for <paramref name="role"/>. Override it when the fixture owns the names
    /// of its databases, so a database a test creates is one the fixture also drops.
    /// </summary>
    protected virtual string NewDatabaseName(string role) => $"{role}_{Guid.NewGuid():N}"[..24];

    /// <summary>Drops a database created by <see cref="CreateDatabaseAsync"/>, closing its open sessions.</summary>
    protected abstract Task DropDatabaseAsync(string name);

    protected abstract void UseDatabase(DbContextOptionsBuilder options, string connectionString);

    protected abstract void UsePrimaryStorage<TContext>(MessagingSetupBuilder setup)
        where TContext : DbContext;

    protected abstract MessagingSetupBuilder UseOutboxStorage<TContext>(OutboxStorageBuilder outbox)
        where TContext : DbContext;

    protected abstract MessagingSetupBuilder UseOutboxStorage(OutboxStorageBuilder outbox, string connectionString);

    /// <summary>Reads every published row of the messaging schema in the database.</summary>
    protected abstract Task<IReadOnlyList<PublishedRow>> ReadPublishedAsync(string connectionString);

    /// <summary>Whether the messaging schema of the database has a received (inbox) table.</summary>
    protected abstract Task<bool> HasReceivedTableAsync(string connectionString);

    /// <summary>Runs an exported deploy script against the database, batch by batch where the dialect separates them.</summary>
    protected abstract Task ExecuteScriptAsync(string connectionString, string script);

    protected override async ValueTask DisposeAsyncCore()
    {
        // DropDatabaseAsync is idempotent, so a database a test already dropped is skipped.
        foreach (var database in _databases)
        {
            await DropDatabaseAsync(database);
        }

        await base.DisposeAsyncCore();
    }

    [Fact]
    [Guarantee("MSG-PUB-01")]
    [Guarantee("MSG-PUB-03")]
    public async Task should_commit_the_row_in_the_units_database_and_relay_it_after_commit()
    {
        // given
        var orders = await _CreateDatabaseAsync("orders");
        var billing = await _CreateDatabaseAsync("billing");
        using var host = _BuildHost(
            orders,
            billing,
            setup => UseOutboxStorage<BillingOutboxDbContext>(setup.AddOutbox("billing"))
        );
        await _StartAsync(host);
        var marker = $"billing-{Guid.NewGuid():N}";

        // when
        await _PublishAsync<BillingOutboxDbContext>(host.Services, marker);

        // then
        await host.Services.GetRequiredService<ProbeInbox>().WaitAsync(marker, _DeliveryTimeout, AbortToken);
        await _WaitForStatusAsync(billing.ConnectionString, marker, "Succeeded");
        (await ReadPublishedAsync(orders.ConnectionString)).Should().NotContain(row => row.Contains(marker));
        (await HasReceivedTableAsync(billing.ConnectionString)).Should().BeFalse("an additional outbox has no inbox");
        (await HasReceivedTableAsync(orders.ConnectionString)).Should().BeTrue();
        await host.StopAsync(AbortToken);
    }

    [Fact]
    [Guarantee("MSG-PUB-01")]
    public async Task should_leave_no_row_in_any_database_when_the_unit_rolls_back()
    {
        // given
        var orders = await _CreateDatabaseAsync("orders");
        var billing = await _CreateDatabaseAsync("billing");
        using var host = _BuildHost(
            orders,
            billing,
            setup => UseOutboxStorage<BillingOutboxDbContext>(setup.AddOutbox("billing"))
        );
        await _StartAsync(host);
        var marker = $"rollback-{Guid.NewGuid():N}";
        var sentinel = new InvalidOperationException("roll the unit back");

        // when
        var services = host.Services;
        var act = () => _PublishAsync<BillingOutboxDbContext>(services, marker, afterPublish: () => throw sentinel);

        // then
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Should()
            .BeSameAs(sentinel);
        (await ReadPublishedAsync(billing.ConnectionString)).Should().NotContain(row => row.Contains(marker));
        (await ReadPublishedAsync(orders.ConnectionString)).Should().NotContain(row => row.Contains(marker));
        await host.StopAsync(AbortToken);
    }

    [Fact]
    [Guarantee("MSG-PUB-05")]
    public async Task should_refuse_a_unit_on_a_database_without_an_outbox()
    {
        // given
        var orders = await _CreateDatabaseAsync("orders");
        var billing = await _CreateDatabaseAsync("billing");
        var shipping = await _CreateDatabaseAsync("shipping");
        using var host = _BuildHost(
            orders,
            billing,
            setup => UseOutboxStorage<BillingOutboxDbContext>(setup.AddOutbox("billing")),
            services =>
                services.AddDbContext<ShippingOutboxDbContext>(options =>
                    UseDatabase(options, shipping.ConnectionString)
                )
        );
        await _StartAsync(host);

        // when
        var services = host.Services;
        var act = () => _PublishAsync<ShippingOutboxDbContext>(services, "shipping");

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*cannot join the active unit of work (Database)*");
        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_fail_startup_when_two_outboxes_resolve_to_the_same_database()
    {
        // given
        var orders = await _CreateDatabaseAsync("orders");
        var billing = await _CreateDatabaseAsync("billing");
        using var host = _BuildHost(
            orders,
            billing,
            setup =>
            {
                UseOutboxStorage<BillingOutboxDbContext>(setup.AddOutbox("billing"));
                UseOutboxStorage(setup.AddOutbox("billing-direct"), billing.ConnectionString);
            }
        );

        // when
        var act = () => host.StartAsync(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<MessagingConfigurationException>()
            .WithMessage("*'billing' and 'billing-direct' both resolve to database*");
    }

    [Fact]
    [Guarantee("MSG-PUB-06")]
    public async Task should_keep_relaying_the_other_outboxes_while_one_database_is_down()
    {
        // given — rows committed while the dispatcher is stopped, so only the relay can deliver them
        var orders = await _CreateDatabaseAsync("orders");
        var billing = await _CreateDatabaseAsync("billing");
        var shipping = await _CreateDatabaseAsync("shipping");
        using var host = _BuildHost(
            orders,
            billing,
            setup =>
            {
                UseOutboxStorage<BillingOutboxDbContext>(setup.AddOutbox("billing"));
                UseOutboxStorage<ShippingOutboxDbContext>(setup.AddOutbox("shipping"));
                setup.Options.RetryPolicy.InitialDispatchGrace = TimeSpan.FromSeconds(3);
                setup.Options.RetryProcessor.BaseInterval = TimeSpan.FromMilliseconds(500);
                setup.Options.RetryProcessor.AdaptivePolling = false;
            },
            services =>
                services.AddDbContext<ShippingOutboxDbContext>(options =>
                    UseDatabase(options, shipping.ConnectionString)
                )
        );
        await _InitializeAllOutboxesAsync(host);
        var billingMarker = $"billing-{Guid.NewGuid():N}";
        var ordersMarker = $"orders-{Guid.NewGuid():N}";
        await _PublishAsync<BillingOutboxDbContext>(host.Services, billingMarker);
        await _PublishAsync<OrdersOutboxDbContext>(host.Services, ordersMarker);

        // when — the shipping database goes away once the relay is running
        await _StartAsync(host);
        await DropDatabaseAsync(shipping.Name);

        // then
        var inbox = host.Services.GetRequiredService<ProbeInbox>();
        await inbox.WaitAsync(billingMarker, _DeliveryTimeout, AbortToken);
        await inbox.WaitAsync(ordersMarker, _DeliveryTimeout, AbortToken);
        await _WaitForStatusAsync(billing.ConnectionString, billingMarker, "Succeeded");

        // Proof the shipping relay really polled the missing database meanwhile. A client can still be retrying
        // its login when the others deliver (SqlClient treats a missing database as transient), so wait for it.
        var retryProcessor = host.Services.GetRequiredService<MessageNeedToRetryProcessor>();
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken))
        {
            timeout.CancelAfter(_DeliveryTimeout);
            while (retryProcessor.GetPickupFailureCountForTest(MessageType.Publish, MessageLane.Bus, outbox: 2) == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), timeout.Token);
            }
        }

        await host.StopAsync(AbortToken);
    }

    [Fact]
    [Guarantee("MSG-PUB-06")]
    public async Task should_start_while_an_outbox_database_is_down_and_publish_to_it_once_it_is_back()
    {
        // given — the shipping database does not exist when the host starts
        var orders = await _CreateDatabaseAsync("orders");
        var billing = await _CreateDatabaseAsync("billing");
        var shipping = await _CreateDatabaseAsync("shipping");
        await DropDatabaseAsync(shipping.Name);
        using var host = _BuildHost(
            orders,
            billing,
            setup =>
            {
                UseOutboxStorage<BillingOutboxDbContext>(setup.AddOutbox("billing"));
                UseOutboxStorage<ShippingOutboxDbContext>(setup.AddOutbox("shipping"));
            },
            services =>
                services.AddDbContext<ShippingOutboxDbContext>(options =>
                    UseDatabase(options, shipping.ConnectionString)
                ),
            setupResources: false
        );

        // The deploy step reaches every database it can and names the one it cannot.
        await FluentActions
            .Awaiting(() => host.Services.ProvisionResourcesAsync(AbortToken))
            .Should()
            .ThrowAsync<ResourceProvisioningException>();

        // when
        await _StartAsync(host);

        // then — the host runs, and the other outboxes publish and relay
        var shippingOutbox = host.Services.GetRequiredService<MessagingOutboxes>().Secondaries[1];
        shippingOutbox.IsInitialized.Should().BeFalse();
        var inbox = host.Services.GetRequiredService<ProbeInbox>();
        var billingMarker = $"billing-{Guid.NewGuid():N}";
        await _PublishAsync<BillingOutboxDbContext>(host.Services, billingMarker);
        await inbox.WaitAsync(billingMarker, _DeliveryTimeout, AbortToken);

        // and when — the shipping database comes back, the deployment sets it up, and a unit on it publishes
        await CreateDatabaseAsync(shipping.Name);
        await host.Services.ProvisionResourcesAsync(AbortToken);
        var shippingMarker = $"shipping-{Guid.NewGuid():N}";
        await _PublishAsync<ShippingOutboxDbContext>(host.Services, shippingMarker);

        // then — the outbox is initialized (by the unit's write, or by the background retry if it ran first), and
        // the row is relayed from the shipping database
        shippingOutbox.IsInitialized.Should().BeTrue();
        await inbox.WaitAsync(shippingMarker, _DeliveryTimeout, AbortToken);
        await _WaitForStatusAsync(shipping.ConnectionString, shippingMarker, "Succeeded");
        (await HasReceivedTableAsync(shipping.ConnectionString)).Should().BeFalse("an additional outbox has no inbox");
        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_export_an_outbox_schema_that_creates_its_tables_on_an_empty_database()
    {
        // given — a host with an additional outbox whose database is still empty
        var orders = await _CreateDatabaseAsync("orders");
        var billing = await _CreateDatabaseAsync("billing");
        using var host = _BuildHost(
            orders,
            billing,
            setup => UseOutboxStorage<BillingOutboxDbContext>(setup.AddOutbox("billing")),
            setupResources: false
        );
        var runner = host.Services.GetRequiredService<SchemaRunner>();
        var outboxContribution = runner.Contributions.Single(c => c.ExportOnly);

        // when
        var script = runner.ExportScript(outboxContribution.Dialect);
        await ExecuteScriptAsync(billing.ConnectionString, script);

        // then — the script carries the outbox's steps, and the outbox's own runner finds them all applied
        script.Should().Contain($"-- {outboxContribution.Feature}/");
        (await ReadPublishedAsync(billing.ConnectionString)).Should().BeEmpty();
        var outbox = host.Services.GetRequiredService<MessagingOutboxes>().Secondaries.Single();
        await outbox.Initializer.InitializeAsync(AbortToken);
    }

    [Fact]
    public async Task should_sample_the_backlog_of_each_outbox_tagged_with_its_database()
    {
        // given — rows committed while the host is stopped, so none is relayed: one on the primary, two on billing
        var orders = await _CreateDatabaseAsync("orders");
        var billing = await _CreateDatabaseAsync("billing");
        using var host = _BuildHost(
            orders,
            billing,
            setup =>
            {
                UseOutboxStorage<BillingOutboxDbContext>(setup.AddOutbox("billing"));
                setup.Options.BacklogSamplingInterval = TimeSpan.FromMinutes(5);
            }
        );
        await _InitializeAllOutboxesAsync(host);
        await _PublishAsync<OrdersOutboxDbContext>(host.Services, $"orders-{Guid.NewGuid():N}");
        await _PublishAsync<BillingOutboxDbContext>(host.Services, $"billing-{Guid.NewGuid():N}");
        await _PublishAsync<BillingOutboxDbContext>(host.Services, $"billing-{Guid.NewGuid():N}");
        var sampler = host.Services.GetRequiredService<BacklogSamplingProcessor>();

        // when
        await sampler.SampleAsync(AbortToken);

        // then — the primary reports both of its tables, the additional outbox only its published one
        sampler
            .Measurements.Select(measurement =>
                (
                    Direction: tagValue(measurement, MessagingMetrics.TagPersistenceType),
                    Database: tagValue(measurement, MessagingMetrics.TagDbNamespace),
                    measurement.Pending
                )
            )
            .Should()
            .BeEquivalentTo([
                (MessagingMetrics.PersistenceTypePublish, orders.Name, 1L),
                (MessagingMetrics.PersistenceTypeConsume, orders.Name, 0L),
                (MessagingMetrics.PersistenceTypePublish, billing.Name, 2L),
            ]);

        static string? tagValue(BacklogMeasurement measurement, string key) =>
            measurement.Tags.SingleOrDefault(tag => string.Equals(tag.Key, key, StringComparison.Ordinal)).Value
            as string;
    }

    private IHost _BuildHost(
        TestDatabase primary,
        TestDatabase billing,
        Action<MessagingSetupBuilder> configureOutboxes,
        Action<IServiceCollection>? configureServices = null,
        bool setupResources = true
    )
    {
        var builder = Host.CreateApplicationBuilder();
        var services = builder.Services;
        services.AddLogging();

        if (setupResources)
        {
            services.AddDeployStepsOnStartup(o => o.Seed = false);
        }

        services.AddSingleton<ProbeInbox>();
        services.AddDbContext<OrdersOutboxDbContext>(options => UseDatabase(options, primary.ConnectionString));
        services.AddDbContext<BillingOutboxDbContext>(options => UseDatabase(options, billing.ConnectionString));
        configureServices?.Invoke(services);
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            UsePrimaryStorage<OrdersOutboxDbContext>(setup);
            configureOutboxes(setup);
            setup.AddModule<AdditionalOutboxTestsModule>();
        });
        services.ConfigureMessaging(messaging => messaging.Message<OutboxProbe>("tests.additional-outbox.probe"));

        return builder.Build();
    }

    [Fact]
    public async Task should_stamp_and_read_changes_of_a_row_through_its_outboxs_storage()
    {
        // given
        var orders = await _CreateDatabaseAsync("orders");
        var billing = await _CreateDatabaseAsync("billing");
        using var host = _BuildHost(
            orders,
            billing,
            setup => UseOutboxStorage<BillingOutboxDbContext>(setup.AddOutbox("billing"))
        );
        await _StartAsync(host);
        var outboxes = host.Services.GetRequiredService<MessagingOutboxes>();
        var billingStorage = outboxes.Secondaries.Single().Storage;
        var primaryStorage = outboxes.Primary!;
        var billingStart = await billingStorage.GetMessageChangeStartCursorAsync(AbortToken);
        var primaryStart = await primaryStorage.GetMessageChangeStartCursorAsync(AbortToken);
        var marker = $"changes-{Guid.NewGuid():N}";

        // when: a row is committed in the outbox's database and relayed to Succeeded
        await _PublishAsync<BillingOutboxDbContext>(host.Services, marker);
        await _WaitForStatusAsync(billing.ConnectionString, marker, "Succeeded");
        var billingChanges = await _ReadAllChangesAsync(billingStorage, billingStart);
        var primaryChanges = await _ReadAllChangesAsync(primaryStorage, primaryStart);

        // then: the outbox's storage returns the row's changes, ending with its terminal state; the primary does not
        var monitoring = billingStorage.GetMonitoringApi();
        Guid? rowId = null;
        foreach (var change in billingChanges.Where(c => c.Kind == MessageChangeKind.Published))
        {
            var row = await monitoring.GetPublishedMessageAsync(change.Id, AbortToken);
            if (row?.Content.Contains(marker, StringComparison.Ordinal) is true)
            {
                rowId = change.Id;
            }
        }

        rowId.Should().NotBeNull("the outbox's change read returns the row committed in its database");
        billingChanges.Should().NotContain(c => c.Kind == MessageChangeKind.Received, "an outbox has no inbox");
        billingChanges.Last(c => c.Id == rowId).Status.Should().Be(StatusName.Succeeded);
        primaryChanges.Should().NotContain(c => c.Id == rowId);
        await host.StopAsync(AbortToken);
    }

    // Reads changes until two reads in a row return a short page, so every row committed before them is seen.
    private static async Task<List<MessageChange>> _ReadAllChangesAsync(
        IDataStorage storage,
        MessageChangeCursor cursor
    )
    {
        var changes = new List<MessageChange>();
        var shortReads = 0;
        for (var read = 0; read < 100 && shortReads < 2; read++)
        {
            var page = await storage.GetMessageChangesAsync(
                cursor,
                MessageChangeCursor.DefaultOverlapWindow,
                500,
                AbortToken
            );
            changes.AddRange(page.Changes);
            cursor = page.Cursor;
            shortReads = page.Changes.Count < 500 ? shortReads + 1 : 0;
        }

        return changes;
    }

    // The bootstrapper is a background service, so the host's start returns before schema initialization ends;
    // joining its bootstrap waits for the primary and for one initialization attempt of every additional outbox.
    private async Task _StartAsync(IHost host)
    {
        await host.StartAsync(AbortToken);
        await host.Services.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
    }

    private async Task _InitializeAllOutboxesAsync(IHost host)
    {
        // The deploy step: creates the primary's and every additional outbox's tables, which the initializers verify.
        await host.Services.ProvisionResourcesAsync(AbortToken);
        foreach (var outbox in host.Services.GetRequiredService<MessagingOutboxes>().Secondaries)
        {
            await outbox.Initializer.InitializeAsync(AbortToken);
        }
    }

    private async Task _PublishAsync<TContext>(IServiceProvider services, string marker, Action? afterPublish = null)
        where TContext : DbContext
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var factory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();
        await factory.RunAsync(
            db,
            async (unit, ct) =>
            {
                await unit.Outbox.PublishAsync(new OutboxProbe(marker), ct);
                afterPublish?.Invoke();
            },
            cancellationToken: AbortToken
        );
    }

    private async Task _WaitForStatusAsync(string connectionString, string marker, string status)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        timeout.CancelAfter(_DeliveryTimeout);
        while (true)
        {
            var rows = await ReadPublishedAsync(connectionString);
            if (
                rows.Any(row => row.Contains(marker) && string.Equals(row.StatusName, status, StringComparison.Ordinal))
            )
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), timeout.Token);
        }
    }

    private async Task<TestDatabase> _CreateDatabaseAsync(string role)
    {
        var name = NewDatabaseName(role);
        var connectionString = await CreateDatabaseAsync(name);
        _databases.Add(name);

        return new TestDatabase(name, connectionString);
    }

    private sealed record TestDatabase(string Name, string ConnectionString);

    /// <summary>A published row as the conformance tests read it back.</summary>
    public sealed record PublishedRow(string? Content, string StatusName)
    {
        public bool Contains(string marker) => Content?.Contains(marker, StringComparison.Ordinal) is true;
    }

    public sealed record OutboxProbe(string Marker);

    public sealed class OutboxProbeConsumer(ProbeInbox inbox) : IConsume<OutboxProbe>
    {
        public ValueTask ConsumeAsync(ConsumeContext<OutboxProbe> context, CancellationToken cancellationToken)
        {
            inbox.Record(context.Message.Marker);

            return ValueTask.CompletedTask;
        }
    }

    public sealed class ProbeInbox
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _received = new(StringComparer.Ordinal);

        public void Record(string marker) => _Get(marker).TrySetResult();

        public Task WaitAsync(string marker, TimeSpan timeout, CancellationToken cancellationToken) =>
            _Get(marker).Task.WaitAsync(timeout, cancellationToken);

        private TaskCompletionSource _Get(string marker) =>
            _received.GetOrAdd(
                marker,
                static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
            );
    }

    /// <summary>The primary storage's database.</summary>
    public sealed class OrdersOutboxDbContext(DbContextOptions<OrdersOutboxDbContext> options) : DbContext(options);

    /// <summary>A database with an additional outbox.</summary>
    public sealed class BillingOutboxDbContext(DbContextOptions<BillingOutboxDbContext> options) : DbContext(options);

    /// <summary>A database with or without an additional outbox, depending on the test.</summary>
    public sealed class ShippingOutboxDbContext(DbContextOptions<ShippingOutboxDbContext> options) : DbContext(options);
}

/// <summary>The consumer every <see cref="AdditionalOutboxConformanceTests"/> host registers.</summary>
public sealed class AdditionalOutboxTestsModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog)
    {
        catalog.AddBusConsumer<
            AdditionalOutboxConformanceTests.OutboxProbeConsumer,
            AdditionalOutboxConformanceTests.OutboxProbe
        >(
            "tests.additional-outbox.consumer",
            everyInstance: false,
            TestConsumerDispatch.FromServices<
                AdditionalOutboxConformanceTests.OutboxProbeConsumer,
                AdditionalOutboxConformanceTests.OutboxProbe
            >()
        );
    }
}
