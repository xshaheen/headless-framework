using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Headless.Messaging.Testing;
using HeadlessShop.Api;
using HeadlessShop.Catalog.Api;
using HeadlessShop.Catalog.Application;
using HeadlessShop.Contracts;
using HeadlessShop.Ordering.Api;
using HeadlessShop.Ordering.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HeadlessShop.Tests.Integration;

public sealed class ShopSmokeTests
{
    [Fact]
    public async Task product_to_order_flow_publishes_projects_and_places_order()
    {
        await using var factory = new HeadlessShopFactory();
        using var client = _CreateAuthenticatedClient(factory);
        var harness = factory.Services.GetRequiredService<MessagingTestHarness>();

        var createProduct = await client.PostAsJsonAsync(
            "/catalog/products",
            new CreateProductRequest("SKU-001", "Agent-ready backpack", 89m),
            TestContext.Current.CancellationToken
        );

        createProduct.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = await createProduct.Content.ReadFromJsonAsync<ProductView>(TestContext.Current.CancellationToken);
        product.Should().NotBeNull();

        await harness.WaitForConsumed<ProductCreated>(
            message =>
                message.ProductId == product!.Id
                && string.Equals(message.TenantId, "tenant-a", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );

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
    public async Task tenant_b_cannot_read_tenant_a_product_even_with_spoof_header()
    {
        await using var factory = new HeadlessShopFactory();
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
        await using var factory = new HeadlessShopFactory();
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
        await using var developmentFactory = new HeadlessShopFactory(environment: "Development");
        await using var productionFactory = new HeadlessShopFactory(environment: "Production");

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
        await using var factory = new HeadlessShopFactory(environment: "Production");
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
        await using var factory = new HeadlessShopFactory();
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
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            await using (var firstFactory = new HeadlessShopFactory(databasePath: databasePath))
            using (var firstClient = firstFactory.CreateClient())
            {
                var firstResponse = await firstClient.GetAsync(
                    $"/catalog/products/{Guid.NewGuid()}",
                    TestContext.Current.CancellationToken
                );
                firstResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            }

            await using (var secondFactory = new HeadlessShopFactory(databasePath: databasePath))
            using (var secondClient = secondFactory.CreateClient())
            {
                var secondResponse = await secondClient.GetAsync(
                    $"/catalog/products/{Guid.NewGuid()}",
                    TestContext.Current.CancellationToken
                );
                secondResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            }
        }
        finally
        {
            File.Delete(databasePath);
            File.Delete($"{databasePath}-wal");
            File.Delete($"{databasePath}-shm");
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

    private sealed class HeadlessShopFactory(string environment = "Development", string? databasePath = null)
        : WebApplicationFactory<Program>
    {
        private readonly string _databasePath =
            databasePath ?? Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        private readonly bool _ownsDatabase = databasePath is null;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:Shop", $"Data Source={_databasePath}");
            builder.UseSetting("HeadlessShop:Encryption:DefaultPassPhrase", "test-passphrase");
            builder.UseSetting("HeadlessShop:Encryption:DefaultSalt", "test-encryption-salt");
            builder.UseSetting("HeadlessShop:Hashing:DefaultSalt", "test-hash-salt");
            builder.UseSetting("HeadlessShop:AllowFakeTourAuth", "true");
            builder.ConfigureTestServices(services => services.AddMessagingTestHarness());
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (_ownsDatabase)
            {
                _DeleteDatabaseFile(_databasePath);
                _DeleteDatabaseFile($"{_databasePath}-wal");
                _DeleteDatabaseFile($"{_databasePath}-shm");
            }
        }

        private static void _DeleteDatabaseFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
