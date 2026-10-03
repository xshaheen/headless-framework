// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.UnitOfWork;

namespace Headless.Messaging.Internal;

internal interface IInboxTransactionRunner
{
    /// <summary>
    /// Runs <paramref name="handler" /> inside one inbox transaction, handing it the unit of work enlisted in that
    /// transaction so the consumer's context and the callback publish can join it.
    /// </summary>
    Task ExecuteAsync(
        MediumMessage message,
        Func<IUnitOfWork, CancellationToken, Task> handler,
        CancellationToken cancellationToken
    );
}
