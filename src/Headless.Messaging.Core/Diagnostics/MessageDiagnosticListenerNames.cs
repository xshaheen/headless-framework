// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Diagnostics;

/// <summary>
/// Extension methods on the DiagnosticListener class to log messaging data
/// </summary>
public static class MessageDiagnosticListenerNames
{
    private const string _Prefix = "Headless.Messages.";

    //Tracing
    public const string DiagnosticListenerName = "MessagingDiagnosticListener";

    public const string BeforePublishMessageStore = _Prefix + "WritePublishMessageStoreBefore";
    public const string AfterPublishMessageStore = _Prefix + "WritePublishMessageStoreAfter";
    public const string ErrorPublishMessageStore = _Prefix + "WritePublishMessageStoreError";

    public const string BeforePublish = _Prefix + "WritePublishBefore";
    public const string AfterPublish = _Prefix + "WritePublishAfter";
    public const string ErrorPublish = _Prefix + "WritePublishError";

    public const string BeforeConsume = _Prefix + "WriteConsumeBefore";
    public const string AfterConsume = _Prefix + "WriteConsumeAfter";
    public const string ErrorConsume = _Prefix + "WriteConsumeError";

    public const string BeforeSubscriberInvoke = _Prefix + "WriteSubscriberInvokeBefore";
    public const string AfterSubscriberInvoke = _Prefix + "WriteSubscriberInvokeAfter";
    public const string ErrorSubscriberInvoke = _Prefix + "WriteSubscriberInvokeError";

    //Metrics
    public const string MetricListenerName = _Prefix + "EventCounter";
    public const string PublishedPerSec = "published-per-second";
    public const string ConsumePerSec = "consume-per-second";
    public const string InvokeSubscriberPerSec = "invoke-subscriber-per-second";
    public const string InvokeSubscriberElapsedMs = "invoke-subscriber-elapsed-ms";
}
