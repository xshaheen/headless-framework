// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

/// <summary>
/// The one definition of a terminal stored row, shared by every place that decides it in code. A row is terminal
/// when it succeeded, or failed with no retry scheduled; a failed row that still has <c>NextRetryAt</c> is waiting
/// for its next attempt. The relational providers state the same rule in SQL.
/// </summary>
internal static class MessageTerminalState
{
    public static bool IsTerminal(StatusName? status, DateTimeOffset? nextRetryAt) =>
        status is StatusName.Succeeded or StatusName.Failed && nextRetryAt is null;
}
