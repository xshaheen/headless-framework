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
            .And.Failures
            .Should()
            .Contain("SensitiveValueTransformer must be configured when SensitiveDataStrategy is Transform.");
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
}
