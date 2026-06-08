// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AmbientTransactions;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Transactions;

internal interface IMessageOutboxBufferObserver
{
    void MessageBuffered(IAmbientTransaction transaction, MediumMessage message);
}
