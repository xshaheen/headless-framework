// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Messaging.Configuration;

namespace Headless.Messaging.Persistence;

/// <summary>One provider-clock snapshot for a history sweep. Creation timestamps equal to a cutoff are expired.</summary>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct InboxHistoryRetentionCutoffs(
    DateTimeOffset CleanupAudit,
    DateTimeOffset OperatorAudit,
    DateTimeOffset CleanupReceipt,
    DateTimeOffset OperatorReceipt
)
{
    /// <summary>Computes cutoffs using the same clock authority that stamps persisted history.</summary>
    internal static InboxHistoryRetentionCutoffs Create(DateTimeOffset now, MessagingOptions options) =>
        new(
            _Subtract(now, options.InboxCleanupAuditRetention),
            _Subtract(now, options.InboxOperatorAuditRetention),
            _Subtract(now, options.InboxCleanupReceiptRetention),
            _Subtract(now, options.InboxOperatorReceiptRetention)
        );

    // A valid, very long retention means no representable timestamp has expired.
    private static DateTimeOffset _Subtract(DateTimeOffset now, TimeSpan retention) =>
        retention.Ticks > now.UtcTicks ? DateTimeOffset.MinValue : now.Subtract(retention);
}
