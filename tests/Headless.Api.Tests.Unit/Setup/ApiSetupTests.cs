// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api;
using Headless.Api.Middlewares;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests.Setup;

public sealed class ApiSetupTests
{
    [Fact]
    public void add_headless_api_should_register_current_tenant_by_default()
    {
        // given
        var builder = WebApplication.CreateBuilder();
        _AddDefaultHeadlessSecurityConfiguration(builder.Configuration);

        // when
        builder.AddHeadlessInfrastructure();

        using var serviceProvider = builder.Services.BuildServiceProvider();
        var currentTenant = serviceProvider.GetRequiredService<ICurrentTenant>();

        // then
        currentTenant.Should().BeOfType<CurrentTenant>();
        currentTenant.Id.Should().BeNull();
        currentTenant.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void add_headless_api_should_replace_null_current_tenant_fallback()
    {
        // given
        var builder = WebApplication.CreateBuilder();
        _AddDefaultHeadlessSecurityConfiguration(builder.Configuration);
        builder.Services.AddSingleton<ICurrentTenant, NullCurrentTenant>();

        // when
        builder.AddHeadlessInfrastructure();

        using var serviceProvider = builder.Services.BuildServiceProvider();

        // then
        serviceProvider.GetRequiredService<ICurrentTenant>().Should().BeOfType<CurrentTenant>();
    }

    [Fact]
    public void add_headless_api_should_preserve_custom_current_tenant()
    {
        // given
        var builder = WebApplication.CreateBuilder();
        _AddDefaultHeadlessSecurityConfiguration(builder.Configuration);
        var customTenant = new ApiCustomCurrentTenant();
        builder.Services.AddSingleton<ICurrentTenant>(customTenant);

        // when
        builder.AddHeadlessInfrastructure();

        using var serviceProvider = builder.Services.BuildServiceProvider();

        // then
        serviceProvider.GetRequiredService<ICurrentTenant>().Should().BeSameAs(customTenant);
    }

    [Fact]
    public async Task add_headless_api_should_register_service_defaults()
    {
        // given
        var builder = WebApplication.CreateBuilder();
        _AddDefaultHeadlessSecurityConfiguration(builder.Configuration);

        // when
        builder.AddHeadlessInfrastructure();

        using var serviceProvider = builder.Services.BuildServiceProvider();
        var kestrelOptions = serviceProvider.GetRequiredService<IOptions<KestrelServerOptions>>().Value;
        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();
        var statusCodesRewriter = serviceProvider.GetRequiredService<StatusCodesRewriterMiddleware>();
        var healthReport = await healthCheckService.CheckHealthAsync(
            registration => registration.Tags.Contains("live"),
            CancellationToken.None
        );

        // then
        kestrelOptions.AddServerHeader.Should().BeFalse();
        kestrelOptions.Limits.MaxRequestBodySize.Should().Be(1024 * 1024 * 30);
        kestrelOptions.Limits.MaxRequestHeaderCount.Should().Be(40);
        statusCodesRewriter.Should().NotBeNull();
        healthReport.Entries.Should().ContainKey("self");
        healthReport.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public void add_headless_api_should_allow_configuration_sections()
    {
        // given
        var builder = WebApplication.CreateBuilder();
        var configuration = _CreateSecuritySectionConfiguration();

        // when
        builder.AddHeadlessInfrastructure(
            configuration.GetRequiredSection("Security:StringEncryption"),
            configuration.GetRequiredSection("Security:StringHash")
        );

        using var serviceProvider = builder.Services.BuildServiceProvider();
        var encryptionOptions = serviceProvider.GetRequiredService<IOptions<StringEncryptionOptions>>().Value;
        var hashOptions = serviceProvider.GetRequiredService<IOptions<StringHashOptions>>().Value;

        // then
        encryptionOptions.DefaultPassPhrase.Should().Be("SectionPassPhrase123");
        encryptionOptions.DefaultSalt.Should().BeEquivalentTo("SectionSalt"u8.ToArray());
        hashOptions.DefaultSalt.Should().Be("SectionSalt");
        hashOptions.Iterations.Should().Be(700000);
    }

    [Fact]
    public void add_headless_api_should_allow_configuration_callbacks()
    {
        // given
        var builder = WebApplication.CreateBuilder();
        _AddDefaultHeadlessSecurityConfiguration(builder.Configuration);

        // when
        builder.AddHeadlessInfrastructure(
            encryption =>
            {
                encryption.DefaultPassPhrase = "ActionPassPhrase123";
                encryption.InitVectorBytes = "ActionIV01234567"u8.ToArray();
                encryption.DefaultSalt = "ActionSalt"u8.ToArray();
            },
            hash =>
            {
                hash.DefaultSalt = "ActionSalt";
                hash.Iterations = 800000;
            }
        );

        using var serviceProvider = builder.Services.BuildServiceProvider();
        var encryptionOptions = serviceProvider.GetRequiredService<IOptions<StringEncryptionOptions>>().Value;
        var hashOptions = serviceProvider.GetRequiredService<IOptions<StringHashOptions>>().Value;

        // then
        encryptionOptions.DefaultPassPhrase.Should().Be("ActionPassPhrase123");
        encryptionOptions.DefaultSalt.Should().BeEquivalentTo("ActionSalt"u8.ToArray());
        hashOptions.DefaultSalt.Should().Be("ActionSalt");
        hashOptions.Iterations.Should().Be(800000);
    }

    [Fact]
    public void add_headless_api_should_use_default_hash_configuration_when_hash_callback_is_omitted()
    {
        // given
        var builder = WebApplication.CreateBuilder();
        _AddDefaultHeadlessSecurityConfiguration(builder.Configuration);

        // when
        builder.AddHeadlessInfrastructure(encryption =>
        {
            encryption.DefaultPassPhrase = "ActionPassPhrase123";
            encryption.InitVectorBytes = "ActionIV01234567"u8.ToArray();
            encryption.DefaultSalt = "ActionSalt"u8.ToArray();
        });

        using var serviceProvider = builder.Services.BuildServiceProvider();
        var encryptionOptions = serviceProvider.GetRequiredService<IOptions<StringEncryptionOptions>>().Value;
        var hashOptions = serviceProvider.GetRequiredService<IOptions<StringHashOptions>>().Value;

        // then
        encryptionOptions.DefaultPassPhrase.Should().Be("ActionPassPhrase123");
        encryptionOptions.DefaultSalt.Should().BeEquivalentTo("ActionSalt"u8.ToArray());
        hashOptions.DefaultSalt.Should().Be("TestSalt");
    }

    [Fact]
    public void add_headless_api_should_allow_service_provider_callbacks()
    {
        // given
        var builder = WebApplication.CreateBuilder();
        _AddDefaultHeadlessSecurityConfiguration(builder.Configuration);
        builder.Services.AddSingleton(
            new SecurityTestValues
            {
                PassPhrase = "ProviderPassPhrase123",
                EncryptionSalt = "ProviderSalt"u8.ToArray(),
                HashSalt = "ProviderHashSalt",
            }
        );

        // when
        builder.AddHeadlessInfrastructure(
            (encryption, serviceProvider) =>
            {
                var values = serviceProvider.GetRequiredService<SecurityTestValues>();
                encryption.DefaultPassPhrase = values.PassPhrase;
                encryption.InitVectorBytes = "ProviderIV012345"u8.ToArray();
                encryption.DefaultSalt = values.EncryptionSalt;
            },
            (hash, serviceProvider) =>
            {
                var values = serviceProvider.GetRequiredService<SecurityTestValues>();
                hash.DefaultSalt = values.HashSalt;
                hash.Iterations = 900000;
            }
        );

        using var serviceProvider = builder.Services.BuildServiceProvider();
        var encryptionOptions = serviceProvider.GetRequiredService<IOptions<StringEncryptionOptions>>().Value;
        var hashOptions = serviceProvider.GetRequiredService<IOptions<StringHashOptions>>().Value;

        // then
        encryptionOptions.DefaultPassPhrase.Should().Be("ProviderPassPhrase123");
        encryptionOptions.DefaultSalt.Should().BeEquivalentTo("ProviderSalt"u8.ToArray());
        hashOptions.DefaultSalt.Should().Be("ProviderHashSalt");
        hashOptions.Iterations.Should().Be(900000);
    }

    [Fact]
    public void add_headless_multi_tenancy_should_replace_null_current_tenant_and_store_custom_claim_type()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<ICurrentTenant, NullCurrentTenant>();

        // when
        builder.AddHeadlessMultiTenancy(options => options.ClaimType = "custom_tenant_id");

        using var serviceProvider = builder.Services.BuildServiceProvider();
        var currentTenant = serviceProvider.GetRequiredService<ICurrentTenant>();
        var options = serviceProvider.GetRequiredService<IOptions<MultiTenancyOptions>>().Value;

        // then
        currentTenant.Should().BeOfType<CurrentTenant>();
        options.ClaimType.Should().Be("custom_tenant_id");
    }

    [Fact]
    public void add_headless_multi_tenancy_should_preserve_custom_current_tenant()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        var customTenant = new ApiCustomCurrentTenant();
        builder.Services.AddSingleton<ICurrentTenant>(customTenant);

        // when
        builder.AddHeadlessMultiTenancy();

        using var serviceProvider = builder.Services.BuildServiceProvider();

        // then
        serviceProvider.GetRequiredService<ICurrentTenant>().Should().BeSameAs(customTenant);
    }

