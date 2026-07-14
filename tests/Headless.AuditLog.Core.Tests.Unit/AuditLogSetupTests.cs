// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class AuditLogSetupTests
{
    [Fact]
    public void add_headless_audit_log_throws_when_transform_strategy_has_no_transformer()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessAuditLog(options => options.SensitiveDataStrategy = SensitiveDataStrategy.Transform);
        var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<AuditLogOptions>>();

        // then
        var assertions = options.Invoking(x => x.Value).Should().Throw<OptionsValidationException>();
        assertions
            .And.Failures.Should()
            .Contain(failure =>
                failure.Contains(
                    "SensitiveValueTransformer must be configured when SensitiveDataStrategy is Transform."
                )
            );
    }

    [Fact]
    public void add_headless_audit_log_allows_transform_strategy_when_transformer_is_configured()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessAuditLog(options =>
        {
            options.SensitiveDataStrategy = SensitiveDataStrategy.Transform;
            options.SensitiveValueTransformer = context => context.Value?.ToString();
        });
        var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<AuditLogOptions>>();

        // then
        options.Value.SensitiveValueTransformer.Should().NotBeNull();
    }

    [Fact]
    public void add_headless_audit_log_with_setup_throws_when_no_storage_provider_is_registered()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () => services.AddHeadlessAuditLog((HeadlessAuditLogSetupBuilder _) => { });

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*requires exactly one storage provider*UseEntityFramework*");
    }

    [Fact]
    public void add_headless_audit_log_with_setup_throws_when_multiple_storage_providers_are_registered()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddHeadlessAuditLog(setup =>
            {
                setup.RegisterExtension(new NoopStorageExtension());
                setup.RegisterExtension(new NoopStorageExtension());
            });

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*Multiple storage providers were configured*");
    }

    [Fact]
    public void configure_options_composes_delegates_in_registration_order()
    {
        // given
        var services = new ServiceCollection();
        bool? auditByDefaultSeenBySecondDelegate = null;

        services.AddHeadlessAuditLog(setup =>
        {
            setup.RegisterExtension(new NoopStorageExtension());
            setup.ConfigureOptions(options => options.AuditByDefault = true);
            setup.ConfigureOptions(options =>
            {
                auditByDefaultSeenBySecondDelegate = options.AuditByDefault;
                options.IsEnabled = false;
            });
        });

        var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<AuditLogOptions>>().Value;

        // then - both delegates ran in registration order against the same options instance
        auditByDefaultSeenBySecondDelegate.Should().BeTrue();
        options.AuditByDefault.Should().BeTrue();
        options.IsEnabled.Should().BeFalse();
    }

    private sealed class NoopStorageExtension : IAuditLogStorageOptionsExtension
    {
        public void AddServices(IServiceCollection services) { }
    }
}
