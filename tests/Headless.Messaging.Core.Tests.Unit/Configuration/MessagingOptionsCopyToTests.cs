// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Headless.Messaging.Configuration;
using Headless.Testing.Tests;

namespace Tests.Configuration;

/// <summary>
/// Drift guard for <see cref="MessagingOptions.CopyTo"/>. The reflection walk asserts every public
/// mutable property is propagated — adding a new property without updating <c>CopyTo</c> will fail
/// this test, preventing silent omissions like the <see cref="JsonSerializerOptions"/> bug found
/// during PR #254 review.
/// </summary>
public sealed class MessagingOptionsCopyToTests : TestBase
{
    [Fact]
    public void should_copy_every_public_mutable_property_to_target()
    {
        // given
        var source = new MessagingOptions();
        _SetNonDefaultValues(source);

        var target = new MessagingOptions();

        // when
        var copyTo = typeof(MessagingOptions).GetMethod("CopyTo", BindingFlags.Instance | BindingFlags.NonPublic);
        copyTo!.Invoke(source, [target]);

        // then — every public read/write property must round-trip
        var publicMutableProperties = typeof(MessagingOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.SetMethod is { IsPublic: true });

        foreach (var prop in publicMutableProperties)
        {
            var sourceValue = prop.GetValue(source);
            var targetValue = prop.GetValue(target);

            targetValue
                .Should()
                .Be(sourceValue, $"property '{prop.Name}' must be propagated by CopyTo — drift detected");
        }
    }

    [Fact]
    public void should_copy_json_serializer_options_fields()
    {
        // Guards the specific bug surfaced in PR #254 review: a user-configured converter or
        // naming policy on JsonSerializerOptions was being silently dropped by the DI-resolved
        // IOptions<MessagingOptions> instance because CopyTo never touched the property.
        var source = new MessagingOptions
        {
            JsonSerializerOptions =
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                WriteIndented = true,
                AllowTrailingCommas = true,
                MaxDepth = 99,
            },
        };
        source.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());

        var target = new MessagingOptions();

        var copyTo = typeof(MessagingOptions).GetMethod("CopyTo", BindingFlags.Instance | BindingFlags.NonPublic);
        copyTo!.Invoke(source, [target]);

        target.JsonSerializerOptions.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.SnakeCaseLower);
        target.JsonSerializerOptions.WriteIndented.Should().BeTrue();
        target.JsonSerializerOptions.AllowTrailingCommas.Should().BeTrue();
        target.JsonSerializerOptions.MaxDepth.Should().Be(99);
        target.JsonSerializerOptions.Converters.Should().ContainSingle(c => c is JsonStringEnumConverter);
    }

    private static void _SetNonDefaultValues(MessagingOptions options)
    {
        options.DefaultGroupName = "test.group";
        options.GroupNamePrefix = "prefix";
        options.TopicNamePrefix = "topic-prefix";
        options.Version = "v99";
        options.SucceedMessageExpiredAfter = 1;
        options.FailedMessageExpiredAfter = 2;
        options.ConsumerThreadCount = 3;
        options.EnableSubscriberParallelExecute = true;
        options.SubscriberParallelExecuteThreadCount = 4;
        options.SubscriberParallelExecuteBufferFactor = 5;
        options.EnablePublishParallelSend = true;
        options.PublishBatchSize = 42;
        options.CollectorCleaningInterval = 6;
        options.SchedulerBatchSize = 7;
        options.UseStorageLock = true;
        options.TenantContextRequired = true;
    }
}
