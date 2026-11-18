// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests.MultiTenancy;

public sealed class TenantOutputCachePolicyTests : TestBase
{
    [Fact]
    public async Task should_vary_the_cache_key_by_the_resolved_tenant()
    {
        var policy = new TenantOutputCachePolicy(new ListLogger());
        var context = _CreateContext(tenantId: "ten_acme", resolutionRan: true);

        await policy.CacheRequestAsync(context, AbortToken);

        context.CacheVaryByRules.VaryByValues.Should().Contain(TenantOutputCachePolicy.VaryByValueName, "ten_acme");
        context.EnableOutputCaching.Should().BeFalse("the policy only varies the key, never turns caching on");
    }

    [Fact]
    public async Task should_vary_host_context_by_an_empty_tenant()
    {
        var policy = new TenantOutputCachePolicy(new ListLogger());
        var context = _CreateContext(tenantId: null, resolutionRan: true);

        await policy.CacheRequestAsync(context, AbortToken);

        context.CacheVaryByRules.VaryByValues.Should().Contain(TenantOutputCachePolicy.VaryByValueName, string.Empty);
    }

    [Fact]
    public async Task should_log_the_misordering_once_per_host_when_resolution_has_not_run()
    {
        // One policy instance per host: a second host must still get its own Error, which a static guard swallowed.
        var firstHostLogger = new ListLogger();
        var secondHostLogger = new ListLogger();
        var firstHost = new TenantOutputCachePolicy(firstHostLogger);
        var secondHost = new TenantOutputCachePolicy(secondHostLogger);

        await firstHost.CacheRequestAsync(_CreateContext(tenantId: null, resolutionRan: false), AbortToken);
        await firstHost.CacheRequestAsync(_CreateContext(tenantId: null, resolutionRan: false), AbortToken);
        await secondHost.CacheRequestAsync(_CreateContext(tenantId: null, resolutionRan: false), AbortToken);

        firstHostLogger.Entries.Should().ContainSingle().Which.Should().Be(LogLevel.Error);
        secondHostLogger.Entries.Should().ContainSingle().Which.Should().Be(LogLevel.Error);
    }

    [Fact]
    public void should_register_the_policy_as_an_output_cache_base_policy_with_catalog_resolution()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOutputCache();
        services.AddTenantCatalogResolution();
        services.AddTenantCatalogResolution();
        using var provider = services.BuildServiceProvider();

        provider
            .GetServices<IConfigureOptions<OutputCacheOptions>>()
            .OfType<TenantOutputCacheOptionsSetup>()
            .Should()
            .ContainSingle("a second catalog registration must not add the policy twice");
        provider.GetRequiredService<IOptions<OutputCacheOptions>>().Value.Should().NotBeNull();
    }

    private static OutputCacheContext _CreateContext(string? tenantId, bool resolutionRan)
    {
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns(tenantId);
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(tenant).BuildServiceProvider(),
        };

        if (resolutionRan)
        {
            httpContext.Features.Set(HeadlessTenancyResolutionApplied.Instance);
        }

        return new OutputCacheContext { HttpContext = httpContext };
    }

    private sealed class ListLogger : ILogger<TenantOutputCachePolicy>
    {
        public List<LogLevel> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add(logLevel);
        }
    }
}
