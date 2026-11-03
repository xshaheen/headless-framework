// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
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
    public void should_expose_tenant_requirement_as_read_only()
    {
        typeof(MessagingOptions)
            .GetProperty(
                nameof(MessagingOptions.TenantContextRequired),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
            )!
            .GetSetMethod()
            .Should()
            .BeNull();
    }

    [Fact]
    public void should_copy_every_public_mutable_property_to_target()
    {
        // given
        var source = new MessagingOptions();
        _SetNonDefaultValues(source);

        var target = new MessagingOptions();

        // when
        var copyTo = typeof(MessagingOptions).GetMethod(
            "CopyTo",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            [typeof(MessagingOptions)],
            null
        );
        copyTo!.Invoke(source, [target]);

        // then — every public read/write property must round-trip
        var publicMutableProperties = typeof(MessagingOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanWrite: true, SetMethod.IsPublic: true });

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
    public void should_propagate_every_public_get_only_complex_property()
    {
        // Get-only complex options (CircuitBreaker, RetryProcessor, RetryPolicy, JsonSerializerOptions)
        // are not picked up by the read/write drift walk above. This test pins the allow-list so
        // adding a new get-only complex property forces an explicit decision: either wire it into
        // CopyTo or document why it's intentionally shared by reference.
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(MessagingOptions.JsonSerializerOptions),
            nameof(MessagingOptions.CircuitBreaker),
            nameof(MessagingOptions.RetryPolicy),
            nameof(MessagingOptions.RetryProcessor),
            nameof(MessagingOptions.RequestReply),
        };

        var actual = typeof(MessagingOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p =>
                (!p.CanWrite || p.SetMethod?.IsPublic != true)
                && !p.PropertyType.IsValueType
                && p.PropertyType != typeof(string)
            )
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        actual
            .Should()
            .BeEquivalentTo(
                expected,
                "every public get-only complex option must either be wired into CopyTo (and listed here) "
                    + "or have an explicit reason for being shared by reference"
            );
    }

    [Fact]
    public void should_deep_copy_nested_option_state()
    {
        var source = new MessagingOptions
        {
            CircuitBreaker = { FailureThreshold = 42, OpenDuration = TimeSpan.FromMinutes(7) },
            RetryProcessor = { BaseInterval = TimeSpan.FromSeconds(13), CircuitOpenRateThreshold = 0.42 },
            RetryPolicy = { DispatchTimeout = TimeSpan.FromSeconds(23) },
            RequestReply =
            {
                DefaultTimeout = TimeSpan.FromSeconds(11),
                IncludeExceptionDetailsInFaults = true,
                MaxPendingRequests = 64,
            },
        };

        var target = new MessagingOptions();
        var copyTo = typeof(MessagingOptions).GetMethod(
            "CopyTo",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            [typeof(MessagingOptions)],
            null
        );
        copyTo!.Invoke(source, [target]);

        target.CircuitBreaker.FailureThreshold.Should().Be(42);
        target.CircuitBreaker.OpenDuration.Should().Be(TimeSpan.FromMinutes(7));
        target.RetryProcessor.BaseInterval.Should().Be(TimeSpan.FromSeconds(13));
        target.RetryProcessor.CircuitOpenRateThreshold.Should().Be(0.42);
        target.RetryPolicy.DispatchTimeout.Should().Be(TimeSpan.FromSeconds(23));
        target.RequestReply.DefaultTimeout.Should().Be(TimeSpan.FromSeconds(11));
        target.RequestReply.IncludeExceptionDetailsInFaults.Should().BeTrue();
        target.RequestReply.MaxPendingRequests.Should().Be(64);
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
        source.JsonSerializerOptions.Converters.Add(new UpperCaseStringConverter());

        var target = new MessagingOptions();

        var copyTo = typeof(MessagingOptions).GetMethod(
            nameof(MessagingOptions.CopyTo),
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            [typeof(MessagingOptions)],
            null
        );
        copyTo!.Invoke(source, [target]);

        target.JsonSerializerOptions.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.SnakeCaseLower);
        target.JsonSerializerOptions.WriteIndented.Should().BeTrue();
        target.JsonSerializerOptions.AllowTrailingCommas.Should().BeTrue();
        target.JsonSerializerOptions.MaxDepth.Should().Be(99);
        target.JsonSerializerOptions.Converters.Should().ContainSingle(c => c is UpperCaseStringConverter);
        // The target starts with the default converters as well; the copy must not register them a second time.
        target.JsonSerializerOptions.Converters.Should().Equal(source.JsonSerializerOptions.Converters);
    }

    [Fact]
    public void should_start_message_json_from_the_storage_preset()
    {
        var options = new MessagingOptions().JsonSerializerOptions;

        // A consumer on the previous version must still read a message whose producer added a member.
        options.UnmappedMemberHandling.Should().Be(JsonUnmappedMemberHandling.Skip);
        options.AllowDuplicateProperties.Should().BeFalse();
        options.DefaultIgnoreCondition.Should().Be(JsonIgnoreCondition.Never);
        options.IsReadOnly.Should().BeFalse("hosts customize it before the first message");
    }

    private sealed class UpperCaseStringConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetString();

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToUpperInvariant());
    }

    [Fact]
    public void should_copy_duplicate_and_indent_json_settings()
    {
        var source = new MessagingOptions
        {
            JsonSerializerOptions =
            {
                AllowDuplicateProperties = false,
                IndentCharacter = '\t',
                IndentSize = 1,
                NewLine = "\n",
            },
        };
        var target = new MessagingOptions();

        _InvokeCopyTo(source, target);

        target.JsonSerializerOptions.AllowDuplicateProperties.Should().BeFalse();
        target.JsonSerializerOptions.IndentCharacter.Should().Be('\t');
        target.JsonSerializerOptions.IndentSize.Should().Be(1);
        target.JsonSerializerOptions.NewLine.Should().Be("\n");
    }

    [Fact]
    public void should_pin_every_settable_json_serializer_option_copied_by_copy_to()
    {
        // A setting .NET adds to JsonSerializerOptions would otherwise be dropped by CopyTo with no error, as
        // AllowDuplicateProperties was. A new name here means: copy it in _CopyJsonSerializerOptions, then list it.
        var copied = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(JsonSerializerOptions.AllowDuplicateProperties),
            nameof(JsonSerializerOptions.AllowOutOfOrderMetadataProperties),
            nameof(JsonSerializerOptions.AllowTrailingCommas),
            nameof(JsonSerializerOptions.DefaultBufferSize),
            nameof(JsonSerializerOptions.DefaultIgnoreCondition),
            nameof(JsonSerializerOptions.DictionaryKeyPolicy),
            nameof(JsonSerializerOptions.Encoder),
            nameof(JsonSerializerOptions.IgnoreReadOnlyFields),
            nameof(JsonSerializerOptions.IgnoreReadOnlyProperties),
            nameof(JsonSerializerOptions.IncludeFields),
            nameof(JsonSerializerOptions.IndentCharacter),
            nameof(JsonSerializerOptions.IndentSize),
            nameof(JsonSerializerOptions.MaxDepth),
            nameof(JsonSerializerOptions.NewLine),
            nameof(JsonSerializerOptions.NumberHandling),
            nameof(JsonSerializerOptions.PreferredObjectCreationHandling),
            nameof(JsonSerializerOptions.PropertyNameCaseInsensitive),
            nameof(JsonSerializerOptions.PropertyNamingPolicy),
            nameof(JsonSerializerOptions.ReadCommentHandling),
            nameof(JsonSerializerOptions.ReferenceHandler),
            nameof(JsonSerializerOptions.RespectNullableAnnotations),
            nameof(JsonSerializerOptions.RespectRequiredConstructorParameters),
            nameof(JsonSerializerOptions.TypeInfoResolver),
            nameof(JsonSerializerOptions.UnknownTypeHandling),
            nameof(JsonSerializerOptions.UnmappedMemberHandling),
            nameof(JsonSerializerOptions.WriteIndented),
        };

        var settable = typeof(JsonSerializerOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p =>
                p is { CanWrite: true, SetMethod.IsPublic: true } && !Attribute.IsDefined(p, typeof(ObsoleteAttribute))
            )
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        settable.Should().BeEquivalentTo(copied);
    }

    private static void _InvokeCopyTo(MessagingOptions source, MessagingOptions target)
    {
        var copyTo = typeof(MessagingOptions).GetMethod(
            nameof(MessagingOptions.CopyTo),
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            [typeof(MessagingOptions)],
            null
        );
        copyTo!.Invoke(source, [target]);
    }

    private static void _SetNonDefaultValues(MessagingOptions options)
    {
        options.MessageNamePrefix = "messageName-prefix";
        options.Version = "v99";
        options.SucceedMessageExpiredAfter = 1;
        options.FailedMessageExpiredAfter = 2;
        options.InboxCleanupReceiptRetention = TimeSpan.FromDays(1);
        options.InboxCleanupAuditRetention = TimeSpan.FromDays(2);
        options.InboxOperatorReceiptRetention = TimeSpan.FromDays(3);
        options.InboxOperatorAuditRetention = TimeSpan.FromDays(4);
        options.OrphanProbeInterval = TimeSpan.FromSeconds(47);
        options.OrphanProbeBatchSize = 11;
        options.ConsumerThreadCount = 3;
        options.EnableSubscriberParallelExecute = true;
        options.SubscriberParallelExecuteThreadCount = 4;
        options.SubscriberParallelExecuteBufferFactor = 5;
        options.EnablePublishParallelSend = true;
        options.PublishBatchSize = 42;
        options.CollectorCleaningInterval = 6;
        options.SchedulerBatchSize = 7;
        options.RetryBatchSize = 8;
        options.UseStorageLock = true;
        options.TenantContextRequired = true;
        options.TransportPublishTimeout = TimeSpan.FromSeconds(17);
        options.CommandTimeout = TimeSpan.FromSeconds(19);
        options.RetryPolicy.DispatchTimeout = TimeSpan.FromSeconds(23);
    }
}
