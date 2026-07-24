using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Headless.Abstractions;
using Headless.Messaging;
using HeadlessShop.Api;
using HeadlessShop.Catalog.Api;
using HeadlessShop.Catalog.Application;
using HeadlessShop.Catalog.Domain;
using HeadlessShop.Catalog.Infrastructure;
using HeadlessShop.Contracts;
using HeadlessShop.Ordering.Api;
using HeadlessShop.Ordering.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HeadlessShop.Tests.Integration;

[Collection(typeof(PostgreSqlFixture))]
public sealed class ShopSmokeTests(PostgreSqlFixture postgres)
{
    [Fact]
    public async Task product_to_order_flow_publishes_projects_and_places_order()
    {
        await using var factory = new HeadlessShopFactory(postgres.ConnectionString);
        using var client = _CreateAuthenticatedClient(factory);
        await _StartMessagingAsync(factory);

        var createProduct = await client.PostAsJsonAsync(
            "/catalog/products",
            new CreateProductRequest("SKU-001", "Agent-ready backpack", 89m),
            TestContext.Current.CancellationToken
        );

        createProduct.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = await createProduct.Content.ReadFromJsonAsync<ProductView>(TestContext.Current.CancellationToken);
        product.Should().NotBeNull();

        await _WaitUntilAsync(
            () => _RowExistsAsync(postgres.ConnectionString, "ordering", "ProductSnapshots", product!.Id),
            "Ordering did not project ProductCreated"
        );
        (await _MessageCountAsync(postgres.ConnectionString, "published", product!.Id.ToString()))
            .Should()
            .BeGreaterThanOrEqualTo(1, "the integration event must be durably persisted");

        var placeOrder = await client.PostAsJsonAsync(
            "/orders",
            new PlaceOrderRequest(product!.Id, 2),
            TestContext.Current.CancellationToken
        );

        placeOrder.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = await placeOrder.Content.ReadFromJsonAsync<OrderView>(TestContext.Current.CancellationToken);
        order.Should().NotBeNull();
        order!.ProductId.Should().Be(product.Id);
    }

