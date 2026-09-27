// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
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

    [Fact]
    public void should_default_the_storage_schema_to_the_shared_headless_schema()
    {
        new AuditLogStorageOptions().Schema.Should().Be("headless");
    }

    [Fact]
    public void should_derive_every_storage_object_name_from_the_table_name()
    {
        AuditLogStorageNames.PrimaryKey("audit_log").Should().Be("PK_audit_log");
        AuditLogStorageNames.TenantTimeIndex("audit_log").Should().Be("ix_audit_log_tenant_time");
        AuditLogStorageNames.TenantActionTimeIndex("audit_log").Should().Be("ix_audit_log_tenant_action_time");
        AuditLogStorageNames.TenantEntityTimeIndex("audit_log").Should().Be("ix_audit_log_tenant_entity_time");
        AuditLogStorageNames.TenantActorTimeIndex("audit_log").Should().Be("ix_audit_log_tenant_actor_time");
        AuditLogStorageNames.TenantAccountTimeIndex("audit_log").Should().Be("ix_audit_log_tenant_account_time");
        AuditLogStorageNames.CorrelationIndex("audit_log").Should().Be("ix_audit_log_correlation");
    }

    [Theory]
    [InlineData("audit_log")]
    [InlineData("tenant_audit_archive")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 40 characters: the longest derived name is exactly 63
    public void should_accept_a_table_name_whose_derived_names_fit_the_identifier_limit(string tableName)
    {
        // when
        var result = new TableNameValidator().Validate(new AuditLogStorageOptions { TableName = tableName });

        // then
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_reject_a_table_name_whose_derived_index_name_exceeds_the_identifier_limit()
    {
        // given — 41 characters push "ix_<table>_tenant_account_time" to 64 bytes.
        var options = new AuditLogStorageOptions { TableName = new string('a', 41) };

        // when
        var result = new TableNameValidator().Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result
            .Errors.Should()
            .ContainSingle()
            .Which.ErrorMessage.Should()
            .Contain("40 characters")
            .And.Contain("63 bytes");
    }

    private sealed class TableNameValidator : AbstractValidator<AuditLogStorageOptions>
    {
        public TableNameValidator()
        {
            RuleFor(x => x.TableName).FitsDerivedStorageNames();
        }
    }

    private sealed class NoopStorageExtension : IAuditLogStorageOptionsExtension
    {
        public void AddServices(IServiceCollection services) { }
    }
}
