// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Logging;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>
/// The logging profile labels every entry with the ambient tenant on any provider, console-only hosts included, and
/// never writes the key twice.
/// </summary>
public sealed partial class TenantLogEnrichmentTests : TestBase
{
    private const string _TenantId = "TenantId";

    [Fact]
    public void should_add_the_ambient_tenant_once_to_entries_written_while_a_tenant_is_ambient_only()
    {
        // given
        using var sink = new CapturingProvider();
        using var host = _Build(sink);
        var logger = _Logger(host);
        var currentTenant = host.Services.GetRequiredService<ICurrentTenant>();

        // when
        logger.LogInformation("before");
        using (currentTenant.Change("acme"))
        {
            logger.LogInformation("inside");

            using (currentTenant.Change("globex"))
            {
                logger.LogInformation("nested");
            }

            using (currentTenant.Change(null))
            {
                logger.LogInformation("system");
            }
        }

        // then
        sink.Entries.Should().HaveCount(4);
        sink.Entries[0].Keys.Should().NotContain(_TenantId);
        sink.Entries[1].Keys.Should().ContainSingle(k => k == _TenantId);
        sink.Entries[1].Properties[_TenantId].Should().Be("acme");
        sink.Entries[2].Properties[_TenantId].Should().Be("globex");
        sink.Entries[3].Keys.Should().NotContain(_TenantId);
    }

    [Fact]
    public void should_keep_the_template_value_and_add_no_second_key_when_the_template_has_a_tenant_hole()
    {
        // given
        using var sink = new CapturingProvider();
        using var host = _Build(sink);
        var logger = _Logger(host);

        // when: the framework's consume and job messages name the tenant they switch to in the template
        using (host.Services.GetRequiredService<ICurrentTenant>().Change("acme"))
        {
#pragma warning disable CA1848, CA2254 // The message-template overload is what the test exercises.
            logger.LogInformation("Switching to tenant '{TenantId}'", "globex");
#pragma warning restore CA1848, CA2254
            LogTenantSwitch(logger, "initech");
        }

        // then
        sink.Entries.Should().HaveCount(2);
        sink.Entries[0].Keys.Should().ContainSingle(k => k == _TenantId);
        sink.Entries[0].Properties[_TenantId].Should().Be("globex");
        sink.Entries[1].Keys.Should().ContainSingle(k => k == _TenantId);
        sink.Entries[1].Properties[_TenantId].Should().Be("initech");
    }

    [Fact]
    public void should_add_no_tenant_when_log_enrichment_is_turned_off()
    {
        // given
        using var sink = new CapturingProvider();
        using var host = _Build(
            sink,
            services => services.Configure<TenantTelemetryOptions>(options => options.EnrichLogs = false)
        );

        // when
        using (host.Services.GetRequiredService<ICurrentTenant>().Change("acme"))
        {
            _Logger(host).LogInformation("inside");
        }

        // then
        sink.Entries.Should().ContainSingle().Which.Keys.Should().NotContain(_TenantId);
    }

    [Fact]
    public void should_write_the_tenant_under_the_configured_log_attribute_name()
    {
        // given
        using var sink = new CapturingProvider();
        using var host = _Build(
            sink,
            services => services.Configure<TenantTelemetryOptions>(options => options.LogAttributeName = "Tenant")
        );

        // when
        using (host.Services.GetRequiredService<ICurrentTenant>().Change("acme"))
        {
            _Logger(host).LogInformation("inside");
        }

        // then
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Properties["Tenant"].Should().Be("acme");
        entry.Keys.Should().NotContain(_TenantId);
    }

    [Fact]
    public void should_log_without_a_tenant_on_a_host_that_registers_no_tenancy()
    {
        // given
        using var sink = new CapturingProvider();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.AddHeadlessLogging();
        builder.Logging.AddProvider(sink);
        using var host = builder.Build();

        // when
        _Logger(host).LogInformation("hello");

        // then
        sink.Entries.Should().ContainSingle().Which.Keys.Should().NotContain(_TenantId);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Restoring tenant '{TenantId}'")]
    private static partial void LogTenantSwitch(ILogger logger, string tenantId);

    private static ILogger _Logger(IHost host) =>
        host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Tests.Tenant");

    private static IHost _Build(CapturingProvider sink, Action<IServiceCollection>? configure = null)
    {
        // A console-only host: the logging profile and a tenant, no OpenTelemetry.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.AddHeadlessLogging();
        builder.Logging.AddProvider(sink);
        builder.Services.AddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
        builder.Services.AddSingleton<ICurrentTenant, CurrentTenant>();
        configure?.Invoke(builder.Services);

        return builder.Build();
    }
}
