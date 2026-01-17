// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Messages.Diagnostics;

/// <summary>
/// Extension methods on the DiagnosticListener class to log CAP data
/// </summary>
public static class CapDiagnosticListenerNames
{
    private const string _CapPrefix = "DotNetCore.CAP.";

    //Tracing
    public const string DiagnosticListenerName = "CapDiagnosticListener";

    public const string BeforePublishMessageStore = _CapPrefix + "WritePublishMessageStoreBefore";
    public const string AfterPublishMessageStore = _CapPrefix + "WritePublishMessageStoreAfter";
    public const string ErrorPublishMessageStore = _CapPrefix + "WritePublishMessageStoreError";

    public const string BeforePublish = _CapPrefix + "WritePublishBefore";
    public const string AfterPublish = _CapPrefix + "WritePublishAfter";
    public const string ErrorPublish = _CapPrefix + "WritePublishError";

    public const string BeforeConsume = _CapPrefix + "WriteConsumeBefore";
    public const string AfterConsume = _CapPrefix + "WriteConsumeAfter";
    public const string ErrorConsume = _CapPrefix + "WriteConsumeError";

    public const string BeforeSubscriberInvoke = _CapPrefix + "WriteSubscriberInvokeBefore";
    public const string AfterSubscriberInvoke = _CapPrefix + "WriteSubscriberInvokeAfter";
    public const string ErrorSubscriberInvoke = _CapPrefix + "WriteSubscriberInvokeError";

    //Metrics
    public const string MetricListenerName = _CapPrefix + "EventCounter";
    public const string PublishedPerSec = "published-per-second";
    public const string ConsumePerSec = "consume-per-second";
    public const string InvokeSubscriberPerSec = "invoke-subscriber-per-second";
    public const string InvokeSubscriberElapsedMs = "invoke-subscriber-elapsed-ms";
}
