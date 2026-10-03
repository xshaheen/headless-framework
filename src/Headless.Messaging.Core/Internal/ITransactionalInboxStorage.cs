// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.UnitOfWork;

namespace Headless.Messaging.Internal;

internal interface ITransactionalInboxStorage
{
    ValueTask<bool> CompleteReceivedInboxAsync(
        MediumMessage message,
        DbTransaction transaction,
        CancellationToken cancellationToken
    );

    ValueTask<InboxCommitProbe> ProbeReceivedInboxCommitAsync(
        MediumMessage message,
        CancellationToken cancellationToken
    );
}