    private sealed class ApiCustomCurrentTenant : ICurrentTenant
    {
        public bool IsAvailable => true;

        public string? Id => "custom";

        public string? Name => "Custom";

        public IDisposable Change(string? id, string? name = null) => new ApiCurrentTenantScope();
    }

    private sealed class ApiCurrentTenantScope : IDisposable
    {
        public void Dispose() { }
    }

    private sealed class SecurityTestValues
    {
        public required string PassPhrase { get; init; }

        public required byte[] EncryptionSalt { get; init; }

        public required string HashSalt { get; init; }
    }

    private static void _AddDefaultHeadlessSecurityConfiguration(IConfigurationBuilder configuration)
    {
        configuration.AddInMemoryCollection([
            new KeyValuePair<string, string?>("Headless:StringEncryption:DefaultPassPhrase", "TestPassPhrase123456"),
            new KeyValuePair<string, string?>("Headless:StringEncryption:InitVectorBytes", "VGVzdElWMDEyMzQ1Njc4OQ=="),
            new KeyValuePair<string, string?>("Headless:StringEncryption:DefaultSalt", "VGVzdFNhbHQ="),
            new KeyValuePair<string, string?>("Headless:StringHash:DefaultSalt", "TestSalt"),
        ]);
    }

    private static IConfiguration _CreateSecuritySectionConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection([
                new KeyValuePair<string, string?>(
                    "Security:StringEncryption:DefaultPassPhrase",
                    "SectionPassPhrase123"
                ),
                new KeyValuePair<string, string?>(
                    "Security:StringEncryption:InitVectorBytes",
                    "VGVzdElWMDEyMzQ1Njc4OQ=="
                ),
                new KeyValuePair<string, string?>("Security:StringEncryption:DefaultSalt", "U2VjdGlvblNhbHQ="),
                new KeyValuePair<string, string?>("Security:StringHash:DefaultSalt", "SectionSalt"),
                new KeyValuePair<string, string?>("Security:StringHash:Iterations", "700000"),
            ])
            .Build();
    }
}
