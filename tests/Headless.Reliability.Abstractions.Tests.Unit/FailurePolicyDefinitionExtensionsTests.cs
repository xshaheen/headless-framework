// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using System.Text.Json;
using Headless.Reliability;
using Headless.Testing.Tests;

namespace Tests;

public sealed class FailurePolicyDefinitionExtensionsTests : TestBase
{
    [Fact]
    public void should_write_every_declared_setting_in_order()
    {
        // given
        var policy = new FailurePolicyBuilder()
            .Immediate(2)
            .Delayed(3, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15))
            .FailOn<ArgumentException>()
            .FailOn<InvalidOperationException>()
            .FailWhen(static _ => false)
            .FailWhen(static _ => false)
            .Build();

        // when
        var json = _Describe(policy);

        // then
        json.Should()
            .Be(
                """{"immediateRetries":2,"delayedRetries":3,"delayedInitialDelaySeconds":30,"delayedMaxDelaySeconds":900,"failOn":["System.ArgumentException","System.InvalidOperationException"],"failWhenRules":2}"""
            );
    }

    [Fact]
    public void should_omit_delays_and_undeclared_rules_when_policy_has_no_delayed_retries()
    {
        // given
        var policy = new FailurePolicyBuilder()
            .Immediate(1)
            .Delayed(0, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15))
            .Build();

        // when
        var json = _Describe(policy);

        // then
        json.Should().Be("""{"immediateRetries":1,"delayedRetries":0}""");
    }

    private static string _Describe(FailurePolicyDefinition policy)
    {
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            policy.WriteDescription(writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
