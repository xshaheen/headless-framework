// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.SourceGenerator.Utilities;

/// <summary>Metadata names and file names the Messaging source generator depends on.</summary>
internal static class SourceGeneratorConstants
{
    public const string BusConsumerAttributeMetadataName = "Headless.Messaging.BusConsumerAttribute";
    public const string QueueConsumerAttributeMetadataName = "Headless.Messaging.QueueConsumerAttribute";
    public const string ConsumeInterfaceMetadataName = "Headless.Messaging.IConsume`1";
    public const string SubscriptionHookMetadataName = "Headless.Messaging.IOnSubscriptionEstablished";
    public const string ConsumerLifecycleMetadataName = "Headless.Messaging.IConsumerLifecycle";
    public const string FailurePolicyMetadataName = "Headless.Reliability.FailurePolicy";

    public const string GeneratedFileName = "MessagingModule.g.cs";
}