    [Fact]
    public async Task duplicate_product_sku_returns_conflict()
    {
        await using var factory = new HeadlessShopFactory(postgres.ConnectionString);
        using var client = _CreateAuthenticatedClient(factory);
        var sku = $"DUP-{Guid.NewGuid():N}";

        var first = await client.PostAsJsonAsync(
            "/catalog/products",
            new CreateProductRequest(sku, "Original", 10m),
            TestContext.Current.CancellationToken
        );
        var duplicate = await client.PostAsJsonAsync(
            "/catalog/products",
            new CreateProductRequest(sku, "Duplicate", 11m),
            TestContext.Current.CancellationToken
        );

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task product_and_outbox_message_roll_back_together()
    {
        await using var factory = new HeadlessShopFactory(postgres.ConnectionString);
        _ = factory.CreateClient();
        var productId = Guid.NewGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        using var tenantScope = currentTenant.Change("tenant-a");
        var rolledBack = false;

        try
        {
            await dbContext.ExecuteCoordinatedTransactionAsync(
                async (database, cancellationToken) =>
                {
                    database.Products.Add(
                        Product.Create(productId, "tenant-a", $"ROLLBACK-{productId:N}", "Rollback probe", 5m)
                    );
                    await database.SaveChangesAsync(cancellationToken);
                    throw new InvalidOperationException("Force rollback after the outbox write.");
                },
                cancellationToken: TestContext.Current.CancellationToken
            );
        }
        catch (InvalidOperationException exception)
            when (string.Equals(exception.Message, "Force rollback after the outbox write.", StringComparison.Ordinal))
        {
            rolledBack = true;
        }

        rolledBack.Should().BeTrue();
        (await _RowExistsAsync(postgres.ConnectionString, "catalog", "Products", productId)).Should().BeFalse();
        (await _MessageCountAsync(postgres.ConnectionString, "published", productId.ToString())).Should().Be(0);
    }

    [Fact]
    public async Task duplicate_product_event_is_idempotent()
    {
        await using var factory = new HeadlessShopFactory(postgres.ConnectionString);
        await _StartMessagingAsync(factory);
        var publisher = factory.Services.GetRequiredService<IBus>();
        var productId = Guid.NewGuid();
        var message = new ProductCreated(
            Guid.NewGuid().ToString("N"),
            productId,
            $"REPLAY-{productId:N}",
            "Replay-safe product",
            20m,
            "tenant-a"
        );
        var options = new PublishOptions { TenantId = "tenant-a" };

        await publisher.PublishAsync(message, options, TestContext.Current.CancellationToken);
        await _WaitUntilAsync(
            () => _RowExistsAsync(postgres.ConnectionString, "ordering", "ProductSnapshots", productId),
            "the first ProductCreated delivery was not projected"
        );
        await publisher.PublishAsync(message, options, TestContext.Current.CancellationToken);
        await _WaitUntilAsync(
            async () => await _MessageCountAsync(postgres.ConnectionString, "received", productId.ToString()) >= 2,
            "the duplicate ProductCreated delivery was not consumed"
        );

        (await _RowCountAsync(postgres.ConnectionString, "ordering", "ProductSnapshots", productId)).Should().Be(1);
    }

    [Fact]
    public async Task transient_consumer_failure_is_retried()
    {
        await using var factory = new HeadlessShopFactory(postgres.ConnectionString);
        await _StartMessagingAsync(factory);
        var state = factory.Services.GetRequiredService<TransientRetryState>();
        var probe = new TransientRetryProbe(Guid.NewGuid());

        await factory
            .Services.GetRequiredService<IBus>()
            .PublishAsync(probe, new PublishOptions { TenantId = "tenant-a" }, TestContext.Current.CancellationToken);
        await state.Completed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        state.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task permanent_consumer_failure_is_persisted_for_diagnosis()
    {
        await using var factory = new HeadlessShopFactory(postgres.ConnectionString);
        await _StartMessagingAsync(factory);
        var probe = new PermanentFailureProbe(Guid.NewGuid());

        await factory
            .Services.GetRequiredService<IBus>()
            .PublishAsync(probe, new PublishOptions { TenantId = "tenant-a" }, TestContext.Current.CancellationToken);
        await _WaitUntilAsync(
            () => _FailedMessageExistsAsync(postgres.ConnectionString, probe.Id.ToString()),
            "the permanently failed message was not retained"
        );

        factory.Services.GetRequiredService<PermanentFailureState>().Attempts.Should().Be(1);
    }

    [Fact]
    public async Task tenant_b_cannot_read_tenant_a_product_even_with_spoof_header()
    {
        await using var factory = new HeadlessShopFactory(postgres.ConnectionString);
        using var tenantA = _CreateAuthenticatedClient(factory);
        using var tenantB = factory.CreateClient();
        tenantB.DefaultRequestHeaders.Add(FakeTourAuthenticationHandler.UserHeader, "user-b");
        tenantB.DefaultRequestHeaders.Add(FakeTourAuthenticationHandler.TenantHeader, "tenant-b");
        tenantB.DefaultRequestHeaders.Add("X-Spoof-Tenant-Id", "tenant-a");

        var createProduct = await tenantA.PostAsJsonAsync(
            "/catalog/products",
            new CreateProductRequest("SKU-002", "Tenant scoped notebook", 12m),
            TestContext.Current.CancellationToken
        );

        var product = await createProduct.Content.ReadFromJsonAsync<ProductView>(TestContext.Current.CancellationToken);
        var tenantBRead = await tenantB.GetAsync(
            $"/catalog/products/{product!.Id}",
            TestContext.Current.CancellationToken
        );

        tenantBRead.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task create_product_requires_permission_and_tenant_context()
    {
        await using var factory = new HeadlessShopFactory(postgres.ConnectionString);
        using var anonymous = factory.CreateClient();
        using var missingPermission = factory.CreateClient();
        missingPermission.DefaultRequestHeaders.Add(FakeTourAuthenticationHandler.UserHeader, "user-a");
        missingPermission.DefaultRequestHeaders.Add(FakeTourAuthenticationHandler.TenantHeader, "tenant-a");

        var anonymousResponse = await anonymous.PostAsJsonAsync(
            "/catalog/products",
            new CreateProductRequest("SKU-003", "No tenant", 10m),
            TestContext.Current.CancellationToken
        );
        var permissionResponse = await missingPermission.PostAsJsonAsync(
            "/catalog/products",
            new CreateProductRequest("SKU-004", "No permission", 10m),
            TestContext.Current.CancellationToken
        );

        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        permissionResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task openapi_is_development_only()
    {
        await using var developmentFactory = new HeadlessShopFactory(postgres.ConnectionString, "Development");
        await using var productionFactory = new HeadlessShopFactory(postgres.ConnectionString, "Production");

        var developmentResponse = await developmentFactory
            .CreateClient()
            .GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var productionResponse = await productionFactory
            .CreateClient()
            .GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        developmentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        productionResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task production_rejects_fake_tour_headers()
    {
        await using var factory = new HeadlessShopFactory(postgres.ConnectionString, "Production");
        using var client = _CreateAuthenticatedClient(factory);

        var response = await client.PostAsJsonAsync(
            "/catalog/products",
            new CreateProductRequest("SKU-005", "Production fake header", 10m),
            TestContext.Current.CancellationToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task placing_order_for_missing_product_returns_not_found()
    {
        await using var factory = new HeadlessShopFactory(postgres.ConnectionString);
        using var client = _CreateAuthenticatedClient(factory);

        var response = await client.PostAsJsonAsync(
            "/orders",
            new PlaceOrderRequest(Guid.NewGuid(), 1),
            TestContext.Current.CancellationToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task database_initialization_is_idempotent_across_hosts()
    {
        await using (var firstFactory = new HeadlessShopFactory(postgres.ConnectionString))
        using (var firstClient = firstFactory.CreateClient())
        {
            var firstResponse = await firstClient.GetAsync(
                $"/catalog/products/{Guid.NewGuid()}",
                TestContext.Current.CancellationToken
            );
            firstResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        await using (var secondFactory = new HeadlessShopFactory(postgres.ConnectionString))
        using (var secondClient = secondFactory.CreateClient())
        {
            var secondResponse = await secondClient.GetAsync(
                $"/catalog/products/{Guid.NewGuid()}",
                TestContext.Current.CancellationToken
            );
            secondResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    private static HttpClient _CreateAuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(FakeTourAuthenticationHandler.UserHeader, "user-a");
        client.DefaultRequestHeaders.Add(FakeTourAuthenticationHandler.TenantHeader, "tenant-a");
        client.DefaultRequestHeaders.Add(FakeTourAuthenticationHandler.PermissionHeader, "catalog.products.create");

        return client;
    }

    private static async Task _StartMessagingAsync(WebApplicationFactory<Program> factory)
    {
        _ = factory.CreateClient();
        await factory
            .Services.GetRequiredService<IBootstrapper>()
            .BootstrapAsync(TestContext.Current.CancellationToken);
    }

    private static async Task _WaitUntilAsync(Func<Task<bool>> condition, string failureMessage)
    {
        var timeout = TimeProvider.System.GetUtcNow().AddSeconds(10);

        while (TimeProvider.System.GetUtcNow() < timeout)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        }

        throw new TimeoutException(failureMessage);
    }

    private static async Task<bool> _RowExistsAsync(string connectionString, string schema, string table, Guid id) =>
        await _RowCountAsync(connectionString, schema, table, id) > 0;

    private static async Task<int> _RowCountAsync(string connectionString, string schema, string table, Guid id)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            $"SELECT count(*) FROM \"{schema}\".\"{table}\" WHERE \"Id\" = @id",
            connection
        );
        command.Parameters.AddWithValue("id", id);

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken),
            CultureInfo.InvariantCulture
        );
    }

    private static Task<int> _MessageCountAsync(string connectionString, string table, string contentFragment) =>
        _MessageCountAsync(connectionString, table, contentFragment, status: null);

    private static async Task<int> _MessageCountAsync(
        string connectionString,
        string table,
        string contentFragment,
        string? status
    )
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var statusClause = status is null ? string.Empty : " AND \"StatusName\" = @status";
        await using var command = new NpgsqlCommand(
            $"SELECT count(*) FROM \"messaging\".\"{table}\" WHERE \"Content\" LIKE @content{statusClause}",
            connection
        );
        command.Parameters.AddWithValue("content", $"%{contentFragment}%");

        if (status is not null)
        {
            command.Parameters.AddWithValue("status", status);
        }

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken),
            CultureInfo.InvariantCulture
        );
    }

    private static async Task<bool> _FailedMessageExistsAsync(string connectionString, string contentFragment) =>
        await _MessageCountAsync(connectionString, "received", contentFragment, "Failed") > 0;

    private sealed class HeadlessShopFactory(string connectionString, string environment = "Development")
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:Shop", connectionString);
            builder.UseSetting("HeadlessShop:Encryption:DefaultPassPhrase", "test-passphrase");
            builder.UseSetting("HeadlessShop:Encryption:DefaultSalt", "test-encryption-salt");
            builder.UseSetting("HeadlessShop:Hashing:DefaultSalt", "test-hash-salt");
            builder.UseSetting("HeadlessShop:AllowFakeTourAuth", "true");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TransientRetryState>();
                services.AddSingleton<PermanentFailureState>();
                services.ForMessage<TransientRetryProbe>(message =>
                    message.MessageName("tests.transient-retry").OnBus<TransientRetryConsumer>()
                );
                services.ForMessage<PermanentFailureProbe>(message =>
                    message.MessageName("tests.permanent-failure").OnBus<PermanentFailureConsumer>()
                );
            });
        }
    }

    private sealed record TransientRetryProbe(Guid Id);

    private sealed class TransientRetryState
    {
        private int _attempts;

        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Attempts => Volatile.Read(ref _attempts);

        public int RecordAttempt() => Interlocked.Increment(ref _attempts);
    }

    private sealed class TransientRetryConsumer(TransientRetryState state) : IConsume<TransientRetryProbe>
    {
        public ValueTask ConsumeAsync(ConsumeContext<TransientRetryProbe> context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (state.RecordAttempt() == 1)
            {
                throw new InvalidOperationException($"Transient failure for {context.Message.Id}.");
            }

            state.Completed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed record PermanentFailureProbe(Guid Id);

    private sealed class PermanentFailureState
    {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        public void RecordAttempt() => Interlocked.Increment(ref _attempts);
    }

    private sealed class PermanentFailureConsumer(PermanentFailureState state) : IConsume<PermanentFailureProbe>
    {
        public ValueTask ConsumeAsync(
            ConsumeContext<PermanentFailureProbe> context,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.RecordAttempt();
            throw new ArgumentException($"Permanent failure for {context.Message.Id}.", nameof(context));
        }
    }
}
