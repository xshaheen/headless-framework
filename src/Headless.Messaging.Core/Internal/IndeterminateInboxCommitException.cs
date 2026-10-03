// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.UnitOfWork;

namespace Headless.Messaging.Internal;

internal sealed class IndeterminateInboxCommitException(
    Guid storageId,
    Exception commitException,
    Exception probeException
)
    : InvalidOperationException(
        $"The coordinated commit outcome for inbox attempt '{storageId}' is indeterminate; persisted recovery must resolve it before handler re-entry.",
        new AggregateException(commitException, probeException)
    );
